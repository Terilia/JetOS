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
        class ConfigurationModule : ProgramModule
        {
            private enum MenuLevel { Category, ParameterList, ValueAdjust }
            private MenuLevel currentLevel = MenuLevel.Category;
            private int categoryIndex = 0;
            private int parameterIndex = 0;

            const string C_W = "Warn";
            const string C_GC = "Gun";
            const string C_HT = "HUD";
            const string C_TH = "Theme";
            const string C_RA = "Reset";
            const string S_SEL = "3 SAVE";
            const string S_BCK = "4 BACK";

            private string[] categories = new string[]
            {
                C_W,
                C_GC,
                C_HT,
                C_TH,
                C_RA
            };

            // Configuration storage
            private Dictionary<string, ConfigParam> allConfigs = new Dictionary<string, ConfigParam>();

            public ConfigurationModule(Program program) : base(program)
            {
                name = "Config";
                InitializeConfigs();
                LoadFromCustomData();
            }

            private class ConfigParam
            {
                public string Category;
                public string Name;
                public string DisplayName;
                public float Value;
                public float DefaultValue;
                public float MinValue;
                public float MaxValue;
                public float StepSize;
                public string Unit;
                public bool IsModified => Ab(Value - DefaultValue) > 0.0001f;
                public bool IsToggle => MaxValue == 1f && MinValue == 0f && StepSize == 1f;

                public ConfigParam(string category, string name, string displayName, float defaultValue,
                                 float minValue, float maxValue, float stepSize, string unit = "")
                {
                    Category = category;
                    Name = name;
                    DisplayName = displayName;
                    Value = defaultValue;
                    DefaultValue = defaultValue;
                    MinValue = minValue;
                    MaxValue = maxValue;
                    StepSize = stepSize;
                    Unit = unit;
                }

                public void Adjust(int direction)
                {
                    Value = Mx(MinValue, Mn(MaxValue, Value + direction * StepSize));
                }

                public void Reset()
                {
                    Value = DefaultValue;
                }
            }

            private void InitializeConfigs()
            {
                // WARNINGS
                AddConfig(C_W, CFG_ALTITUDE_WARNING, "Alt Warn", 150f, 100f, 1000f, 10f, "m");
                AddConfig(C_W, CFG_SPEED_WARNING, "Spd Warn", 360f, 100f, 600f, 10f, "kts");
                AddConfig(C_W, CFG_BINGO_FUEL, "Bingo", 0.20f, 0.05f, 0.50f, 0.05f, "%");
                AddConfig(C_W, CFG_LOW_FUEL, "Low Fuel", 0.35f, 0.10f, 0.60f, 0.05f, "%");

                // GUN CONTROL
                AddConfig(C_GC, CFG_GUN_KP, "KP Gain", 5.0f, 0.5f, 20.0f, 0.5f);
                AddConfig(C_GC, CFG_GUN_MAX_RPM, "Max RPM", 30f, 5f, 60f, 5f, "RPM");
                AddConfig(C_GC, CFG_GUN_LOCK_THRESHOLD, "Lock", 2.0f, 0.5f, 10.0f, 0.5f, "deg");
                AddConfig(C_GC, CFG_GUN_MAX_RANGE, "Range", 6000f, 1000f, 15000f, 500f, "m");
                AddConfig(C_GC, CFG_GUN_MUZZLE_VELOCITY, "Muzzle", 1100f, 200f, 2000f, 50f, "m/s");

                // HUD TOGGLES (1=on, 0=off)
                AddHudToggle(CFG_HUD_RADAR, "Radar");
                AddHudToggle(CFG_HUD_GUN_FUNNEL, "Funnel");
                AddHudToggle(CFG_HUD_TARGET_BRACKETS, "Tgt Brkt");
                AddHudToggle(CFG_HUD_GFORCE, "G");
                AddHudToggle(CFG_HUD_AOA, "AOA Indexer");
                AddHudToggle(CFG_HUD_FPM, "FPM");
                AddHudToggle(CFG_HUD_COMPASS, "Compass");
                AddHudToggle(CFG_HUD_BREAKAWAY, "Break");

                // HUD THEME (0=Green, 1=Blue, 2=Amber, 3=White)
                AddConfig(C_TH, CFG_HUD_THEME, "Color", 0f, 0f, 3f, 1f);

            }

            private void AddConfig(string category, string name, string displayName, float defaultValue,
                                  float minValue, float maxValue, float stepSize, string unit = "")
            {
                allConfigs[name] = new ConfigParam(category, name, displayName, defaultValue,
                                                  minValue, maxValue, stepSize, unit);
            }

            private void AddToggle(string category, string name, string displayName)
            {
                AddConfig(category, name, displayName, 1f, 0f, 1f, 1f);
            }

            private void AddHudToggle(string name, string displayName)
            {
                AddToggle(C_HT, name, displayName);
            }

            private string FormatValue(ConfigParam p)
            {
                if (p.Name == CFG_HUD_THEME)
                {
                    int idx = (int)p.Value;
                    return idx >= 0 && idx < themeNames.Length ? themeNames[idx] : "?";
                }
                if (p.IsToggle)
                    return p.Value > 0.5f ? "ON" : "OFF";
                return p.Value.ToString("F2").TrimEnd('0').TrimEnd('.') + p.Unit;
            }

            private void LoadFromCustomData()
            {
                string customData = ParentProgram.Me.CustomData;
                if (SE(customData)) return;

                string[] lines = customData.Split('\n');
                foreach (string line in lines)
                {
                    if (line.StartsWith(CD_CONFIG))
                    {
                        string[] parts = line.Substring(CD_CONFIG.Length).Split(':');
                        if (parts.Length == 2)
                        {
                            string configName = parts[0];
                            float value;
                            if (allConfigs.ContainsKey(configName) && float.TryParse(parts[1], out value))
                            {
                                allConfigs[configName].Value = value;
                            }
                        }
                    }
                }
            }

            private void SaveToCustomData()
            {
                StringBuilder sb = new StringBuilder();

                // Preserve non-config lines
                string currentData = ParentProgram.Me.CustomData;
                if (!SE(currentData))
                {
                    string[] lines = currentData.Split('\n');
                    foreach (string line in lines)
                    {
                        if (!line.StartsWith(CD_CONFIG))
                        {
                            sb.AppendLine(line);
                        }
                    }
                }

                // Add all config values
                foreach (var kvp in allConfigs)
                {
                    sb.AppendLine($"{CD_CONFIG}{kvp.Key}:{kvp.Value.Value}");
                }

                ParentProgram.Me.CustomData = sb.ToString();
                SystemManager.MarkCustomDataDirty();
            }

            public float GetValue(string configName)
            {
                if (allConfigs.ContainsKey(configName))
                    return allConfigs[configName].Value;
                return 0f;
            }

            private static readonly string[] themeNames = { "G", "B", "A", "W" };

            public override string[] GetOptions()
            {
                switch (currentLevel)
                {
                    case MenuLevel.Category:
                        return categories;

                    case MenuLevel.ParameterList:
                        string selectedCategory = categories[categoryIndex];
                        if (selectedCategory == C_RA)
                        {
                            return new string[] { "Reset All", "Back" };
                        }
                        List<string> options = new List<string>();
                        foreach (var kvp in allConfigs)
                        {
                            var p = kvp.Value;
                            if (p.Category == selectedCategory)
                            {
                                string modified = p.IsModified ? " *" : "";
                                options.Add($"{p.DisplayName}: {FormatValue(p)}{modified}");
                            }
                        }
                        options.Add("Reset Cat");
                        options.Add("Back");
                        return options.ToArray();

                    case MenuLevel.ValueAdjust:
                        var currentParams = GetCurrentCategoryParams();
                        if (parameterIndex < currentParams.Count)
                        {
                            var param = currentParams[parameterIndex];
                            string adjustLabel = $"ADJ {param.DisplayName}";
                            if (param.Name == CFG_HUD_THEME)
                            {
                                return new string[]
                                {
                                    adjustLabel,
                                    "1/2 THEME",
                                    "NOW " + FormatValue(param),
                                    "0G 1B 2A 3W",
                                    S_SEL,
                                    S_BCK
                                };
                            }
                            if (param.IsToggle)
                            {
                                return new string[]
                                {
                                    adjustLabel,
                                    "1/2 TOG",
                                    "NOW " + FormatValue(param),
                                    S_SEL,
                                    S_BCK
                                };
                            }
                            return new string[]
                            {
                                adjustLabel,
                                "1 +",
                                $"NOW {param.Value:F2}{param.Unit}",
                                "2 -",
                                $"DEF {param.DefaultValue:F2}{param.Unit}",
                                $"{param.MinValue:F2}-{param.MaxValue:F2}{param.Unit}",
                                S_SEL,
                                S_BCK
                            };
                        }
                        break;
                }
                return new string[] { "Error" };
            }

            private List<ConfigParam> GetCurrentCategoryParams()
            {
                string selectedCategory = categories[categoryIndex];
                List<ConfigParam> params_list = new List<ConfigParam>();
                foreach (var kvp in allConfigs)
                {
                    if (kvp.Value.Category == selectedCategory)
                    {
                        params_list.Add(kvp.Value);
                    }
                }
                return params_list;
            }

            public override void ExecuteOption(int index)
            {
                switch (currentLevel)
                {
                    case MenuLevel.Category:
                        if (categories[index] == C_RA)
                        {
                            foreach (var kvp in allConfigs)
                                kvp.Value.Reset();
                            SaveToCustomData();
                        }
                        else
                        {
                            categoryIndex = index;
                            currentLevel = MenuLevel.ParameterList;
                            parameterIndex = 0;
                            SystemManager.currentMenuIndex = 0;
                        }
                        break;

                    case MenuLevel.ParameterList:
                        var params_list = GetCurrentCategoryParams();
                        if (index < params_list.Count)
                        {
                            parameterIndex = index;
                            currentLevel = MenuLevel.ValueAdjust;
                            SystemManager.currentMenuIndex = 0;
                        }
                        else if (index == params_list.Count)
                        {
                            // Reset category
                            foreach (var param in params_list)
                                param.Reset();
                            SaveToCustomData();
                        }
                        else
                        {
                            // Back
                            currentLevel = MenuLevel.Category;
                            SystemManager.currentMenuIndex = categoryIndex;
                        }
                        break;

                    case MenuLevel.ValueAdjust:
                        SaveToCustomData();
                        currentLevel = MenuLevel.ParameterList;
                        SystemManager.currentMenuIndex = parameterIndex;
                        break;
                }
            }

            public override bool HandleNavigation(bool isUp)
            {
                if (currentLevel == MenuLevel.ValueAdjust)
                {
                    var params_list = GetCurrentCategoryParams();
                    if (parameterIndex < params_list.Count)
                    {
                        var param = params_list[parameterIndex];
                        if (param.Name == CFG_HUD_THEME)
                        {
                            // Cycle forward only, wrap around
                            param.Value = (int)(param.Value + 1) % 4;
                        }
                        else if (isUp)
                        {
                            param.Adjust(1);
                        }
                        else
                        {
                            param.Adjust(-1);
                        }
                        return true;
                    }
                }
                return false;
            }

            public override bool HandleBack()
            {
                if (currentLevel == MenuLevel.ValueAdjust)
                {
                    currentLevel = MenuLevel.ParameterList;
                    SystemManager.currentMenuIndex = parameterIndex;
                    return true;
                }
                else if (currentLevel == MenuLevel.ParameterList)
                {
                    currentLevel = MenuLevel.Category;
                    SystemManager.currentMenuIndex = categoryIndex;
                    return true;
                }
                return false;
            }
        }
    }
}
