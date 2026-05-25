using Sandbox.Definitions;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.Game.World;
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using JetOSExtensions.Shared;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.Utils;
using VRage.Render.Scene;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

namespace CameraLCD
{
    // different ID to avoid conflicting with the original plugin
    [MyTextSurfaceScript(SCRIPT_ID, "Camera Display")]
    public class CameraTSS : MyTSSCommon
    {
        public const string SCRIPT_ID = CamovSurfaceProtocol.CameraDisplayScriptId;

        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update100;
        public DisplayId Id { get; }
        public bool IsActive { get; private set; } = false;

        private readonly MyTerminalBlock _lcd;
        private readonly MyTextPanelComponent _lcdComponent;
        private readonly int _surfaceId;
        private readonly Vector2I _baseTextureSize;

        private string _customData;
        private MyCameraBlock _camera;
        private volatile MySprite[] _externalSprites = Array.Empty<MySprite>();
        private string _lastDrawGate;
        private int _spriteUpdateCount;
        private int _lastLoggedSpriteCount = -1;
        private int _appliedResolutionScalePercent = CamovResolutionScale.DefaultPercent;
        private string _scaledRenderTextureName;
        private Vector2I _scaledRenderTextureSize;
        private bool _scaledRenderTextureCreated;
        private static readonly FieldInfo GeneratedTextureDescField =
            typeof(MyGeneratedTextureManager).Assembly
                .GetType("VRage.Render11.Resources.Internal.MyGeneratedTexture")
                ?.GetField("m_desc", BindingFlags.Instance | BindingFlags.NonPublic);

        // Scratch RTV name — used as the SpritesManager queue key and the pool borrow
        // debug name. The backing resource is borrowed from MyManagers.RwTexturesPool per
        // frame (must be RTV-bindable; FileTextures.CreateGeneratedTexture with
        // generateMipmaps:false returns a SRV-only texture whose Rtv is null, which
        // silently drops every render-target write).
        private string _camovScratchName;

        public CameraTSS(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _lcd = (MyTerminalBlock)block;
            _lcdComponent = (MyTextPanelComponent)surface;
            _surfaceId = GetSurfaceId(_lcd, _lcdComponent);
            _baseTextureSize = _lcdComponent.m_textureSize;
            Id = new DisplayId(_lcd.EntityId, _lcdComponent.Area);

            _lcd.CustomDataChanged += Lcd_CustomDataChanged; // doesn't work if the change occurred locally
            _lcd.IsWorkingChanged += Lcd_IsWorkingChanged;
            _lcd.CubeGridChanged += Lcd_CubeGridChanged;
            _lcd.OnMarkForClose += Lcd_OnMarkForClose;
            UpdateSettings();
        }

        private static int GetSurfaceId(MyTerminalBlock block, MyTextPanelComponent surface)
        {
            if (block is MyTextPanel)
            {
                return 0;
            }
            else
            {
                return surface.Area;
            }
        }

        public override void Run()
        {
            base.Run();

            if (_lcdComponent.Script != SCRIPT_ID)
                return;

            bool customDataChanged = _customData != _lcd.CustomData;
            if (_camera == null || customDataChanged)
            {
                UpdateSettings();
            }
        }

        private void RegisterCamera(MyCameraBlock camera)
        {
            UnregisterCamera("camera-change-before-attach");

            _camera = camera;
            _camera.OnClose += Camera_OnClose;
            _camera.IsWorkingChanged += Camera_IsWorkingChanged;
            _camera.CubeGridChanged += Camera_CubeGridChanged;
            _camera.CustomNameChanged += Camera_CustomNameChanged;
            UpdateIsActive("attach");
            CameraLcdManager.AddDisplay(Id, this);
            _lastDrawGate = "attach";
            LogLifecycle("attach", "camera-bound", camera);
        }

        private void UnregisterCamera(string reason)
        {
            if (_camera != null)
            {
                MyCameraBlock oldCamera = _camera;
                CameraLcdManager.RemoveDisplay(Id);
                IsActive = false;
                _camera.OnClose -= Camera_OnClose;
                _camera.IsWorkingChanged -= Camera_IsWorkingChanged;
                _camera.CubeGridChanged -= Camera_CubeGridChanged;
                _camera.CustomNameChanged -= Camera_CustomNameChanged;
                _camera = null;
                _lastDrawGate = "detach";
                LogLifecycle("detach", reason, oldCamera);
                UpdateIsActive("detach");
            }

            RestoreResolutionScale();
        }

        private void Camera_OnClose(MyEntity obj) => UnregisterCamera("camera-closed");

        private void Lcd_CustomDataChanged(MyTerminalBlock block) => UpdateSettings();

        private void Lcd_IsWorkingChanged(MyCubeBlock block) => UpdateIsActive("lcd-working-changed");

        private void Lcd_CubeGridChanged(VRage.Game.ModAPI.IMyCubeGrid oldGrid) => CubeGridChanged("lcd-grid-changed");

        private void Camera_IsWorkingChanged(MyCubeBlock block) => UpdateIsActive("camera-working-changed");

        private void Camera_CubeGridChanged(VRage.Game.ModAPI.IMyCubeGrid oldGrid) => CubeGridChanged("camera-grid-changed");

        private void Camera_CustomNameChanged(MyTerminalBlock block) => UpdateSettings();

        private void CubeGridChanged(string reason)
        {
            if (_camera != null && !_camera.CubeGrid.IsSameConstructAs(_lcd.CubeGrid))
            {
                UnregisterCamera(reason);
            }
        }

        private void UpdateIsActive(string reason)
        {
            bool wasActive = IsActive;
            IsActive = _camera != null && _camera.IsWorking && _lcd.IsWorking;
            if (wasActive != IsActive)
                LogLifecycle(IsActive ? "active" : "inactive", reason);
        }

        private void UpdateSettings()
        {
            _customData = _lcd.CustomData;

            if (!TryFindCamera(_customData, out MyCameraBlock newCamera))
            {
                _isForced = false;
                UnregisterCamera("camera-not-found");
                return;
            }

            _isForced = CamovSurfaceProtocol.UsesForcedMode(
                _customData,
                _surfaceId,
                commonTssSet: true,
                cameraSelected: true);

            if (_camera == newCamera)
            {
                return;
            }
            
            if (_camera is not null)
            {
                // unregister current camera if changed (and not null)
                UnregisterCamera("camera-changed");
            }

            // is new or changed
            RegisterCamera(newCamera);
        }

        public void UpdateExternalSprites(IReadOnlyList<MySprite> sprites)
        {
            _spriteUpdateCount++;

            MySprite[] frame;
            if (sprites == null || sprites.Count == 0)
            {
                frame = Array.Empty<MySprite>();
            }
            else if (sprites is MySprite[] spriteArray)
            {
                frame = spriteArray;
            }
            else
            {
                frame = new MySprite[sprites.Count];
                for (int i = 0; i < sprites.Count; i++)
                    frame[i] = sprites[i];
            }

            _externalSprites = frame;

            if (frame.Length != _lastLoggedSpriteCount || _spriteUpdateCount <= 3 || (_spriteUpdateCount % 120) == 0)
            {
                _lastLoggedSpriteCount = frame.Length;
                LogLifecycle("sprites", $"update={_spriteUpdateCount}");
            }
        }

        private void LogLifecycle(string action, string reason, MyCameraBlock cameraOverride = null)
        {
            if (!Plugin.Settings.DebugLogging)
                return;

            try
            {
                MyCameraBlock camera = cameraOverride ?? _camera;
                string renderTextureName = GetActiveRenderTextureName();
                bool fileTexture = TryGetRenderTexture(renderTextureName, out IUserGeneratedTexture renderTexture);
                string texLoaded = renderTexture != null ? renderTexture.IsLoaded.ToString() : "<none>";
                string texRtv = renderTexture != null ? (renderTexture.Rtv != null).ToString() : "<none>";
                string texSize = renderTexture != null ? $"{renderTexture.Size.X}x{renderTexture.Size.Y}" : "<none>";
                string scaledSize = _scaledRenderTextureCreated ? $"{_scaledRenderTextureSize.X}x{_scaledRenderTextureSize.Y}" : "<none>";
                string cameraId = camera != null ? camera.EntityId.ToString(CultureInfo.InvariantCulture) : "<none>";
                string cameraName = camera != null ? camera.CustomName?.ToString() ?? "" : "";
                string cameraWorking = camera != null ? camera.IsWorking.ToString() : "<none>";
                string lcdName = _lcd?.CustomName?.ToString() ?? "";
                string script = _lcdComponent.Script ?? "<null>";
                string distance = "<none>";
                MyCamera renderCamera = MySector.MainCamera;
                if (renderCamera != null)
                {
                    distance = renderCamera
                        .GetDistanceFromPoint(_lcd.WorldMatrix.Translation)
                        .ToString("0.0", CultureInfo.InvariantCulture);
                }

                MyLog.Default.WriteLine(
                    $"CAMOV CLIENT: {action} reason={reason ?? "-"} lcd={_lcd.EntityId} area={_surfaceId} " +
                    $"lcdName=\"{lcdName}\" camera={cameraId} cameraName=\"{cameraName}\" forced={_isForced} " +
                    $"active={IsActive} lcdWorking={_lcd.IsWorking} cameraWorking={cameraWorking} " +
                    $"content={_lcdComponent.ContentType} script={script} texGenerated={_lcdComponent.m_textureGenerated} " +
                    $"fileTexture={fileTexture} texName={renderTextureName ?? "<null>"} texSize={texSize} texLoaded={texLoaded} texRtv={texRtv} " +
                    $"lcdTextureSize={_lcdComponent.m_textureSize.X}x{_lcdComponent.m_textureSize.Y} scaled={_scaledRenderTextureCreated} scaledSize={scaledSize} " +
                    $"registered={CameraLcdManager.HasDisplay(_lcd.EntityId, _lcdComponent.Area)} " +
                    $"sprites={_externalSprites.Length} dist={distance}");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"CAMOV CLIENT: lifecycle-log failed action={action} reason={reason}: {ex.Message}");
            }
        }

        public bool TryFindCamera(string customData, out MyCameraBlock camera)
        {
            camera = null;

            if (string.IsNullOrWhiteSpace(customData))
            {
                return false;
            }

            string cameraName = GetCameraName(customData);
            if (!string.IsNullOrWhiteSpace(cameraName))
            {
                return TryFindMechanicallyConnectedCamera(_lcd.CubeGrid, cameraName, out camera);
            }
            else // brute force search
            {
                using StringReader sr = new StringReader(customData);
                while (sr.ReadLine() is string line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    if (TryFindMechanicallyConnectedCamera(_lcd.CubeGrid, line, out camera))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
#nullable enable
        private static bool TryFindMechanicallyConnectedCamera(MyCubeGrid grid, string customName, out MyCameraBlock? result)
        {
            if (TryFindMatch(grid.GetAllCameraBlocks(), customName, out result))
            {
                return true;
            }

            var mechanicalGroup = MyCubeGridGroups.Static.Mechanical.GetGroup(grid);
            if (mechanicalGroup != null)
            {
                foreach (var node in mechanicalGroup.Nodes)
                {
                    if (node.NodeData != grid && TryFindMatch(node.NodeData.GetAllCameraBlocks(), customName, out result))
                    {
                        return true;
                    }
                }
            }

            return false;

            static bool TryFindMatch(List<MyCameraBlock> cameras, string customName, out MyCameraBlock? result)
            {
                foreach (var cameraBlock in cameras)
                {
                    if (cameraBlock.CustomName.EqualsStrFast(customName))
                    {
                        result = cameraBlock;
                        return true;
                    }
                }
                result = null;
                return false;
            }
        }
#nullable disable
        // Forced-mode behavior comes from either explicit CustomData before script selection
        // or from this active common TSS instance once a camera is selected.
        private bool _isForced;
        public bool IsForced => _isForced;

        // Static helper used by CamovShadowManager before selecting the real TSS: checks
        // whether the given CustomData flags the given surfaceId as forced.
        public static bool TryParseForcedForSurface(string customData, int surfaceId)
        {
            return CamovSurfaceProtocol.IsForcedSurface(customData, surfaceId);
        }

        private string GetCameraName(string customData)
        {
            return CamovSurfaceProtocol.GetCameraSelectionName(customData, _surfaceId);
        }

        private struct RendererState
        {
            public bool Lodding;
            public bool DrawBillboards;
            public bool EyeAdaption;
            public bool Flares;
            public bool SSAO;
            public bool Bloom;
            public bool ShadowCameraFrozen;
            public Vector2I ViewportResolution;
            public Vector2I ResolutionI;

            private static readonly RendererState _cameraViewState = new()
            {
                Lodding = false,
                DrawBillboards = true,
                EyeAdaption = true, // when turned off, makes the image too bright when surface is lit by sunlight
                Flares = false,
                SSAO = false,
                Bloom = false,
                ShadowCameraFrozen = true, // don't update shadow camera as it causes flickering for distant shadows
                //ViewportResolution = ,
                //ResolutionI = ,
            };

            public static RendererState GetCameraViewState(Vector2I surfaceResolution)
            {
                return _cameraViewState with
                {
                    ViewportResolution = surfaceResolution,
                    ResolutionI = surfaceResolution,
                };
            }
        }

        private struct CameraState
        {
            public MatrixD ViewMatrix;
            public MatrixD ProjMatrix;
            public MatrixD ProjFarMatrix;
            public float Fov;
            public float NearPlane;
            public float FarPlane;
            public float ProjOffsetX;
            public float ProjOffsetY;
            public Vector3D CameraPos;

            public static CameraState From(MyEnvironmentMatrices matrices)
            {
                return new CameraState
                {
                    ViewMatrix = matrices.ViewD,
                    ProjMatrix = matrices.OriginalProjection,
                    ProjFarMatrix = matrices.OriginalProjectionFar,
                    Fov = matrices.FovH,
                    NearPlane = matrices.NearClipping,
                    FarPlane = matrices.FarClipping,
                    ProjOffsetX = matrices.Projection.M31,
                    ProjOffsetY = matrices.Projection.M32,
                    CameraPos = matrices.CameraPosition,
                };
            }
        }

        public bool Draw()
        {
            try
            {
                return DrawInternal();
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"CAMOV: CameraTSS.Draw threw lcd={_lcd?.EntityId} area={_surfaceId}: {ex}");
                return false;
            }
        }

        private bool OnGate(string gate)
        {
            if (_lastDrawGate != gate)
            {
                _lastDrawGate = gate;
                LogLifecycle("draw-gate", gate);
            }
            return false;
        }

        private void OnDrawSuccess(IUserGeneratedTexture surfaceRtv)
        {
            if (!surfaceRtv.IsLoaded)
            {
                surfaceRtv.SetTextureReady();
                LogLifecycle("texture-ready", "draw-success");
            }

            if (_lastDrawGate == null)
                return;

            string previousGate = _lastDrawGate;
            _lastDrawGate = null;
            ForceRebindAfterRecovery(previousGate);
            LogLifecycle("draw-resume", $"{previousGate} size={surfaceRtv.Size.X}x{surfaceRtv.Size.Y}");
        }

        private void ForceRebindAfterRecovery(string previousGate)
        {
            if (!_isForced)
                return;

            if (previousGate != "Inactive" &&
                previousGate != "TextureNotGenerated" &&
                previousGate != "NoRenderTexture")
                return;

            try
            {
                _lcdComponent.ChangeRenderTexture(_lcdComponent.m_area, GetActiveRenderTextureName(), isForced: true);
                LogLifecycle("forced-rebind", previousGate);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine(
                    $"CAMOV CLIENT: forced-rebind failed lcd={_lcd?.EntityId} area={_surfaceId} reason={previousGate}: {ex}");
            }
        }

        private bool EnsureRenderTargetReady(IUserGeneratedTexture surfaceRtv)
        {
            if (surfaceRtv.Rtv != null && surfaceRtv.Resource != null && surfaceRtv.Srv != null)
                return true;

            try
            {
                surfaceRtv.Reset();
                MyRender11.RC.ClearRtv(surfaceRtv, default);
                LogLifecycle("texture-reset", "missing-rtv");
                return true;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine(
                    $"CAMOV CLIENT: texture-reset failed lcd={_lcd?.EntityId} area={_surfaceId} " +
                    $"texture={surfaceRtv.Name} size={surfaceRtv.Size.X}x{surfaceRtv.Size.Y} " +
                    $"scaled={_scaledRenderTextureCreated}: {ex}");
                if (_scaledRenderTextureCreated)
                    RestoreResolutionScale();
                return false;
            }
        }

        private Vector2I GetDesiredRenderTextureSize()
        {
            int percent = CamovResolutionScale.NormalizePercent(Plugin.Settings.ResolutionScalePercent);
            return new Vector2I(
                CamovResolutionScale.ScaleDimension(_baseTextureSize.X, percent),
                CamovResolutionScale.ScaleDimension(_baseTextureSize.Y, percent));
        }

        private bool EnsureRenderTextureScale(out IUserGeneratedTexture surfaceRtv)
        {
            surfaceRtv = null;
            Vector2I desiredSize = GetDesiredRenderTextureSize();
            int percent = CamovResolutionScale.NormalizePercent(Plugin.Settings.ResolutionScalePercent);
            string textureName = GetVanillaRenderTextureName();
            bool textureMatches = TryGetRenderTexture(textureName, out surfaceRtv) &&
                SameSize(surfaceRtv.Size, desiredSize);
            bool componentMatches = SameSize(_lcdComponent.m_textureSize, desiredSize);

            if (componentMatches && textureMatches)
            {
                SetResolutionScaleState(textureName, desiredSize, percent);
                _lcdComponent.ChangeRenderTexture(_lcdComponent.m_area, textureName, isForced: true);
                return true;
            }

            try
            {
                ApplyResolutionScale(textureName, desiredSize);
                bool ready = TryGetRenderTexture(textureName, out surfaceRtv) &&
                    SameSize(_lcdComponent.m_textureSize, desiredSize) &&
                    SameSize(surfaceRtv.Size, desiredSize);
                if (!ready)
                    throw new InvalidOperationException(
                        $"Render texture was not ready after recreate: {textureName} {desiredSize.X}x{desiredSize.Y}");

                SetResolutionScaleState(textureName, desiredSize, percent);
                _lcdComponent.ChangeRenderTexture(_lcdComponent.m_area, textureName, isForced: true);
                LogLifecycle("resolution-scale", $"{desiredSize.X}x{desiredSize.Y} {CamovResolutionScale.FormatLabel(percent)}");
                return true;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine(
                    $"CAMOV CLIENT: resolution-scale failed lcd={_lcd?.EntityId} area={_surfaceId} " +
                    $"target={desiredSize.X}x{desiredSize.Y}: {ex}");
                RestoreResolutionScale();
                return false;
            }
        }

        private string GetVanillaRenderTextureName()
        {
            return _lcdComponent.GetRenderTextureName();
        }

        private string GetActiveRenderTextureName()
        {
            return _scaledRenderTextureCreated && !string.IsNullOrEmpty(_scaledRenderTextureName)
                ? _scaledRenderTextureName
                : GetVanillaRenderTextureName();
        }

        private static bool SameSize(Vector2I left, Vector2I right)
        {
            return left.X == right.X && left.Y == right.Y;
        }

        private void SetResolutionScaleState(string textureName, Vector2I textureSize, int percent)
        {
            _appliedResolutionScalePercent = percent;

            if (percent == CamovResolutionScale.DefaultPercent)
            {
                _scaledRenderTextureName = null;
                _scaledRenderTextureSize = Vector2I.Zero;
                _scaledRenderTextureCreated = false;
                return;
            }

            _scaledRenderTextureName = textureName;
            _scaledRenderTextureSize = textureSize;
            _scaledRenderTextureCreated = true;
        }

        private void ApplyResolutionScale(string textureName, Vector2I desiredSize)
        {
            DestroyGeneratedRenderTexture(textureName);
            _lcdComponent.m_textureSize = desiredSize;
            IUserGeneratedTexture texture = MyManagers.FileTextures.CreateGeneratedTexture(
                textureName,
                desiredSize.X,
                desiredSize.Y,
                MyGeneratedTextureType.RGBA,
                generateMipmaps: true,
                data: null,
                immediatelyReady: false);
            if (texture == null || !SameSize(texture.Size, desiredSize))
                throw new InvalidOperationException(
                    $"Scaled generated texture was not created: {textureName} {desiredSize.X}x{desiredSize.Y}");
            try
            {
                PrepareScaledRenderTexture(texture);
            }
            catch
            {
                _lcdComponent.m_textureSize = _baseTextureSize;
                DestroyGeneratedRenderTexture(textureName);
                throw;
            }
            _lcdComponent.m_textureGenerated = true;
            _lcdComponent.ChangeRenderTexture(_lcdComponent.m_area, textureName, isForced: true);
        }

        private static void PrepareScaledRenderTexture(IUserGeneratedTexture texture)
        {
            // The public render message accepts numMiplevels:1, but current render messages
            // drop that value before creating the generated texture. Force the descriptor back
            // to the one-mip RTV shape vanilla LCDs expect, then allocate it now.
            if (GeneratedTextureDescField == null)
                throw new InvalidOperationException("Generated texture descriptor field was not found.");

            var desc = (SharpDX.Direct3D11.Texture2DDescription)GeneratedTextureDescField.GetValue(texture);
            desc.MipLevels = 1;
            desc.OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None;
            desc.BindFlags |= SharpDX.Direct3D11.BindFlags.ShaderResource | SharpDX.Direct3D11.BindFlags.RenderTarget;
            desc.Usage = SharpDX.Direct3D11.ResourceUsage.Default;
            GeneratedTextureDescField.SetValue(texture, desc);

            texture.Reset();
            MyRender11.RC.ClearRtv(texture, default);
        }

        private static void DestroyGeneratedRenderTexture(string textureName)
        {
            if (string.IsNullOrEmpty(textureName))
                return;

            if (MyRenderProxy.RenderSystemThread == System.Threading.Thread.CurrentThread)
                MyManagers.FileTextures.DestroyGeneratedTexture(textureName);
            else
                MyRenderProxy.DestroyGeneratedTexture(textureName);
        }

        public void RestoreResolutionScale()
        {
            if (_appliedResolutionScalePercent == CamovResolutionScale.DefaultPercent &&
                SameSize(_lcdComponent.m_textureSize, _baseTextureSize) &&
                !_scaledRenderTextureCreated)
            {
                return;
            }

            try
            {
                _lcdComponent.m_textureSize = _baseTextureSize;
                _lcdComponent.ReleaseTexture(useEmptyTexture: false);
                _lcdComponent.EnsureGeneratedTexture();
                _lcdComponent.ChangeRenderTexture(_lcdComponent.m_area, GetVanillaRenderTextureName(), isForced: true);
                _appliedResolutionScalePercent = CamovResolutionScale.DefaultPercent;
                _scaledRenderTextureName = null;
                _scaledRenderTextureSize = Vector2I.Zero;
                _scaledRenderTextureCreated = false;
                LogLifecycle("resolution-scale", $"restore {_baseTextureSize.X}x{_baseTextureSize.Y}");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine(
                    $"CAMOV CLIENT: resolution-scale restore failed lcd={_lcd?.EntityId} area={_surfaceId}: {ex}");
            }
        }

        private bool DrawInternal()
        {
            if (!IsActive) return OnGate("Inactive");

            MyCamera renderCamera = MySector.MainCamera;
            if (renderCamera is null) return OnGate("NoMainCamera");

            if (renderCamera.GetDistanceFromPoint(_lcd.WorldMatrix.Translation) > Plugin.Settings.Range)
                return OnGate("OutOfRange");

            if (_lcdComponent.ContentType != ContentType.SCRIPT) return OnGate("ContentTypeNotScript");
            if (_lcdComponent.Script != SCRIPT_ID) return OnGate("ScriptMismatch");

            // frustum test
            if (MyRender11.Environment.Matrices.ViewFrustumClippedD.Contains(_lcd.PositionComp.WorldAABB) is ContainmentType.Disjoint)
                return OnGate("Frustum");

            if (!EnsureRenderTextureScale(out IUserGeneratedTexture surfaceRtv))
                return OnGate("NoRenderTexture");

            if (!_scaledRenderTextureCreated && !_lcdComponent.m_textureGenerated) return OnGate("TextureNotGenerated");
            if (!EnsureRenderTargetReady(surfaceRtv))
                return OnGate("RenderTargetResetFailed");

            var originalRendererState = new RendererState
            {
                Lodding = MyCommon.LoddingSettings.Global.IsUpdateEnabled,
                DrawBillboards = MyRender11.Settings.DrawBillboards,
                EyeAdaption = MyRender11.Postprocess.EnableEyeAdaptation,
                Flares = MyRender11.DebugOverrides.Flares,
                SSAO = MyRender11.DebugOverrides.SSAO,
                Bloom = MyRender11.DebugOverrides.Bloom,
                ShadowCameraFrozen = MyRender11.Settings.ShadowCameraFrozen,
                ViewportResolution = MyRender11.ViewportResolution,
                ResolutionI = MyRender11.ResolutionI,
            };

            var originalCameraState = CameraState.From(MyRender11.Environment.Matrices);

            {
                // set state for CameraLCD rendering
                SetRendererState(RendererState.GetCameraViewState(surfaceRtv.Size));
                GetCameraViewMatrixAndPosition(_camera, out MatrixD cameraViewMatrix, out Vector3D cameraPos);
                SetCameraViewMatrix(originalCameraState with
                {
                    ViewMatrix = cameraViewMatrix,
                    Fov = _camera.GetFov(),
                    NearPlane = renderCamera.NearPlaneDistance,
                    FarPlane = renderCamera.FarPlaneDistance,
                    CameraPos = cameraPos,
                    ProjOffsetX = 0,
                    ProjOffsetY = 0,
                }, renderCamera.FarFarPlaneDistance, 1, false);

                CameraViewRenderer.Draw(surfaceRtv);

                // restore camera settings
                SetRendererState(originalRendererState);
                SetCameraViewMatrix(originalCameraState, renderCamera.FarFarPlaneDistance, 0, false);
            }

            // PB-sprite overlay: sprites go to a scratch RTV with alpha, then one fullscreen
            // quad blends scratch over the camera-filled LCD RTV. Early-exits at zero cost
            // if the surface has no sprites.
            CamovComposite(surfaceRtv);

            if (surfaceRtv.MipLevels > 1)
                MyRender11.RC.GenerateMips(surfaceRtv);

            OnDrawSuccess(surfaceRtv);
            return true;
        }

        private static int _camovFrameId;

        private static void BlendScratchOntoLcd(ISrvBindable source, IUserGeneratedTexture dest)
        {
            var rc = MyRender11.RC;
            rc.SetBlendState(MyBlendStateManager.BlendAlphaPremultNoAlphaChannel);  // RGB only — preserves LCD alpha=0
            rc.SetInputLayout(null);
            rc.PixelShader.Set(MyCopyToRT.CopyPs);
            rc.SetRtv(dest);
            rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
            rc.PixelShader.SetSrv(0, source);
            MyScreenPass.DrawFullscreenQuad(rc, new MyViewport(dest.Size.X, dest.Size.Y));
            rc.SetBlendState(null);
            rc.PixelShader.SetSrv(0, null);
            rc.SetRtvNull();
        }

        private void CamovComposite(IUserGeneratedTexture lcdRtv)
        {
            var sprites = _externalSprites;
            if (sprites == null || sprites.Length == 0) return;

            if (_camovScratchName == null)
                _camovScratchName = $"CAMOV_Scratch_{_lcd.EntityId}_{_surfaceId}";

            Vector2I textureSize = _baseTextureSize;
            Vector2I renderTextureSize = lcdRtv.Size;
            Vector2 aspectRatio = _lcdComponent.m_screenAspectRatio;
            Vector2 aspectFactor = MyRenderComponentScreenAreas.CalcAspectFactor(textureSize, aspectRatio);
            Vector2 targetAspectFactor = MyRenderComponentScreenAreas.CalcAspectFactor(renderTextureSize, aspectRatio);
            Vector2 shift = MyRenderComponentScreenAreas.CalcShift(textureSize, aspectFactor);
            Vector2 halfTexture = (Vector2)textureSize * 0.5f;
            Vector2 renderScale = new Vector2(
                renderTextureSize.X / (float)Math.Max(1, textureSize.X),
                renderTextureSize.Y / (float)Math.Max(1, textureSize.Y));

            int frameId = System.Threading.Interlocked.Increment(ref _camovFrameId);
            BuildSpriteMessages(sprites, textureSize, shift, halfTexture, renderScale, _camovScratchName, frameId);

            var messages = MyManagers.SpritesManager.AcquireDrawMessages(_camovScratchName);
            if (messages == null) return;

            IBorrowedRtvTexture scratchRtv = null;
            try
            {
                // Mirror MyOffscreenRenderer.SubmitWork: manually touch sprite source
                // textures so they're in residency before rasterize. The RC-overload of
                // DrawSpritesOffscreen below passes touchTextures:false internally.
                if (messages.Messages != null)
                {
                    foreach (var m in messages.Messages)
                    {
                        var (t1, t2) = m.GetUsedTextures();
                        if (t1 != null) MyManagers.Textures.GetTempTexture(t1, MyFileTextureEnum.GUI, 10000);
                        if (t2 != null) MyManagers.Textures.GetTempTexture(t2, MyFileTextureEnum.GUI, 10000);
                    }
                }

                scratchRtv = MyManagers.RwTexturesPool.BorrowRtv(
                    _camovScratchName, lcdRtv.Size.X, lcdRtv.Size.Y,
                    SharpDX.DXGI.Format.R8G8B8A8_UNorm_SRgb);

                if (scratchRtv == null || scratchRtv.Rtv == null ||
                    scratchRtv.Srv == null || scratchRtv.Resource == null)
                    return;

                // RC-overload matches vanilla MyOffscreenRenderer.RenderWork: passes
                // aspectFactor so `viewportSizeWrittenIntoShaders = texture.Size *
                // aspectRatio`. The pool-overload hardcodes (w,h) and mis-maps sprite
                // positions on non-square surfaces.
                Vector2 aspectForDraw = targetAspectFactor;
                bool ok = MyRender11.DrawSpritesOffscreen(
                    MyRender11.RC, scratchRtv, messages, ref aspectForDraw,
                    new SharpDX.Mathematics.Interop.RawColor4(0f, 0f, 0f, 0f),
                    MyBlendStateManager.BlendAlphaPremult);
                if (!ok) return;

                // Composite scratch over LCD RTV (camera). BlendAlphaPremultNoAlphaChannel:
                // dst.rgb = src.rgb + dst.rgb*(1-src.a); alpha channel mask off so LCD
                // alpha=0 is preserved (LCD mesh shader expects that).
                BlendScratchOntoLcd(scratchRtv, lcdRtv);
            }
            finally
            {
                scratchRtv?.Release();
                MyManagers.SpritesManager.DisposeDrawMessages(messages);
            }
        }

        private void BuildSpriteMessages(MySprite[] sprites, Vector2I textureSize,
            Vector2 shift, Vector2 halfTexture, Vector2 renderScale, string targetName, int frameId)
        {
            bool hasScissor = false;
            int count = sprites.Length;
            for (int i = 0; i < count; i++)
            {
                MySprite sprite = sprites[i];
                Vector2 size = (sprite.Size ?? (Vector2)textureSize) * renderScale;
                Vector2 position = (sprite.Position ?? halfTexture) + shift;
                position *= renderScale;
                Color color = sprite.Color ?? Color.White;

                switch (sprite.Type)
                {
                    case SpriteType.TEXTURE:
                    {
                        var def = MyDefinitionManager.Static.GetDefinition<MyLCDTextureDefinition>(MyStringHash.GetOrCompute(sprite.Data));
                        string path = def?.SpritePath ?? def?.TexturePath;
                        if (path == null) break;
                        switch (sprite.Alignment)
                        {
                            case TextAlignment.LEFT:  position += new Vector2(size.X * 0.5f, 0f); break;
                            case TextAlignment.RIGHT: position -= new Vector2(size.X * 0.5f, 0f); break;
                        }
                        Vector2 rightVec = new Vector2(1f, 0f);
                        if (Math.Abs(sprite.RotationOrScale) > 1e-5f)
                            rightVec = new Vector2((float)Math.Cos(sprite.RotationOrScale), (float)Math.Sin(sprite.RotationOrScale));

                        var msg = MyRenderProxy.MessagePool.Get<MyRenderMessageDrawSpriteAtlas>(MyRenderMessageEnum.DrawSpriteAtlas);
                        msg.Texture = path;
                        msg.Position = position;
                        msg.TextureOffset = Vector2.Zero;
                        msg.TextureSize = Vector2.One;
                        msg.RightVector = rightVec;
                        msg.Scale = Vector2.One;
                        msg.Color = color;
                        msg.HalfSize = size * 0.5f;
                        msg.TargetTexture = targetName;
                        msg.IgnoreBounds = false;
                        MyManagers.SpritesManager.AddMessage(msg, frameId);
                        msg.Dispose();
                        break;
                    }
                    case SpriteType.TEXT:
                    {
                        switch (sprite.Alignment)
                        {
                            case TextAlignment.RIGHT:  position -= new Vector2(size.X, 0f); break;
                            case TextAlignment.CENTER: position -= new Vector2(size.X * 0.5f, 0f); break;
                        }
                        var fontDef = MyDefinitionManager.Static.GetDefinition<MyFontDefinition>(MyStringHash.GetOrCompute(sprite.FontId));
                        int widthPx = (int)Math.Round(size.X);
                        int fontIdx = (int)(fontDef?.Id.SubtypeId ?? MyStringHash.GetOrCompute("Debug"));

                        var msg = MyRenderProxy.MessagePool.Get<MyRenderMessageDrawStringAligned>(MyRenderMessageEnum.DrawStringAligned);
                        msg.Text = sprite.Data ?? string.Empty;
                        msg.FontIndex = fontIdx;
                        msg.ScreenCoord = position;
                        msg.ColorMask = color;
                        msg.ScreenScale = sprite.RotationOrScale * renderScale.X;
                        msg.ScreenMaxWidth = float.PositiveInfinity;
                        msg.TargetTexture = targetName;
                        msg.TextureWidthInPx = widthPx;
                        msg.Alignment = (MyRenderTextAlignmentEnum)sprite.Alignment;
                        msg.IgnoreBounds = false;
                        MyManagers.SpritesManager.AddMessage(msg, frameId);
                        msg.Dispose();
                        break;
                    }
                    case SpriteType.CLIP_RECT:
                        if (sprite.Position.HasValue && sprite.Size.HasValue)
                        {
                            if (hasScissor) AddScissorPop(targetName, frameId);
                            else hasScissor = true;
                            AddScissorPush(targetName, frameId, new Rectangle(
                                (int)Math.Round(position.X),
                                (int)Math.Round(position.Y),
                                (int)Math.Round(size.X),
                                (int)Math.Round(size.Y)));
                        }
                        else if (hasScissor)
                        {
                            AddScissorPop(targetName, frameId);
                            hasScissor = false;
                        }
                        break;
                }
            }
            if (hasScissor) AddScissorPop(targetName, frameId);
        }

        private static void AddScissorPush(string targetName, int frameId, Rectangle rect)
        {
            var msg = MyRenderProxy.MessagePool.Get<MyRenderMessageSpriteScissorPush>(MyRenderMessageEnum.SpriteScissorPush);
            msg.ScreenRectangle = rect;
            msg.TargetTexture = targetName;
            MyManagers.SpritesManager.AddMessage(msg, frameId);
            msg.Dispose();
        }

        private static void AddScissorPop(string targetName, int frameId)
        {
            var msg = MyRenderProxy.MessagePool.Get<MyRenderMessageSpriteScissorPop>(MyRenderMessageEnum.SpriteScissorPop);
            msg.TargetTexture = targetName;
            MyManagers.SpritesManager.AddMessage(msg, frameId);
            msg.Dispose();
        }

        private static void SetRendererState(RendererState state)
        {
            SetLoddingEnabled(state.Lodding);
            MyRender11.Settings.DrawBillboards = state.DrawBillboards;
            MyRender11.Postprocess.EnableEyeAdaptation = state.EyeAdaption;
            MyRender11.DebugOverrides.Flares = state.Flares;
            MyRender11.DebugOverrides.SSAO = state.SSAO;
            MyRender11.DebugOverrides.Bloom = state.Bloom;
            MyRender11.Settings.ShadowCameraFrozen = state.ShadowCameraFrozen;

            MyRender11.ViewportResolution = state.ViewportResolution;
            MyRender11.m_resolution = state.ResolutionI;

            static bool SetLoddingEnabled(bool enabled)
            {
                // Reference: MyRender11.ProcessMessageInternal(MyRenderMessageBase message, int frameId)
                //              case MyRenderMessageEnum.UpdateNewLoddingSettings

                MyNewLoddingSettings loddingSettings = MyCommon.LoddingSettings;
                MyGlobalLoddingSettings globalSettings = loddingSettings.Global;
                bool initial = globalSettings.IsUpdateEnabled;
                if (initial == enabled)
                    return initial;

                globalSettings.IsUpdateEnabled = enabled;
                loddingSettings.Global = globalSettings;
                MyManagers.GeometryRenderer.IsLodUpdateEnabled = enabled;
                MyManagers.GeometryRenderer.m_globalLoddingSettings = globalSettings;
                MyManagers.ModelFactory.OnLoddingSettingChanged();
                return initial;
            }
        }

        private static void SetCameraViewMatrix(CameraState state, float farFarPlane, int lastMomentUpdateIndex, bool smooth)
        {
            MyRenderMessageSetCameraViewMatrix renderMessage = null;
            try
            {
                renderMessage = MyRenderProxy.MessagePool.Get<MyRenderMessageSetCameraViewMatrix>(MyRenderMessageEnum.SetCameraViewMatrix);
                renderMessage.ViewMatrix = state.ViewMatrix;
                renderMessage.ProjectionMatrix = state.ProjMatrix;
                renderMessage.ProjectionFarMatrix = state.ProjFarMatrix;
                renderMessage.FOV = state.Fov;
                renderMessage.FOVForSkybox = state.Fov;
                renderMessage.NearPlane = state.NearPlane;
                renderMessage.FarPlane = state.FarPlane;
                renderMessage.FarFarPlane = farFarPlane;
                renderMessage.CameraPosition = state.CameraPos;
                renderMessage.LastMomentUpdateIndex = lastMomentUpdateIndex;
                renderMessage.ProjectionOffsetX = state.ProjOffsetX;
                renderMessage.ProjectionOffsetY = state.ProjOffsetY;
                renderMessage.Smooth = smooth;
                MyRender11.SetupCameraMatrices(renderMessage);
            }
            finally
            {
                renderMessage?.Dispose();
            }
        }

        private static void GetCameraViewMatrixAndPosition(MyCameraBlock camera, out MatrixD viewMatrix, out Vector3D position)
        {
            // same as MyCameraBlock.GetViewMatrix() but using a custom matrix

            // use camera's render object matrix (if available) since the entity's simulation matrix may be desynced
            MatrixD matrix = TryGetActor(camera, out MyActor actor) ? actor.WorldMatrix : camera.WorldMatrix;
            matrix.Translation += matrix.Forward * 0.2;
            
            if (camera.Model.Dummies != null)
            {
                foreach (KeyValuePair<string, MyModelDummy> dummy in camera.Model.Dummies)
                {
                    if (dummy.Value.Name == "camera")
                    {
                        Quaternion rotation = Quaternion.CreateFromForwardUp(matrix.Forward, matrix.Up);
                        matrix.Translation += MatrixD.Transform(dummy.Value.Matrix, rotation).Translation;
                        break;
                    }
                }
            }

            position = matrix.Translation;
            MatrixD.Invert(ref matrix, out viewMatrix);
        }

        private static bool TryGetActor(MyEntity entity, out MyActor actor)
        {
            actor = null;
            if (entity?.Render is not MyRenderComponentBase renderComp)
            {
                return false;
            }

            try
            {
                uint actorId = renderComp.GetRenderObjectID();
                actor = actorId != uint.MaxValue ? MyIDTracker<MyActor>.FindByID(actorId) : null;
                return actor != null;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetRenderTexture(out IUserGeneratedTexture texture)
        {
            try
            {
                return TryGetRenderTexture(GetActiveRenderTextureName(), out texture);
            }
            catch (NullReferenceException)
            {
                texture = null;
                return false;
            }
        }

        private static bool TryGetRenderTexture(string name, out IUserGeneratedTexture texture)
        {
            if (string.IsNullOrEmpty(name))
            {
                texture = null;
                return false;
            }
            return MyManagers.FileTextures.TryGetTexture(name, out texture) && texture != null;
        }

        private void Lcd_OnMarkForClose(MyEntity obj) => Dispose();

        public override void Dispose()
        {
            base.Dispose();

            UnregisterCamera("dispose");
            _lcd.CustomDataChanged -= Lcd_CustomDataChanged;
            _lcd.IsWorkingChanged -= Lcd_IsWorkingChanged;
            _lcd.CubeGridChanged -= Lcd_CubeGridChanged;
            _lcd.OnMarkForClose -= Lcd_OnMarkForClose;
        }
    }
}
