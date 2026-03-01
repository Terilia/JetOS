// TURRET DIAGNOSTIC - Paste this into a SEPARATE programmable block in-game.
// Set hinge & rotor to 0 degrees, then click "Run" on the PB.
// Check the PB's right-side detail panel for Echo output.
// This script is READ-ONLY — it does NOT move any motors.
//
// This file is excluded from the MDK build (see csproj).

const string ROTOR_LEFT = "Gun Rotor Left";
const string HINGE_LEFT = "Gun Hinge Left";
const string ROTOR_RIGHT = "Gun Rotor Right";
const string HINGE_RIGHT = "Gun Hinge Right";
const string COCKPIT_NAME = "Jet Pilot Seat";

public void Main()
{
    var cockpit = GridTerminalSystem.GetBlockWithName(COCKPIT_NAME) as IMyCockpit;
    if (cockpit == null) { Echo("ERROR: cockpit not found"); return; }

    Vector3D shipFwd = cockpit.WorldMatrix.Forward;
    Echo("=== TURRET DIAGNOSTIC ===");
    Echo("Ensure hinge+rotor at 0 deg!\n");
    Echo("Ship Forward: " + V(shipFwd));

    DiagnoseTurret("LEFT", ROTOR_LEFT, HINGE_LEFT, shipFwd);
    DiagnoseTurret("RIGHT", ROTOR_RIGHT, HINGE_RIGHT, shipFwd);
}

void DiagnoseTurret(string label, string rotorName, string hingeName, Vector3D shipFwd)
{
    Echo("\n--- " + label + " TURRET ---");

    var rotor = GridTerminalSystem.GetBlockWithName(rotorName) as IMyMotorStator;
    var hinge = GridTerminalSystem.GetBlockWithName(hingeName) as IMyMotorStator;
    if (rotor == null) { Echo("  Rotor: MISSING"); return; }
    if (hinge == null) { Echo("  Hinge: MISSING"); return; }

    IMySmallGatlingGun gun = null;
    if (hinge.TopGrid != null)
    {
        var topGrid = hinge.TopGrid;
        var guns = new List<IMySmallGatlingGun>();
        GridTerminalSystem.GetBlocksOfType(guns, g => g.CubeGrid == topGrid);
        if (guns.Count > 0) gun = guns[0];
    }
    if (gun == null) { Echo("  Gun: MISSING"); return; }

    Echo("  Rotor angle: " + MathHelper.ToDegrees(rotor.Angle).ToString("F1") + " deg");
    Echo("  Hinge angle: " + MathHelper.ToDegrees(hinge.Angle).ToString("F1") + " deg");

    // BARREL DIRECTION TEST
    MatrixD gm = gun.WorldMatrix;
    double dotFwd   = Vector3D.Dot(gm.Forward, shipFwd);
    double dotBack  = Vector3D.Dot(gm.Backward, shipFwd);
    double dotUp    = Vector3D.Dot(gm.Up, shipFwd);
    double dotDown  = Vector3D.Dot(gm.Down, shipFwd);
    double dotRight = Vector3D.Dot(gm.Right, shipFwd);
    double dotLeft  = Vector3D.Dot(gm.Left, shipFwd);

    Echo("  BARREL TEST (dot with ship fwd):");
    Echo("    Forward  = " + dotFwd.ToString("F3"));
    Echo("    Backward = " + dotBack.ToString("F3"));
    Echo("    Up       = " + dotUp.ToString("F3"));
    Echo("    Down     = " + dotDown.ToString("F3"));
    Echo("    Right    = " + dotRight.ToString("F3"));
    Echo("    Left     = " + dotLeft.ToString("F3"));

    string barrelAxis = "Forward";
    double bestDot = dotFwd;
    if (dotBack > bestDot)  { bestDot = dotBack;  barrelAxis = "Backward"; }
    if (dotUp > bestDot)    { bestDot = dotUp;    barrelAxis = "Up"; }
    if (dotDown > bestDot)  { bestDot = dotDown;  barrelAxis = "Down"; }
    if (dotRight > bestDot) { bestDot = dotRight; barrelAxis = "Right"; }
    if (dotLeft > bestDot)  { bestDot = dotLeft;  barrelAxis = "Left"; }
    Echo("  >>> BARREL = Gun." + barrelAxis + " (dot=" + bestDot.ToString("F3") + ")");

    if (bestDot < 0.9)
        Echo("  WARNING: No axis aligns with ship fwd!");
    if (barrelAxis != "Forward")
        Echo("  WARNING: Barrel != Gun.Forward!");

    // MOTOR AXES
    Echo("  Rotor.Up: " + V(rotor.WorldMatrix.Up));
    Echo("  Hinge.Up: " + V(hinge.WorldMatrix.Up));

    // ELEVATION SIGN
    Vector3D barrelDir = GetAxis(gm, barrelAxis);
    Vector3D rotorUp = rotor.WorldMatrix.Up;
    Vector3D crossResult = Vector3D.Cross(rotorUp, barrelDir);
    if (crossResult.LengthSquared() > 1e-6)
    {
        Vector3D baseLeft = Vector3D.Normalize(crossResult);
        double elevDot = Vector3D.Dot(baseLeft, hinge.WorldMatrix.Up);
        int elevSign = Math.Sign(elevDot);
        Echo("  ElevationSign: " + elevSign + " (dot=" + elevDot.ToString("F4") + ")");
    }
    else
    {
        Echo("  ElevationSign: DEGENERATE");
    }
}

Vector3D GetAxis(MatrixD m, string name)
{
    if (name == "Backward") return m.Backward;
    if (name == "Up")       return m.Up;
    if (name == "Down")     return m.Down;
    if (name == "Right")    return m.Right;
    if (name == "Left")     return m.Left;
    return m.Forward;
}

string V(Vector3D v)
{
    return "(" + v.X.ToString("F3") + ", " + v.Y.ToString("F3") + ", " + v.Z.ToString("F3") + ")";
}
