using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        // Hardware abstraction for the HQ station — the analogue of the jet's Jet class.
        // Gathers block references once, exposes station position/orientation, cached gravity,
        // and the command-seat analog input (mouse + WASD) that the Tactical Map pans with.
        public class Station
        {
            // ── Block naming convention (see docs) ──
            public const string SEAT_NAME = "HQ Command Seat";   // IMyShipController — toolbar + pan input
            public const string MFD_NAME  = "HQ MFD";            // text-surface provider — main MFD
            public const string MAP_NAME  = "HQ MAP";            // text-surface provider — dedicated always-on map

            private readonly Program _p;
            public IMyProgrammableBlock Pb;
            public IMyShipController Seat;        // command seat (optional, needed for map pan)
            public IMyTextSurface Mfd;            // main MFD surface (required for any UI)
            public IMyTextSurface Map;            // dedicated map surface (optional; map falls back to MFD)
            public IMyRadioAntenna Antenna;       // required for IGC reach to the fleet

            // Cached once per tick.
            public Vector3D Position;
            public Vector3D Gravity;

            public Station(Program program)
            {
                _p = program;
                Pb = program.Me;
                var gts = program.GridTerminalSystem;

                Seat = gts.GetBlockWithName(SEAT_NAME) as IMyShipController;

                var provider = gts.GetBlockWithName(MFD_NAME) as IMyTextSurfaceProvider;
                if (provider != null && provider.SurfaceCount > 0)
                {
                    Mfd = provider.GetSurface(0);
                    PrepSurface(Mfd);
                }

                var mapProvider = gts.GetBlockWithName(MAP_NAME) as IMyTextSurfaceProvider;
                if (mapProvider != null && mapProvider.SurfaceCount > 0)
                {
                    Map = mapProvider.GetSurface(0);
                    PrepSurface(Map);
                }

                var antennas = new List<IMyRadioAntenna>();
                gts.GetBlocksOfType(antennas);
                if (antennas.Count > 0) Antenna = antennas[0];

                Position = Pb.GetPosition();
            }

            // Refreshed at the top of every tick.
            public void UpdateTickCache()
            {
                Position = Pb.GetPosition();
                Gravity = Seat != null ? Seat.GetNaturalGravity() : VZ;
            }

            // ── Command-seat analog input (zero unless someone is seated) ──
            public bool SeatControlled => Seat != null && Seat.IsUnderControl;
            // MoveIndicator: X = A/D strafe, Y = C/Space down/up, Z = S/W back/fwd (W = -Z).
            public Vector3 Move => Seat != null ? Seat.MoveIndicator : Vector3.Zero;
            // RotationIndicator: X = mouse-Y (pitch), Y = mouse-X (yaw).
            public Vector2 Rot => Seat != null ? Seat.RotationIndicator : Vector2.Zero;

            // Up direction for the top-down map: gravity when on a planet, else seat/PB up.
            public Vector3D MapUp
            {
                get
                {
                    if (Gravity.LengthSquared() > 1e-4) return VN(-Gravity);
                    return Seat != null ? WU(Seat) : Pb.WorldMatrix.Up;
                }
            }

            public bool HasGravity => Gravity.LengthSquared() > 1e-4;
            public double AntennaRange => Antenna != null ? Antenna.Radius : 0.0;

            // Operator-facing block diagnostics, appended to the PB Echo panel.
            public void AppendDiagnostics(StringBuilder sb)
            {
                sb.Append("MFD ").Append(Mfd != null ? "OK" : "MISSING (\"" + MFD_NAME + "\")").Append('\n');
                sb.Append("MAP ").Append(Map != null ? "OK" : "MFD fallback").Append('\n');
                sb.Append("ANT ").Append(Antenna != null ? ((int)(AntennaRange / 1000.0) + "km") : "MISSING").Append('\n');
                sb.Append("SEAT ").Append(Seat == null ? "MISSING" : SeatControlled ? "MANNED" : "empty").Append('\n');
            }
        }
    }
}
