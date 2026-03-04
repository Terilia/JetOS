using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;

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

            private string[] categories = new string[]
            {
                "Warnings",
                "Gun Control",
                "HUD Toggles",
                "HUD Theme",
                "Reset All"
            };

            // Configuration storage
            private Dictionary<string, ConfigParam> allConfigs = new Dictionary<string, ConfigParam>();

            public ConfigurationModule(Program program) : base(program)
            {
                name = "Configuration";
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
                public bool IsModified => Math.Abs(Value - DefaultValue) > 0.0001f;

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
                    Value = Math.Max(MinValue, Math.Min(MaxValue, Value + direction * StepSize));
                }

                public void Reset()
                {
                    Value = DefaultValue;
                }
            }

            private void InitializeConfigs()
            {
                // WARNINGS
                AddConfig("Warnings", "altitude_warning", "Altitude Warning", 380f, 100f, 1000f, 10f, "m");
                AddConfig("Warnings", "speed_warning", "Speed Warning", 360f, 100f, 600f, 10f, "kts");
                AddConfig("Warnings", "bingo_fuel", "Bingo Fuel", 0.20f, 0.05f, 0.50f, 0.05f, "%");
                AddConfig("Warnings", "low_fuel", "Low Fuel", 0.35f, 0.10f, 0.60f, 0.05f, "%");

                // GUN CONTROL
                AddConfig("Gun Control", "gun_kp", "KP Gain", 5.0f, 0.5f, 20.0f, 0.5f);
                AddConfig("Gun Control", "gun_max_rpm", "Max RPM", 30f, 5f, 60f, 5f, "RPM");
                AddConfig("Gun Control", "gun_lock_threshold", "Lock Threshold", 2.0f, 0.5f, 10.0f, 0.5f, "deg");
                AddConfig("Gun Control", "gun_max_range", "Max Range", 6000f, 1000f, 15000f, 500f, "m");
                AddConfig("Gun Control", "gun_muzzle_velocity", "Muzzle Velocity", 1100f, 200f, 2000f, 50f, "m/s");

                // HUD TOGGLES (1=on, 0=off)
                AddConfig("HUD Toggles", "hud_radar", "Radar Minimap", 1f, 0f, 1f, 1f);
                AddConfig("HUD Toggles", "hud_gun_funnel", "Gun Funnel", 1f, 0f, 1f, 1f);
                AddConfig("HUD Toggles", "hud_target_brackets", "Target Brackets", 1f, 0f, 1f, 1f);
                AddConfig("HUD Toggles", "hud_gforce", "G-Force", 1f, 0f, 1f, 1f);
                AddConfig("HUD Toggles", "hud_aoa", "AOA Indexer", 1f, 0f, 1f, 1f);
                AddConfig("HUD Toggles", "hud_fpm", "Flight Path Marker", 1f, 0f, 1f, 1f);
                AddConfig("HUD Toggles", "hud_compass", "Compass", 1f, 0f, 1f, 1f);
                AddConfig("HUD Toggles", "hud_breakaway", "Breakaway Warning", 1f, 0f, 1f, 1f);

                // HUD THEME (0=Green, 1=Blue, 2=Amber, 3=White)
                AddConfig("HUD Theme", "hud_theme", "Color Theme", 0f, 0f, 3f, 1f);
            }

            private void AddConfig(string category, string name, string displayName, float defaultValue,
                                  float minValue, float maxValue, float stepSize, string unit = "")
            {
                allConfigs[name] = new ConfigParam(category, name, displayName, defaultValue,
                                                  minValue, maxValue, stepSize, unit);
            }

            private void LoadFromCustomData()
            {
                string customData = ParentProgram.Me.CustomData;
                if (string.IsNullOrEmpty(customData)) return;

                string[] lines = customData.Split('\n');
                foreach (string line in lines)
                {
                    if (line.StartsWith("Config:"))
                    {
                        string[] parts = line.Substring(7).Split(':');
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
                if (!string.IsNullOrEmpty(currentData))
                {
                    string[] lines = currentData.Split('\n');
                    foreach (string line in lines)
                    {
                        if (!line.StartsWith("Config:"))
                        {
                            sb.AppendLine(line);
                        }
                    }
                }

                // Add all config values
                foreach (var kvp in allConfigs)
                {
                    sb.AppendLine($"Config:{kvp.Key}:{kvp.Value.Value}");
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

            private static readonly string[] themeNames = { "Green", "Blue", "Amber", "White" };

            public override string[] GetOptions()
            {
                switch (currentLevel)
                {
                    case MenuLevel.Category:
                        return categories;

                    case MenuLevel.ParameterList:
                        string selectedCategory = categories[categoryIndex];
                        if (selectedCategory == "Reset All")
                        {
                            return new string[] { "Reset All to Defaults", "Back" };
                        }
                        List<string> options = new List<string>();
                        foreach (var kvp in allConfigs)
                        {
                            if (kvp.Value.Category == selectedCategory)
                            {
                                string modified = kvp.Value.IsModified ? " *" : "";
                                if (kvp.Key == "hud_theme")
                                {
                                    int idx = (int)kvp.Value.Value;
                                    string themeName = idx >= 0 && idx < themeNames.Length ? themeNames[idx] : "?";
                                    options.Add($"{kvp.Value.DisplayName}: {themeName}{modified}");
                                }
                                else if (kvp.Value.MaxValue == 1f && kvp.Value.MinValue == 0f && kvp.Value.StepSize == 1f)
                                {
                                    string toggle = kvp.Value.Value > 0.5f ? "ON" : "OFF";
                                    options.Add($"{kvp.Value.DisplayName}: {toggle}{modified}");
                                }
                                else
                                {
                                    string valueStr = kvp.Value.Value.ToString("F2").TrimEnd('0').TrimEnd('.');
                                    options.Add($"{kvp.Value.DisplayName}: {valueStr}{kvp.Value.Unit}{modified}");
                                }
                            }
                        }
                        options.Add("Reset Category");
                        options.Add("Back");
                        return options.ToArray();

                    case MenuLevel.ValueAdjust:
                        var currentParams = GetCurrentCategoryParams();
                        if (parameterIndex < currentParams.Count)
                        {
                            var param = currentParams[parameterIndex];
                            if (param.Name == "hud_theme")
                            {
                                int idx = (int)param.Value;
                                string themeName = idx >= 0 && idx < themeNames.Length ? themeNames[idx] : "?";
                                return new string[]
                                {
                                    $"Adjusting: {param.DisplayName}",
                                    "",
                                    "^ / V  Cycle Theme",
                                    $"  Current: {themeName}",
                                    "",
                                    "0-Green 1-Blue 2-Amber 3-White",
                                    "",
                                    "SELECT to save",
                                    "BACK to cancel"
                                };
                            }
                            if (param.MaxValue == 1f && param.MinValue == 0f && param.StepSize == 1f)
                            {
                                string toggle = param.Value > 0.5f ? "ON" : "OFF";
                                return new string[]
                                {
                                    $"Adjusting: {param.DisplayName}",
                                    "",
                                    "^ / V  Toggle",
                                    $"  Current: {toggle}",
                                    "",
                                    "SELECT to save",
                                    "BACK to cancel"
                                };
                            }
                            return new string[]
                            {
                                $"Adjusting: {param.DisplayName}",
                                "",
                                "^ Increase (Navigate Up)",
                                $"  Current: {param.Value:F2}{param.Unit}",
                                "V Decrease (Navigate Down)",
                                "",
                                $"Default: {param.DefaultValue:F2}{param.Unit}",
                                $"Range: {param.MinValue:F2} - {param.MaxValue:F2}{param.Unit}",
                                "",
                                "SELECT to save",
                                "BACK to cancel"
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
                        if (categories[index] == "Reset All")
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

            public override void HandleSpecialFunction(int key) { }

            public override string GetHotkeys()
            {
                return "";
            }

            public override bool HandleNavigation(bool isUp)
            {
                if (currentLevel == MenuLevel.ValueAdjust)
                {
                    var params_list = GetCurrentCategoryParams();
                    if (parameterIndex < params_list.Count)
                    {
                        var param = params_list[parameterIndex];
                        if (param.Name == "hud_theme")
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

            public override void Tick() { }
        }
    }
}
