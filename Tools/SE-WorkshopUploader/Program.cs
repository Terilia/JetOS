using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Steamworks;

namespace SEWorkshopUploader
{
    // Minimal Steam Workshop uploader using Steamworks.NET wrapper.
    // Uses SE's app id (244850) so the user's owned Steam install handles auth.
    // Steam must be running and logged in.
    static class Program
    {
        const uint SE_APP_ID = 244850;

        static PublishedFileId_t _newItemId = PublishedFileId_t.Invalid;
        static UGCUpdateHandle_t _updateHandle;
        static bool _createDone, _updateDone;
        static EResult _createResult, _updateResult;
        static bool _needsLegalAgreement;

        static int Main(string[] args)
        {
            if (args.Length < 5)
            {
                Console.Error.WriteLine(
                    "Usage:\n" +
                    "  SE-WorkshopUploader <contentFolder> <thumbPath> <title> <description> <visibility> [tags] [--update <itemId>] [--changenote <text>]\n\n" +
                    "  visibility   : 0=Public  1=FriendsOnly  2=Private  3=Unlisted\n" +
                    "  tags         : comma-separated, e.g. \"Other,Modpack\" (default: Other)\n" +
                    "  --update <id>: patch existing Workshop item instead of creating a new one\n" +
                    "  --changenote : Workshop changelog entry (default: \"Initial upload\" / \"Update\")");
                return 1;
            }

            string contentFolder = Path.GetFullPath(args[0]);
            string thumbPath     = Path.GetFullPath(args[1]);
            string title         = args[2];
            string description   = args[3];
            var visibility       = (ERemoteStoragePublishedFileVisibility)int.Parse(args[4]);

            var tags = new List<string> { "Other" };
            ulong existingItemId = 0;
            string changenote = null;
            for (int i = 5; i < args.Length; i++)
            {
                if (args[i] == "--update" && i + 1 < args.Length)
                {
                    existingItemId = ulong.Parse(args[++i]);
                }
                else if (args[i] == "--changenote" && i + 1 < args.Length)
                {
                    changenote = args[++i];
                }
                else if (!args[i].StartsWith("--"))
                {
                    // First bare arg after visibility is the tags list.
                    tags = new List<string>(args[i].Split(','));
                }
            }
            if (changenote == null)
                changenote = existingItemId != 0 ? "Update" : "Initial upload";

            if (!Directory.Exists(contentFolder))
            {
                Console.Error.WriteLine($"Content folder not found: {contentFolder}");
                return 1;
            }

            // SteamAPI.Init reads steam_appid.txt from CWD when the process is not
            // launched through Steam. Write it next to the executable to be safe.
            string appIdPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steam_appid.txt");
            File.WriteAllText(appIdPath, SE_APP_ID.ToString());

            if (!SteamAPI.Init())
            {
                Console.Error.WriteLine(
                    "SteamAPI.Init failed.\n" +
                    "  - Is Steam running and logged in?\n" +
                    "  - Do you own Space Engineers (App 244850)?\n" +
                    "  - Is steam_appid.txt next to the .exe? (we just wrote it)");
                return 2;
            }

            try
            {
                Console.WriteLine($"[upload] Steam OK — user: {SteamFriends.GetPersonaName()}");
                Console.WriteLine($"[upload] Mode        : {(existingItemId != 0 ? $"UPDATE existing item {existingItemId}" : "CREATE new item")}");
                Console.WriteLine($"[upload] Content     : {contentFolder}");
                Console.WriteLine($"[upload] Thumb       : {thumbPath}");
                Console.WriteLine($"[upload] Title       : {title}");
                Console.WriteLine($"[upload] Visibility  : {visibility}");
                Console.WriteLine($"[upload] Tags        : {string.Join(", ", tags)}");
                Console.WriteLine($"[upload] Changenote  : {changenote}");
                Console.WriteLine();

                PublishedFileId_t targetItem;
                if (existingItemId != 0)
                {
                    targetItem = new PublishedFileId_t(existingItemId);
                }
                else
                {
                    // 1) CreateItem — get a fresh PublishedFileId.
                    // CreateItem is an async call; result comes via CallResult, not Callback.
                    var createCallResult = CallResult<CreateItemResult_t>.Create(OnCreate);
                    SteamAPICall_t hCreate = SteamUGC.CreateItem(new AppId_t(SE_APP_ID), EWorkshopFileType.k_EWorkshopFileTypeCommunity);
                    createCallResult.Set(hCreate);

                    Console.WriteLine("[upload] Waiting for CreateItem...");
                    if (!WaitFor(() => _createDone, timeoutMs: 30000, pollMs: 100))
                    {
                        Console.Error.WriteLine("[upload] CreateItem timed out.");
                        return 3;
                    }
                    if (_createResult != EResult.k_EResultOK)
                    {
                        Console.Error.WriteLine($"[upload] CreateItem failed: {_createResult}");
                        return 4;
                    }
                    targetItem = _newItemId;
                    Console.WriteLine($"[upload] Created item: {targetItem.m_PublishedFileId}");
                }
                ulong itemId = targetItem.m_PublishedFileId;

                // 2) StartItemUpdate + Set* + SubmitItemUpdate — push content
                _updateHandle = SteamUGC.StartItemUpdate(new AppId_t(SE_APP_ID), targetItem);
                SteamUGC.SetItemTitle(_updateHandle, title);
                SteamUGC.SetItemDescription(_updateHandle, description);
                SteamUGC.SetItemVisibility(_updateHandle, visibility);
                SteamUGC.SetItemContent(_updateHandle, contentFolder);
                if (File.Exists(thumbPath))
                    SteamUGC.SetItemPreview(_updateHandle, thumbPath);
                else
                    Console.WriteLine($"[upload] (no thumbnail at {thumbPath} — skipping)");
                SteamUGC.SetItemTags(_updateHandle, tags);

                var submitCallResult = CallResult<SubmitItemUpdateResult_t>.Create(OnUpdate);
                SteamAPICall_t hSubmit = SteamUGC.SubmitItemUpdate(_updateHandle, changenote);
                submitCallResult.Set(hSubmit);

                Console.WriteLine("[upload] Submitting update...");
                int ticks = 0;
                while (!_updateDone)
                {
                    SteamAPI.RunCallbacks();
                    Thread.Sleep(500);
                    ticks++;

                    ulong processed, total;
                    EItemUpdateStatus status = SteamUGC.GetItemUpdateProgress(_updateHandle, out processed, out total);
                    if (ticks % 2 == 0)
                        Console.WriteLine($"[upload]   [{status}] {processed}/{total} bytes");

                    if (ticks > 600)  // 5 minutes
                    {
                        Console.Error.WriteLine("[upload] Update timed out (5 min).");
                        return 5;
                    }
                }

                if (_updateResult != EResult.k_EResultOK)
                {
                    Console.Error.WriteLine($"[upload] Update failed: {_updateResult}");
                    return 6;
                }

                Console.WriteLine();
                Console.WriteLine("[upload] ===== SUCCESS =====");
                Console.WriteLine($"[upload] Item ID    : {itemId}");
                Console.WriteLine($"[upload] URL        : https://steamcommunity.com/sharedfiles/filedetails/?id={itemId}");
                Console.WriteLine($"[upload] Visibility : {visibility}");
                if (_needsLegalAgreement)
                {
                    Console.WriteLine();
                    Console.WriteLine("[upload] !! Steam Workshop Legal Agreement not yet accepted.");
                    Console.WriteLine("[upload]    Open the URL above and click \"I Agree\" on the agreement banner");
                    Console.WriteLine("[upload]    or the item will stay hidden even when set Public.");
                }
                return 0;
            }
            finally
            {
                SteamAPI.Shutdown();
            }
        }

        static bool WaitFor(Func<bool> cond, int timeoutMs, int pollMs)
        {
            int waited = 0;
            while (!cond() && waited < timeoutMs)
            {
                SteamAPI.RunCallbacks();
                Thread.Sleep(pollMs);
                waited += pollMs;
            }
            return cond();
        }

        static void OnCreate(CreateItemResult_t r, bool bIOFailure)
        {
            if (bIOFailure)
            {
                Console.Error.WriteLine("[upload] CreateItem IO failure.");
                _createResult = EResult.k_EResultIOFailure;
                _createDone = true;
                return;
            }
            _createResult = r.m_eResult;
            _newItemId = r.m_nPublishedFileId;
            _needsLegalAgreement = r.m_bUserNeedsToAcceptWorkshopLegalAgreement;
            _createDone = true;
        }

        static void OnUpdate(SubmitItemUpdateResult_t r, bool bIOFailure)
        {
            if (bIOFailure)
            {
                Console.Error.WriteLine("[upload] SubmitItemUpdate IO failure.");
                _updateResult = EResult.k_EResultIOFailure;
                _updateDone = true;
                return;
            }
            _updateResult = r.m_eResult;
            if (r.m_bUserNeedsToAcceptWorkshopLegalAgreement) _needsLegalAgreement = true;
            _updateDone = true;
        }
    }
}
