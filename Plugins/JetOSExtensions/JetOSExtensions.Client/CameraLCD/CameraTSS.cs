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
using System.IO;
using System.Linq;
using System.Text;
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
        public const string SCRIPT_ID = "TSS_CameraDisplay_2";

        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update100;
        public DisplayId Id { get; }
        public bool IsActive { get; private set; } = false;

        private readonly MyTerminalBlock _lcd;
        private readonly MyTextPanelComponent _lcdComponent;
        private readonly int _surfaceId;

        private string _customData;
        private MyCameraBlock _camera;

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
            Id = new DisplayId(_lcd.EntityId, _lcdComponent.Area);

            _lcd.CustomDataChanged += _ => UpdateSettings(); // doesn't work if the change occurred locally
            _lcd.IsWorkingChanged += _ => UpdateIsActive();
            _lcd.CubeGridChanged += _ => CubeGridChanged();
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
            UnregisterCamera();

            _camera = camera;
            _camera.OnClose += Camera_OnClose;
            _camera.IsWorkingChanged += _ => UpdateIsActive();
            _camera.CubeGridChanged += _ => CubeGridChanged();
            _camera.CustomNameChanged += _ => UpdateSettings();
            UpdateIsActive();
            CameraLcdManager.AddDisplay(Id, this);
        }

        private void UnregisterCamera()
        {
            if (_camera != null)
            {
                CameraLcdManager.RemoveDisplay(Id);
                IsActive = false;
                _camera.OnClose -= Camera_OnClose;
                _camera.IsWorkingChanged -= _ => UpdateIsActive();
                _camera.CubeGridChanged -= _ => CubeGridChanged();
                _camera.CustomNameChanged -= _ => UpdateSettings();
                _camera = null;
                UpdateIsActive();
            }
        }

        private void Camera_OnClose(MyEntity obj) => UnregisterCamera();

        private void CubeGridChanged()
        {
            if (_camera != null && !_camera.CubeGrid.IsSameConstructAs(_lcd.CubeGrid))
            {
                UnregisterCamera();
            }
        }

        private void UpdateIsActive()
        {
            IsActive = _camera != null && _camera.IsWorking && _lcd.IsWorking;
        }

        private void UpdateSettings()
        {
            _customData = _lcd.CustomData;

            if (!TryFindCamera(_customData, out MyCameraBlock newCamera))
            {
                UnregisterCamera();
                return;
            }

            if (_camera == newCamera)
            {
                return;
            }
            
            if (_camera is not null)
            {
                // unregister current camera if changed (and not null)
                UnregisterCamera();
            }

            // is new or changed
            RegisterCamera(newCamera);
        }

        // Shadow TSS instances never receive Run() ticks (SE only ticks TSS owned by a
        // SCRIPT surface). If _camera goes null during the away-period — e.g. camera
        // streamed out in MP, grid split, or a missed CustomDataChanged on a local edit —
        // nothing re-binds it. CamovShadowManager calls this once per scan (60 ticks) so
        // the shadow self-heals on return.
        public void RefreshIfBroken()
        {
            if (_camera == null || _customData != _lcd.CustomData)
                UpdateSettings();
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
        // "Forced" flag from CustomData — e.g. "0:Eye:Forced" on surface 0. When set,
        // CameraTSS.DrawInternal bypasses the ContentType == SCRIPT check and force-binds
        // the surface material to our RTV, so the camera view still composites even when
        // the player keeps ContentType = NONE / TEXT_AND_IMAGE.
        private bool _isForced;
        public bool IsForced => _isForced;

        // Static helper used by CamovShadowManager before constructing a CameraTSS: checks
        // whether the given CustomData flags the given surfaceId as forced.
        public static bool TryParseForcedForSurface(string customData, int surfaceId)
        {
            if (string.IsNullOrWhiteSpace(customData)) return false;
            string prefix = surfaceId + ":";
            using (StringReader reader = new StringReader(customData))
            {
                while (reader.ReadLine() is string line)
                {
                    if (!line.StartsWith(prefix) || line.Length <= prefix.Length) continue;
                    string rest = line.Substring(prefix.Length);
                    foreach (var seg in rest.Split(':'))
                        if (seg.Trim().Equals(CamovShadowManager.ForcedMarker, StringComparison.OrdinalIgnoreCase))
                            return true;
                }
            }
            return false;
        }

        private string GetCameraName(string customData)
        {
            _isForced = false;
            if (String.IsNullOrWhiteSpace(customData))
                return null;

            string prefix = _surfaceId + ":";
            using (StringReader reader = new StringReader(customData))
            {
                while (reader.ReadLine() is string line)
                {
                    if (line.StartsWith(prefix) && line.Length > prefix.Length)
                    {
                        string rest = line.Substring(prefix.Length);
                        // Split "<name>:<flag>[:<flag>...]"; first segment is the camera name,
                        // any later segment equal to "Forced" sets the force-bind flag.
                        var segs = rest.Split(':');
                        string name = segs[0].Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        for (int i = 1; i < segs.Length; i++)
                            if (segs[i].Trim().Equals(CamovShadowManager.ForcedMarker, StringComparison.OrdinalIgnoreCase))
                                _isForced = true;
                        return name;
                    }
                }
            }
            return null;
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

        // Diagnostic: last failure gate for this shadow ("" = drawing OK). We log only on
        // transitions so each shadow emits 1-2 lines per away/return cycle, not every frame.
        private string _lastGate = "";

        private bool OnGate(string gate)
        {
            if (_lastGate != gate)
            {
                _lastGate = gate;
                MyLog.Default.WriteLine(
                    $"CAMOV: Draw gated lcd={_lcd.EntityId} area={_surfaceId} forced={_isForced} " +
                    $"texGen={_lcdComponent.m_textureGenerated} ct={_lcdComponent.ContentType} " +
                    $"script={_lcdComponent.Script} camBound={_camera != null} gate={gate}");
            }
            return false;
        }

        private void OnSuccess()
        {
            if (_lastGate.Length != 0)
            {
                MyLog.Default.WriteLine(
                    $"CAMOV: Draw resumed lcd={_lcd.EntityId} area={_surfaceId} afterGate={_lastGate}");
                _lastGate = "";
            }
        }

        // SE's scene-cull path tears down the offscreen file texture but does not always
        // reset MyTextPanelComponent.m_textureGenerated in lockstep. On return the flag
        // stays true, so EnsureGeneratedTexture early-exits and never recreates the
        // texture — and TryGetTexture fails because the FileTextures entry is gone.
        //
        // Repair: if the flag is false, call Ensure normally. If the flag is true but
        // FileTextures has no matching entry, force a Release→Ensure cycle to resync.
        private void EnsureSurfaceTexture()
        {
            if (!_lcdComponent.m_textureGenerated)
            {
                _lcdComponent.EnsureGeneratedTexture();
                return;
            }

            string name;
            try { name = _lcdComponent.GetRenderTextureName(); }
            catch (NullReferenceException) { return; }

            if (MyManagers.FileTextures.TryGetTexture(name, out IUserGeneratedTexture tex) && tex != null)
                return;

            MyLog.Default.WriteLine(
                $"CAMOV: texture flag/FileTextures desync — force regen lcd={_lcd.EntityId} area={_surfaceId}");
            _lcdComponent.ReleaseTexture(useEmptyTexture: false);
            _lcdComponent.EnsureGeneratedTexture();
        }

        private bool _wasInRange;

        // Shrink our effective range by a small margin so our range-exit release fires
        // strictly before SE's scene-cull kicks in. Without this, we race SE: if SE
        // destroys the file texture first, our state desyncs (flag stays true, no
        // FileTextures entry) and the shadow gets stuck in the "Online" fallback on
        // return. The exact distance where SE culls an LCD block depends on LOD/LOD
        // factor and isn't easy to read; 1m is a conservative head-start.
        private const float CullSafetyMarginMeters = 1.0f;

        private bool DrawInternal()
        {
            if (!IsActive) return OnGate("IsActive=false");

            MyCamera renderCamera = MySector.MainCamera;
            if (renderCamera is null) return OnGate("NoMainCamera");

            float effectiveRange = Math.Max(1f, Plugin.Settings.Range - CullSafetyMarginMeters);
            bool inRange = renderCamera.GetDistanceFromPoint(_lcd.WorldMatrix.Translation) <= effectiveRange;
            _wasInRange = inRange;

            if (!inRange) return OnGate("OutOfRange");

            if (_isForced)
            {
                // Forced mode — render regardless of ContentType/Script. Ensure the surface
                // texture exists and the material points at it (SE's TSS lifecycle does this
                // normally; we do it here because the user has deliberately kept ContentType
                // at NONE/TEXT_AND_IMAGE).
                EnsureSurfaceTexture();
                if (!_lcdComponent.m_textureGenerated) return OnGate("TexGenForcedFailed");
                // isForced:true so SE's scene-add material-reset logic can't clobber our binding.
                _lcdComponent.ChangeRenderTexture(_lcdComponent.m_area, _lcdComponent.GetRenderTextureName(), isForced: true);
            }
            else
            {
                // Classic mode — only take over surfaces where the user has picked our
                // Camera Display script. SE's TSS lifecycle handles the material binding.
                if (!_lcdComponent.m_textureGenerated) return OnGate("TexGenClassicFalse");
                if (_lcdComponent.ContentType != ContentType.SCRIPT) return OnGate("ContentTypeNotScript");
                if (_lcdComponent.Script != SCRIPT_ID) return OnGate("ScriptMismatch");
            }

            // frustum test
            if (MyRender11.Environment.Matrices.ViewFrustumClippedD.Contains(_lcd.PositionComp.WorldAABB) is ContainmentType.Disjoint)
                return OnGate("Frustum");

            if (!TryGetRenderTexture(out IUserGeneratedTexture surfaceRtv))
                return OnGate("NoRenderTexture");

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

            MyRender11.RC.GenerateMips(surfaceRtv);

            OnSuccess();
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
            var sprites = _lcdComponent.m_renderLayers;
            if (sprites == null || sprites.Count == 0) return;

            if (_camovScratchName == null)
                _camovScratchName = $"CAMOV_Scratch_{_lcd.EntityId}_{_surfaceId}";

            Vector2I textureSize = _lcdComponent.m_textureSize;
            Vector2 aspectRatio = _lcdComponent.m_screenAspectRatio;
            Vector2 aspectFactor = MyRenderComponentScreenAreas.CalcAspectFactor(textureSize, aspectRatio);
            Vector2 shift = MyRenderComponentScreenAreas.CalcShift(textureSize, aspectFactor);
            Vector2 halfTexture = (Vector2)textureSize * 0.5f;

            int frameId = System.Threading.Interlocked.Increment(ref _camovFrameId);
            BuildSpriteMessages(sprites, textureSize, shift, halfTexture, _camovScratchName, frameId);

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
                Vector2 aspectForDraw = aspectFactor;
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

        private void BuildSpriteMessages(System.Collections.Generic.List<MySprite> sprites, Vector2I textureSize,
            Vector2 shift, Vector2 halfTexture, string targetName, int frameId)
        {
            bool hasScissor = false;
            int count = sprites.Count;
            for (int i = 0; i < count; i++)
            {
                MySprite sprite = sprites[i];
                Vector2 size = sprite.Size ?? (Vector2)textureSize;
                Vector2 position = sprite.Position ?? halfTexture;
                Color color = sprite.Color ?? Color.White;
                position += shift;

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
                        msg.ScreenScale = sprite.RotationOrScale;
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
                            AddScissorPush(targetName, frameId, new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y));
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

        private static int _camovOverlayFrameId;

        private void DrawPbSpritesSynchronously(IUserGeneratedTexture target)
        {
            var sprites = _lcdComponent.m_renderLayers;
            if (sprites == null || sprites.Count == 0) return;

            var render = _lcdComponent.m_render;
            if (render == null) return;

            string targetName = render.GenerateOffscreenTextureName(_lcd.EntityId, _lcdComponent.m_area);
            Vector2I textureSize = _lcdComponent.m_textureSize;
            Vector2 aspectRatio = _lcdComponent.m_screenAspectRatio;
            Vector2 aspectFactor = MyRenderComponentScreenAreas.CalcAspectFactor(textureSize, aspectRatio);
            Vector2 shift = MyRenderComponentScreenAreas.CalcShift(textureSize, aspectFactor);
            Vector2 halfTexture = (Vector2)textureSize * 0.5f;

            // Fresh frame id per call so SpritesManager.AddMessage doesn't coalesce with
            // a stale batch under the same target name.
            int frameId = System.Threading.Interlocked.Increment(ref _camovOverlayFrameId);

            bool hasScissor = false;
            int count = sprites.Count;
            for (int i = 0; i < count; i++)
            {
                MySprite sprite = sprites[i];
                Vector2 size = sprite.Size ?? (Vector2)textureSize;
                Vector2 position = sprite.Position ?? halfTexture;
                Color color = sprite.Color ?? Color.White;
                position += shift;

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
                        msg.ScreenScale = sprite.RotationOrScale;
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
                            if (hasScissor) PushScissorPop(targetName, frameId);
                            else hasScissor = true;
                            PushScissorPush(targetName, frameId, new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y));
                        }
                        else if (hasScissor)
                        {
                            PushScissorPop(targetName, frameId);
                            hasScissor = false;
                        }
                        break;
                }
            }
            if (hasScissor) PushScissorPop(targetName, frameId);

            // Synchronous sprite pass on the LCD RTV — MySpritesRenderer.Draw does NOT clear,
            // so sprites blend directly on top of the camera image already written by
            // CameraViewRenderer.Draw in this same frame.
            var messages = MyManagers.SpritesManager.AcquireDrawMessages(targetName);
            if (messages == null) return;

            var renderer = MyManagers.SpritesManager.GetSpritesRenderer();
            try
            {
                if (renderer.ProcessDrawSpritesQueue(messages, touchTextures: true))
                {
                    MyViewport viewport = new MyViewport(target.Size.X, target.Size.Y);
                    Vector2 viewportSize = (Vector2)target.Size * aspectFactor;
                    renderer.Draw(MyRender11.RC, target, ref viewport, ref viewport, ref viewportSize, null,
                        MyBlendStateManager.BlendAlphaPremultNoAlphaChannel);
                }
            }
            finally
            {
                MyManagers.SpritesManager.Return(renderer);
                MyManagers.SpritesManager.DisposeDrawMessages(messages);
            }
        }

        private static void PushScissorPush(string targetName, int frameId, Rectangle rect)
        {
            var msg = MyRenderProxy.MessagePool.Get<MyRenderMessageSpriteScissorPush>(MyRenderMessageEnum.SpriteScissorPush);
            msg.ScreenRectangle = rect;
            msg.TargetTexture = targetName;
            MyManagers.SpritesManager.AddMessage(msg, frameId);
            msg.Dispose();
        }

        private static void PushScissorPop(string targetName, int frameId)
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
            string name;
            try
            {
                name = _lcdComponent.GetRenderTextureName();
            }
            catch (NullReferenceException)
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

            UnregisterCamera();
            _lcd.CustomDataChanged -= _ => UpdateSettings();
            _lcd.IsWorkingChanged -= _ => UpdateIsActive();
            _lcd.CubeGridChanged -= _ => CubeGridChanged();
            _lcd.OnMarkForClose -= Lcd_OnMarkForClose;
        }
    }
}
