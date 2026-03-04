using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        class RaycastCameraControl : ProgramModule
        {
            private IMyCameraBlock camera;
            private IMyTextSurfaceProvider cockpit;
            private IMyRemoteControl remoteControl;
            private IMyMotorStator rotor;
            private IMyMotorAdvancedStator hinge;
            private IMyTextSurface lcdTGP;
            private bool trackingActive = false;
            private int animationTicks = 0;
            private bool animating = false;
            private const int maxAnimationTicks = 100;
            private Jet myJet;
            public RaycastCameraControl(Program program, Jet jet) : base(program)
            {
                name = "TargetingPod Control";
                myJet = jet;
                camera =
                    program.GridTerminalSystem.GetBlockWithName("Camera Targeting Turret")
                    as IMyCameraBlock;
                if (camera != null)
                {
                    camera.EnableRaycast = true;
                }
                lcdTGP =
                    program.GridTerminalSystem.GetBlockWithName("LCD Targeting Pod")
                    as IMyTextSurface;
                if (lcdTGP != null)
                {
                    lcdTGP.ContentType = ContentType.TEXT_AND_IMAGE;
                }
                remoteControl =
                    program.GridTerminalSystem.GetBlockWithName("Remote Control")
                    as IMyRemoteControl;
                rotor =
                    program.GridTerminalSystem.GetBlockWithName("Targeting Rotor")
                    as IMyMotorStator;
                hinge =
                    program.GridTerminalSystem.GetBlockWithName("Targeting Hinge")
                    as IMyMotorAdvancedStator;
                cockpit = jet._cockpit;
            }
            public override string[] GetOptions()
            {
                string trackingStatus = trackingActive ? "[ON]" : "[OFF]";
                string pinnedStatus = myJet.pinnedRaycastTarget.HasValue ? " [PINNED]" : "";
                return new[]
                {
                    "Perform Raycast",
                    "Activate TV",
                    $"Toggle GPS Lock {trackingStatus}",
                    $"Clear Target{pinnedStatus}",
                    "Back to Main Menu"
                };
            }
            public override void ExecuteOption(int index)
            {
                switch (index)
                {
                    case 0:
                        ExecuteRaycast();
                        break;
                    case 1:
                        ActivateTVScreen();
                        break;
                    case 2:
                        ToggleGPSLock();
                        break;
                    case 3:
                        ClearTarget();
                        break;
                    case 4:
                        SystemManager.ReturnToMainMenu();
                        break;
                }
            }
            public override void Tick()
            {
                if (trackingActive)
                {
                    TrackTarget();
                }
                if (animating)
                {
                    AnimateCrosshair();
                }
            }
            private void ExecuteRaycast()
            {
                if (camera != null && camera.CanScan(35000))
                {
                    MyDetectedEntityInfo hitInfo = camera.Raycast(35000);
                    if (!hitInfo.IsEmpty())
                    {
                        Vector3D target = hitInfo.HitPosition ?? Vector3D.Zero;
                        Vector3D targetVelocity = hitInfo.Velocity;

                        // Add to enemy contact list (source index -1 indicates raycast)
                        myJet.UpdateOrAddEnemy(target, targetVelocity, "Raycast", -1);

                        // Store as pinned raycast target (static, never decays)
                        myJet.pinnedRaycastTarget = new Jet.EnemyContact(target, targetVelocity, "Raycast", -1);

                        // Auto-select the pinned target and update GPS cache
                        myJet.SelectPinned();
                        SystemManager.UpdateActiveTargetGPS();
                    }
                }
            }

            private Vector2 center = new Vector2(25, 17);
            private bool isLocked = true;
            private bool animationstarted = false;
            private int maxTicks = 50;
            private void AnimateCrosshair()
            {
                if (!animationstarted)
                {
                    return;
                }
                lcdTGP.ContentType = ContentType.TEXT_AND_IMAGE;
                lcdTGP.Font = "Monospace";
                lcdTGP.FontSize = 0.5f;
                lcdTGP.TextPadding = 0f;
                lcdTGP.Alignment = TextAlignment.LEFT;
                lcdTGP.BackgroundColor = new Color(0, 0, 0);
                StringBuilder output = new StringBuilder();
                for (int i = 0; i < 35; i++)
                {
                    output.AppendLine(new string(' ', 53));
                }
                float progress = (float)animationTicks / maxTicks;
                int horizontalLength = (int)(2 + (isLocked ? 0 : 2 * progress));
                int verticalLength = (int)(1 + (isLocked ? 0 : 1 * progress));
                int leftX = (int)center.X - horizontalLength;
                int rightX = (int)center.X + horizontalLength;
                int topY = (int)center.Y - verticalLength;
                int bottomY = (int)center.Y + verticalLength;
                Color boxColor = isLocked ? new Color(0, 255, 0) : new Color(255, 0, 0);
                lcdTGP.FontColor = boxColor;
                for (int x = leftX; x <= rightX; x++)
                {
                    SetSymbolAtPosition(output, x, topY, '─');
                    SetSymbolAtPosition(output, x, bottomY, '─');
                }
                for (int y = topY; y <= bottomY; y++)
                {
                    SetSymbolAtPosition(output, leftX, y, '│');
                    SetSymbolAtPosition(output, rightX, y, '│');
                }
                SetSymbolAtPosition(output, leftX, topY, isLocked ? '┌' : '╭');
                SetSymbolAtPosition(output, rightX, topY, isLocked ? '┐' : '╮');
                SetSymbolAtPosition(output, leftX, bottomY, isLocked ? '└' : '╰');
                SetSymbolAtPosition(output, rightX, bottomY, isLocked ? '┘' : '╯');
                if (!isLocked)
                {
                    SetSymbolAtPosition(output, leftX - 1, topY, '─');
                    SetSymbolAtPosition(output, rightX + 1, topY, '─');
                    SetSymbolAtPosition(output, leftX - 1, bottomY, '─');
                    SetSymbolAtPosition(output, rightX + 1, bottomY, '─');
                    SetSymbolAtPosition(output, leftX, topY - 1, '│');
                    SetSymbolAtPosition(output, rightX, topY - 1, '│');
                    SetSymbolAtPosition(output, leftX, bottomY + 1, '│');
                    SetSymbolAtPosition(output, rightX, bottomY + 1, '│');
                }
                lcdTGP.WriteText(output.ToString());
                animationTicks++;
                if (animationTicks > maxTicks)
                {
                    animationTicks = 0;
                    isLocked = !isLocked;
                    animationstarted = false;
                }
            }
            private void SetSymbolAtPosition(StringBuilder output, int x, int y, char symbol)
            {
                int lineLength = 54 + 1;
                if (x >= 0 && x < 54 && y >= 0 && y < 35)
                {
                    int index = y * lineLength + x;
                    if (index < output.Length)
                    {
                        output[index] = symbol;
                    }
                }
            }
            private void ClearTarget()
            {
                myJet.pinnedRaycastTarget = null;
                myJet.ClearSelection();
                trackingActive = false;

                SystemManager.RemoveCustomDataValue("Cached");
                SystemManager.RemoveCustomDataValue("CachedSpeed");
            }

            private void ActivateTVScreen()
            {

            }
            private void ToggleGPSLock()
            {
                animationstarted = true;
                if (trackingActive)
                {
                    trackingActive = false;
                    animationTicks = 0;
                    animating = true;
                }
                else
                {
                    trackingActive = myJet.HasSelectedEnemy();
                    if (trackingActive)
                    {
                        animationTicks = 0;
                        animating = true;
                    }
                }
            }
            private void TrackTarget()
            {
                if (remoteControl == null || rotor == null || hinge == null)
                {
                    return;
                }

                var selected = myJet.GetSelectedEnemy();
                if (!selected.HasValue)
                {
                    return;  // No target to track
                }

                Vector3D targetPosition = selected.Value.Position;

                Vector3D cameraPosition = camera.GetPosition();
                Vector3D dirVector = targetPosition - cameraPosition;
                Vector3D directionToTarget = dirVector.LengthSquared() > 0 ? Vector3D.Normalize(dirVector) : Vector3D.Zero;
                Vector3D cameraForward = -camera.WorldMatrix.Forward;
                double dotProductForward = Vector3D.Dot(cameraForward, directionToTarget);
                double angleToTargetForward = Math.Acos(
                    MathHelper.Clamp(dotProductForward, -1.0, 1.0)
                );
                angleToTargetForward = MathHelper.ToDegrees(angleToTargetForward);
                Vector3D remotePosition = remoteControl.GetPosition();
                MatrixD remoteOrientation = remoteControl.WorldMatrix;
                Vector3D relativeTargetPosition = Vector3D.TransformNormal(
                    targetPosition - remotePosition,
                    MatrixD.Transpose(remoteOrientation)
                );
                float kP_rotor = 0.05f;
                float kP_hinge = 0.05f;
                double dampingFactor = Math.Max(0.05, Math.Min(1.0, angleToTargetForward / 90.0));
                double rotorVelocity = -(kP_rotor * relativeTargetPosition.X) * dampingFactor;
                double hingeVelocity = -(kP_hinge * relativeTargetPosition.Y) * dampingFactor;
                rotorVelocity = MathHelper.Clamp(rotorVelocity, -5.0, 5.0);
                hingeVelocity = MathHelper.Clamp(hingeVelocity, -5.0, 5.0);
                rotor.TargetVelocityRPM = (float)rotorVelocity;
                hinge.TargetVelocityRPM = (float)hingeVelocity;
                if (angleToTargetForward < 2.0)
                {
                    rotor.TargetVelocityRPM = 0f;
                    hinge.TargetVelocityRPM = 0f;
                }
            }
            public override void HandleSpecialFunction(int key)
            {
                if (key == 5)
                {
                    ExecuteRaycast();
                }
                if (key == 6)
                {
                    ActivateTVScreen();
                }
            }
            public override string GetHotkeys()
            {
                return "5: \n";
            }
        }
    }
}
