using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using VRage.Utils;

namespace LcdBoosterClient
{
    /// <summary>
    /// Client-side canard animation fix.
    /// Reads angle from CustomData tag [AniAngle:XX.XX] (synced by server-side Torch plugin)
    /// and writes it into the mod's FlapBase.m_angle on the client, letting the mod animate.
    /// Blocks must have [Ani] in their CustomName.
    /// </summary>
    internal static class CanardAnimFix
    {
        const string NAME_TAG = "[Ani]";
        const string ANGLE_PREFIX = "[AniAngle:";
        const int SCAN_INTERVAL = 120;

        // Entity resolution
        static MethodInfo _getEntities;
        static bool _resolved;
        static int _resolveDelay;
        static int _scanCooldown;

        // FlapBase resolution
        static Type _flapBaseType;
        static FieldInfo _angleField;
        static PropertyInfo _gameLogicProp;
        static bool _flapResolved;
        static int _flapResolveDelay;

        // Block access
        static PropertyInfo _markedForClose;
        static PropertyInfo _customNameProp;
        static PropertyInfo _customDataProp;

        struct TrackedBlock
        {
            public object Block;
            public object GameLogic;
        }

        static readonly List<TrackedBlock> _blocks = new List<TrackedBlock>();

        static bool TryResolve()
        {
            if (_resolved) return true;
            if (_resolveDelay > 0) { _resolveDelay--; return false; }

            try
            {
                var myEntitiesType = Type.GetType("Sandbox.Game.Entities.MyEntities, Sandbox.Game");
                if (myEntitiesType != null)
                    _getEntities = myEntitiesType.GetMethod("GetEntities", Type.EmptyTypes);

                if (_getEntities == null)
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

        static bool TryResolveFlapBase()
        {
            if (_flapResolved) return _flapBaseType != null;
            if (_flapResolveDelay > 0) { _flapResolveDelay--; return false; }

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
                    _flapResolveDelay = 300;
                    return false;
                }

                _angleField = _flapBaseType.GetField("m_angle", BindingFlags.NonPublic | BindingFlags.Instance);
                if (_angleField == null)
                {
                    _flapResolveDelay = 300;
                    _flapBaseType = null;
                    return false;
                }

                _flapResolved = true;
            }
            catch
            {
                _flapResolveDelay = 300;
            }

            return _flapBaseType != null;
        }

        public static void Update()
        {
            if (!TryResolve()) return;
            TryResolveFlapBase();

            if (--_scanCooldown <= 0)
            {
                _scanCooldown = SCAN_INTERVAL;
                Rescan();
            }

            for (int i = _blocks.Count - 1; i >= 0; i--)
            {
                try
                {
                    var b = _blocks[i];
                    if (b.Block == null) { _blocks.RemoveAt(i); continue; }
                    if (_markedForClose != null && (bool)_markedForClose.GetValue(b.Block)) { _blocks.RemoveAt(i); continue; }

                    float? angle = ReadAngle(b.Block);
                    if (!angle.HasValue) continue;

                    // Write m_angle on the client's FlapBase — let the mod's own code animate
                    if (b.GameLogic != null && _angleField != null)
                        _angleField.SetValue(b.GameLogic, angle.Value);
                }
                catch { }
            }
        }

        static void Rescan()
        {
            _blocks.Clear();

            try
            {
                var entities = _getEntities.Invoke(null, null) as IEnumerable;
                if (entities == null) return;

                foreach (var entity in entities)
                {
                    var gridType = entity.GetType();
                    if (!gridType.Name.Contains("CubeGrid")) continue;

                    var fatMethod = gridType.GetMethods()
                        .FirstOrDefault(m => m.Name == "GetFatBlocks" && m.GetParameters().Length == 0 && !m.IsGenericMethod);
                    if (fatMethod == null) continue;

                    var blocks = fatMethod.Invoke(entity, null) as IEnumerable;
                    if (blocks == null) continue;

                    foreach (var block in blocks)
                    {
                        try
                        {
                            string name = GetCustomName(block);
                            if (name == null || !name.Contains(NAME_TAG))
                                continue;

                            if (_markedForClose == null)
                                _markedForClose = block.GetType().GetProperty("MarkedForClose");

                            object logic = _flapBaseType != null ? GetFlapBaseLogic(block) : null;

                            _blocks.Add(new TrackedBlock
                            {
                                Block = block,
                                GameLogic = logic
                            });
                        }
                        catch { }
                    }
                }

            }
            catch { }
        }

        static float? ReadAngle(object block)
        {
            try
            {
                if (_customDataProp == null)
                    _customDataProp = block.GetType().GetProperty("CustomData");
                if (_customDataProp == null) return null;

                string data = _customDataProp.GetValue(block)?.ToString();
                if (data == null) return null;

                int start = data.IndexOf(ANGLE_PREFIX);
                if (start < 0) return null;

                start += ANGLE_PREFIX.Length;
                int end = data.IndexOf(']', start);
                if (end < 0) return null;

                string val = data.Substring(start, end - start);
                if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float angle))
                    return angle;
            }
            catch { }
            return null;
        }

        static string GetCustomName(object block)
        {
            try
            {
                if (_customNameProp == null)
                    _customNameProp = block.GetType().GetProperty("CustomName");
                return _customNameProp?.GetValue(block)?.ToString();
            }
            catch { return null; }
        }

        static object GetFlapBaseLogic(object block)
        {
            try
            {
                if (_gameLogicProp == null)
                    _gameLogicProp = block.GetType().GetProperty("GameLogic");
                if (_gameLogicProp == null) return null;

                var gl = _gameLogicProp.GetValue(block);
                if (gl == null) return null;

                if (_flapBaseType.IsInstanceOfType(gl))
                    return gl;

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
