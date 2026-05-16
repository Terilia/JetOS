using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;

namespace LcdBooster
{
    /// <summary>
    /// Server-side: reads m_angle from FlapBase on [Ani] tagged blocks
    /// and writes it to CustomData as [AniAngle:XX.XX] so clients can read the synced value.
    /// </summary>
    internal static class CanardAngleSync
    {
        private const string TAG = "[Ani]";
        private const string ANGLE_PREFIX = "[AniAngle:";
        private const int SCAN_INTERVAL = 60; // ticks between full rescans
        private const int SYNC_INTERVAL = 6;  // ticks between angle writes

        private static Type _flapBaseType;
        private static FieldInfo _angleField;
        private static bool _resolved;
        private static int _resolveDelay = 300;
        private static int _scanCooldown;
        private static int _syncCooldown;

        private struct TrackedCanard
        {
            public MyTerminalBlock Block;
            public object GameLogic;
            public float LastAngle;
        }

        private static readonly List<TrackedCanard> _canards = new List<TrackedCanard>();

        public static bool Resolved => _resolved;
        public static int TrackedCount => _canards.Count;

        public static void Update()
        {
            if (!TryResolve()) return;

            if (--_scanCooldown <= 0)
            {
                _scanCooldown = SCAN_INTERVAL;
                Rescan();
            }

            if (--_syncCooldown <= 0)
            {
                _syncCooldown = SYNC_INTERVAL;
                SyncAngles();
            }
        }

        private static bool TryResolve()
        {
            if (_resolved) return true;
            if (_resolveDelay > 0) { _resolveDelay--; return false; }

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (_flapBaseType != null) break;
                    try
                    {
                        foreach (var t in asm.GetTypes())
                        {
                            if (t.Name == "FlapBase")
                            {
                                _flapBaseType = t;
                                break;
                            }
                        }
                    }
                    catch { }
                }

                if (_flapBaseType == null)
                {
                    _resolveDelay = 300;
                    return false;
                }

                _angleField = _flapBaseType.GetField("m_angle", BindingFlags.NonPublic | BindingFlags.Instance);
                if (_angleField == null)
                {
                    _resolveDelay = 300;
                    return false;
                }

                _resolved = true;
            }
            catch
            {
                _resolveDelay = 300;
            }

            return _resolved;
        }

        private static void Rescan()
        {
            _canards.Clear();

            try
            {
                var entities = MyEntities.GetEntities();
                foreach (var entity in entities)
                {
                    var grid = entity as MyCubeGrid;
                    if (grid == null) continue;

                    foreach (var block in grid.GetFatBlocks<MyTerminalBlock>())
                    {
                        string name = block.CustomName?.ToString();
                        if (name == null || !name.Contains(TAG))
                            continue;

                        object logic = GetFlapBaseLogic(block);
                        if (logic == null) continue;

                        _canards.Add(new TrackedCanard
                        {
                            Block = block,
                            GameLogic = logic,
                            LastAngle = float.NaN
                        });
                    }
                }

            }
            catch { }
        }

        private static void SyncAngles()
        {
            for (int i = _canards.Count - 1; i >= 0; i--)
            {
                var c = _canards[i];
                if (c.Block == null || c.Block.MarkedForClose || c.GameLogic == null)
                {
                    _canards.RemoveAt(i);
                    continue;
                }

                try
                {
                    float angle = (float)_angleField.GetValue(c.GameLogic);

                    // Only update CustomData when angle actually changed
                    if (Math.Abs(angle - c.LastAngle) < 0.01f)
                        continue;

                    c.LastAngle = angle;
                    _canards[i] = c;

                    string data = c.Block.CustomData ?? "";
                    int start = data.IndexOf(ANGLE_PREFIX);
                    string tag = ANGLE_PREFIX + angle.ToString("F2") + "]";

                    if (start >= 0)
                    {
                        int end = data.IndexOf(']', start);
                        if (end >= 0)
                            data = data.Substring(0, start) + tag + data.Substring(end + 1);
                        else
                            data = data.Substring(0, start) + tag;
                    }
                    else
                    {
                        if (data.Length > 0 && !data.EndsWith("\n"))
                            data += "\n";
                        data += tag;
                    }

                    c.Block.CustomData = data;
                }
                catch { }
            }
        }

        private static object GetFlapBaseLogic(MyTerminalBlock block)
        {
            try
            {
                var gl = block.GameLogic;
                if (gl == null) return null;

                if (_flapBaseType.IsInstanceOfType(gl))
                    return gl;

                // Composite logic: check m_logicComponents
                var compField = gl.GetType().GetField("m_logicComponents",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (compField != null)
                {
                    var comps = compField.GetValue(gl) as IEnumerable;
                    if (comps != null)
                    {
                        foreach (var comp in comps)
                        {
                            if (_flapBaseType.IsInstanceOfType(comp))
                                return comp;
                        }
                    }
                }

                return null;
            }
            catch { return null; }
        }
    }
}
