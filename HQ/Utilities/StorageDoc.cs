using System.Collections.Generic;
using System.Text;

namespace IngameScript
{
    partial class Program
    {
        // Shared owner of the PB's single `Storage` string. Several subsystems need to persist
        // small key=value settings (HQConfig thresholds, ZoneStore zones), but `Storage` is one
        // string — if each writer rewrote the whole thing they'd clobber each other. StorageDoc
        // parses Storage once into a dictionary; every writer Set()s only its own (prefixed) keys
        // and Flush() reserializes the union. Init() must run before any consumer Loads.
        static class StorageDoc
        {
            static readonly Dictionary<string, string> _kv = new Dictionary<string, string>();
            static Program _p;

            public static void Init(Program p)
            {
                _p = p;
                _kv.Clear();
                string s = p.Storage ?? "";
                if (s.Length == 0) return;
                string[] lines = s.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    int eq = lines[i].IndexOf('=');
                    if (eq <= 0) continue;
                    _kv[lines[i].Substring(0, eq)] = lines[i].Substring(eq + 1);
                }
            }

            public static bool TryGet(string key, out string val) => _kv.TryGetValue(key, out val);
            public static void Set(string key, string val) { _kv[key] = val; }
            public static void Remove(string key) { _kv.Remove(key); }

            public static List<string> KeysWithPrefix(string prefix)
            {
                var r = new List<string>();
                foreach (var kv in _kv)
                    if (kv.Key.Length >= prefix.Length && kv.Key.Substring(0, prefix.Length) == prefix)
                        r.Add(kv.Key);
                return r;
            }

            public static void Flush()
            {
                if (_p == null) return;
                var sb = new StringBuilder();
                bool first = true;
                foreach (var kv in _kv)
                {
                    if (!first) sb.Append('\n');
                    sb.Append(kv.Key).Append('=').Append(kv.Value);
                    first = false;
                }
                _p.Storage = sb.ToString();
            }
        }
    }
}
