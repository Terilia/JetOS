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
                return new[]
                {
                    "Perform Raycast",
                    "Activate TV Screen",
                    $"Toggle GPS Lock {trackingStatus}",
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

                        // Find first empty slot or overwrite oldest
                        int slotIndex = Program.FindEmptyOrOldestSlot(myJet);

                        // Store target in slot (don't auto-activate, user cycles manually)
                        myJet.targetSlots[slotIndex] = new Jet.TargetSlot(target, targetVelocity, "Raycast");

                        // Add to enemy contact list (source index -1 indicates raycast)
                        myJet.UpdateOrAddEnemy(target, targetVelocity, "Raycast", -1);

                        string gpsCoordinates =
                            "Cached:GPS:Target:"
                            + target.X
                            + ":"
                            + target.Y
                            + ":"
                            + target.Z
                            + ":#FF75C9F1:";

                        // Capture target velocity for motion compensation
                        string cachedSpeed =
                            "CachedSpeed:"
                            + targetVelocity.X
                            + ":"
                            + targetVelocity.Y
                            + ":"
                            + targetVelocity.Z
                            + ":#FF75C9F1:";

                        UpdateCustomDataWithCache(gpsCoordinates, cachedSpeed);
                        DisplayRaycastResult(gpsCoordinates);
                    }
                    else
                    {
                        DisplayRaycastResult("No target detected.");
                    }
                }
                else
                {
                    DisplayRaycastResult("Camera is not ready or cannot perform raycast.");
                }
            }
            private void DisplayRaycastResult(string result)
            {
                if (lcdTGP != null)
                {
                    StringBuilder output = new StringBuilder();
                    output.AppendLine("╔════════════════════╗");
                    output.AppendLine("║    RAYCAST RESULT   ║");
                    output.AppendLine("╠════════════════════╣");
                    output.AppendLine(result);
                    output.AppendLine("╚════════════════════╝");
                    lcdTGP.WriteText(output.ToString());
                }
            }

            private void UpdateCustomDataWithCache(string gpsCoordinates, string cachedSpeed)
            {
                string[] customDataLines = ParentProgram.Me.CustomData.Split('\n');
                bool cachedLineFound = false;
                bool cachedSpeedFound = false;

                for (int i = 0; i < customDataLines.Length; i++)
                {
                    if (customDataLines[i].StartsWith("Cached:"))
                    {
                        customDataLines[i] = gpsCoordinates;
                        cachedLineFound = true;
                    }
                    else if (customDataLines[i].StartsWith("CachedSpeed:"))
                    {
                        customDataLines[i] = cachedSpeed;
                        cachedSpeedFound = true;
                    }
                }

                if (!cachedLineFound)
                {
                    List<string> customDataList = new List<string>(customDataLines);
                    customDataList.Add(gpsCoordinates);
                    customDataLines = customDataList.ToArray();
                }

                if (!cachedSpeedFound)
                {
                    List<string> customDataList = new List<string>(customDataLines);
                    customDataList.Add(cachedSpeed);
                    customDataLines = customDataList.ToArray();
                }

                ParentProgram.Me.CustomData = string.Join("\n", customDataLines);
                SystemManager.MarkCustomDataDirty();
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
            private void ActivateTVScreen()
            {
                if (cockpit == null)
                {
                    return;
                }
                IMyTextSurface screen = cockpit.GetSurface(1);
                if (screen == null)
                {
                    return;
                }
                screen.ContentType = ContentType.SCRIPT;
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
                    // Tracking uses active slot - no separate local variable needed
                    trackingActive = myJet.targetSlots[myJet.activeSlotIndex].IsOccupied;
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

                // Get active target position from Jet slot
                if (!myJet.targetSlots[myJet.activeSlotIndex].IsOccupied)
                {
                    return;  // No target to track
                }

                Vector3D targetPosition = myJet.targetSlots[myJet.activeSlotIndex].Position;

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
                return "5: Perform Raycast\n8: Activate TV Screen\n9: Toggle GPS Lock\n";
            }
        }
    }
}
