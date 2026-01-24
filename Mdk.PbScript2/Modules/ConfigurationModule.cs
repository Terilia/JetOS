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
            private int scrollOffset = 0;

            private string[] categories = new string[]
            {
                "Flight Control",
                "Weapons",
                "HUD & Display",
                "Radar & Sensors",
                "Warnings & Alerts",
                "Physics & Environment",
                "Advanced Settings",
                "Import/Export/Reset"
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
                // FLIGHT CONTROL CATEGORY (Critical - top priority)
                AddConfig("Flight Control", "stabilizer_kp", "Stabilizer Kp", 1.2f, 0.1f, 5.0f, 0.1f);
                AddConfig("Flight Control", "stabilizer_ki", "Stabilizer Ki", 0.0024f, 0.0f, 0.1f, 0.0001f);
                AddConfig("Flight Control", "stabilizer_kd", "Stabilizer Kd", 0.5f, 0.0f, 2.0f, 0.1f);
                AddConfig("Flight Control", "max_pid_output", "Max PID Output", 60f, 10f, 90f, 5f, "deg");
                AddConfig("Flight Control", "max_aoa", "Max AoA Limit", 36f, 10f, 60f, 1f, "deg");
                AddConfig("Flight Control", "integral_clamp", "Integral Clamp", 200f, 50f, 500f, 10f);
                AddConfig("Flight Control", "optimal_aoa_min", "Optimal AoA Min", 8f, 0f, 20f, 1f, "deg");
                AddConfig("Flight Control", "optimal_aoa_max", "Optimal AoA Max", 15f, 10f, 30f, 1f, "deg");

                // WEAPONS CATEGORY
                AddConfig("Weapons", "bombardment_spacing", "Bombardment Spacing", 4.0f, 1.0f, 20.0f, 0.5f, "m");
                AddConfig("Weapons", "circular_pattern_radius", "Circular Pattern Radius", 4.0f, 2.0f, 20.0f, 1.0f, "m");
                AddConfig("Weapons", "shoot_range_inner", "Shoot Cue Range", 1500f, 500f, 5000f, 100f, "m");
                AddConfig("Weapons", "shoot_range_outer", "In Range Cue", 2500f, 1000f, 8000f, 100f, "m");
                AddConfig("Weapons", "proximity_warning", "Proximity Warning", 500f, 100f, 1000f, 50f, "m");
                AddConfig("Weapons", "min_closure_rate", "Min Closure Rate", 100f, 10f, 500f, 10f, "m/s");

                // HUD & DISPLAY CATEGORY
                AddConfig("HUD & Display", "fov_scale_x", "FOV Scale X", 0.3434f, 0.1f, 1.0f, 0.01f);
                AddConfig("HUD & Display", "fov_scale_y", "FOV Scale Y", 0.31f, 0.1f, 1.0f, 0.01f);
                AddConfig("HUD & Display", "velocity_indicator_scale", "Velocity Indicator Scale", 20f, 5f, 50f, 1f);
                AddConfig("HUD & Display", "min_pip_distance", "Min Pip Distance", 50f, 10f, 200f, 10f, "m");
                AddConfig("HUD & Display", "max_pip_distance", "Max Pip Distance", 3000f, 1000f, 10000f, 100f, "m");
                AddConfig("HUD & Display", "max_pip_size", "Max Pip Size Factor", 0.1f, 0.01f, 0.5f, 0.01f);
                AddConfig("HUD & Display", "min_pip_size", "Min Pip Size Factor", 0.01f, 0.001f, 0.1f, 0.001f);
                AddConfig("HUD & Display", "intercept_iterations", "Intercept Iterations", 10f, 1f, 20f, 1f);

                // RADAR & SENSORS CATEGORY
                AddConfig("Radar & Sensors", "radar_range", "Radar Range", 15000f, 1000f, 30000f, 1000f, "m");
                AddConfig("Radar & Sensors", "radar_box_size", "Radar Box Size", 100f, 50f, 200f, 10f, "px");
                AddConfig("Radar & Sensors", "targeting_kp_rotor", "Targeting Pod Kp (Rotor)", 0.05f, 0.01f, 0.5f, 0.01f);
                AddConfig("Radar & Sensors", "targeting_kp_hinge", "Targeting Pod Kp (Hinge)", 0.05f, 0.01f, 0.5f, 0.01f);
                AddConfig("Radar & Sensors", "targeting_max_velocity", "Targeting Max Velocity", 5.0f, 1.0f, 10.0f, 0.5f, "RPM");

                // WARNINGS & ALERTS CATEGORY
                AddConfig("Warnings & Alerts", "bingo_fuel_percent", "Bingo Fuel %", 0.20f, 0.05f, 0.50f, 0.05f, "%");
                AddConfig("Warnings & Alerts", "low_fuel_percent", "Low Fuel %", 0.35f, 0.10f, 0.60f, 0.05f, "%");
                AddConfig("Warnings & Alerts", "altitude_warning_threshold", "Altitude Warning", 380f, 100f, 1000f, 10f, "m");
                AddConfig("Warnings & Alerts", "speed_warning_threshold", "Speed Warning", 360f, 100f, 600f, 10f, "kph");
                AddConfig("Warnings & Alerts", "low_altitude_threshold", "Low Altitude Limit", 100f, 50f, 300f, 10f, "m");
                AddConfig("Warnings & Alerts", "descent_rate_warning", "Descent Rate Warning", -5f, -20f, -1f, 1f, "m/s");

                // PHYSICS & ENVIRONMENT CATEGORY
                AddConfig("Physics & Environment", "speed_of_sound", "Speed of Sound", 343.0f, 300f, 400f, 1f, "m/s");
                AddConfig("Physics & Environment", "gravity", "Gravity", 9.81f, 0.5f, 20f, 0.1f, "m/s²");
                AddConfig("Physics & Environment", "smoothing_window", "Smoothing Window Size", 10f, 1f, 30f, 1f);

                // ADVANCED SETTINGS CATEGORY
                AddConfig("Advanced", "throttle_h2_threshold", "Throttle H2 Threshold", 0.8f, 0.5f, 1.0f, 0.05f);
                AddConfig("Advanced", "gps_cache_slots", "GPS Cache Slots", 4f, 1f, 10f, 1f);
                AddConfig("Advanced", "targeting_angle_threshold", "Targeting Angle Threshold", 2.0f, 0.1f, 10.0f, 0.1f, "deg");
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

            public override string[] GetOptions()
            {
                switch (currentLevel)
                {
                    case MenuLevel.Category:
                        return categories;

                    case MenuLevel.ParameterList:
                        string selectedCategory = categories[categoryIndex];
                        List<string> options = new List<string>();
                        foreach (var kvp in allConfigs)
                        {
                            if (kvp.Value.Category == selectedCategory)
                            {
                                string modified = kvp.Value.IsModified ? " *" : "";
                                string valueStr = kvp.Value.Value.ToString("F4").TrimEnd('0').TrimEnd('.');
                                options.Add($"{kvp.Value.DisplayName}: {valueStr}{kvp.Value.Unit}{modified}");
                            }
                        }
                        if (selectedCategory == "Import/Export/Reset")
                        {
                            options.Clear();
                            options.Add("Export Config to Antenna");
                            options.Add("Import Config from Argument");
                            options.Add("Reset All to Defaults");
                            options.Add("Back to Categories");
                        }
                        else
                        {
                            options.Add("Reset Category to Defaults");
                            options.Add("Back to Categories");
                        }
                        return options.ToArray();

                    case MenuLevel.ValueAdjust:
                        var currentParams = GetCurrentCategoryParams();
                        if (parameterIndex < currentParams.Count)
                        {
                            var param = currentParams[parameterIndex];
                            return new string[]
                            {
                                $"Adjusting: {param.DisplayName}",
                                "",
                                "^ Increase (Navigate Up)",
                                $"  Current: {param.Value:F4}{param.Unit}",
                                "V Decrease (Navigate Down)",
                                "",
                                $"Default: {param.DefaultValue:F4}{param.Unit}",
                                $"Range: {param.MinValue:F2} - {param.MaxValue:F2}{param.Unit}",
                                "",
                                "SELECT to save changes",
                                "BACK to cancel (no save)"
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
                        categoryIndex = index;
                        currentLevel = MenuLevel.ParameterList;
                        parameterIndex = 0;
                        scrollOffset = 0;
                        SystemManager.currentMenuIndex = 0; // Reset selector to first option
                        break;

                    case MenuLevel.ParameterList:
                        string selectedCategory = categories[categoryIndex];
                        if (selectedCategory == "Import/Export/Reset")
                        {
                            HandleImportExportReset(index);
                        }
                        else
                        {
                            var params_list = GetCurrentCategoryParams();
                            if (index < params_list.Count)
                            {
                                parameterIndex = index;
                                currentLevel = MenuLevel.ValueAdjust;
                                SystemManager.currentMenuIndex = 0; // Reset selector to first line
                            }
                            else if (index == params_list.Count)
                            {
                                // Reset category
                                foreach (var param in params_list)
                                {
                                    param.Reset();
                                }
                                SaveToCustomData();
                            }
                            else
                            {
                                // Back to categories
                                currentLevel = MenuLevel.Category;
                                SystemManager.currentMenuIndex = 0; // Reset selector to first category
                            }
                        }
                        break;

                    case MenuLevel.ValueAdjust:
                        // Save and go back to parameter list
                        SaveToCustomData();
                        currentLevel = MenuLevel.ParameterList;
                        SystemManager.currentMenuIndex = parameterIndex; // Return to the parameter we were editing
                        break;
                }
            }

            private void HandleImportExportReset(int index)
            {
                switch (index)
                {
                    case 0: // Export
                        ExportConfig();
                        break;
                    case 1: // Import
                        break;
                    case 2: // Reset All
                        foreach (var kvp in allConfigs)
                        {
                            kvp.Value.Reset();
                        }
                        SaveToCustomData();
                        break;
                    case 3: // Back
                        currentLevel = MenuLevel.Category;
                        break;
                }
            }

            private void ExportConfig()
            {
                StringBuilder export = new StringBuilder("JetOSConfig:");
                foreach (var kvp in allConfigs)
                {
                    if (kvp.Value.IsModified)
                    {
                        export.Append($"{kvp.Key}={kvp.Value.Value},");
                    }
                }
                string exportString = export.ToString().TrimEnd(',');

                // Try to broadcast via antenna
                List<IMyRadioAntenna> antennas = new List<IMyRadioAntenna>();
                ParentProgram.GridTerminalSystem.GetBlocksOfType(antennas);
                if (antennas.Count > 0)
                {
                    foreach (var antenna in antennas)
                    {
                        antenna.HudText = exportString;
                    }
                }
                ParentProgram.Echo(exportString);
            }

            public void ImportConfig(string configString)
            {
                if (!configString.StartsWith("JetOSConfig:")) return;

                string data = configString.Substring(12);
                string[] pairs = data.Split(',');
                foreach (string pair in pairs)
                {
                    string[] parts = pair.Split('=');
                    if (parts.Length == 2)
                    {
                        string key = parts[0];
                        float value;
                        if (allConfigs.ContainsKey(key) && float.TryParse(parts[1], out value))
                        {
                            allConfigs[key].Value = value;
                        }
                    }
                }
                SaveToCustomData();
                ParentProgram.Echo("Configuration imported successfully");
            }

            public override void HandleSpecialFunction(int key)
            {
                // No special function keys needed - using navigation instead
            }

            public override string GetHotkeys()
            {
                if (currentLevel == MenuLevel.ValueAdjust)
                {
                    return "";
                }
                else if (currentLevel == MenuLevel.ParameterList)
                {
                    return "";
                }
                return "";
            }

            public override bool HandleNavigation(bool isUp)
            {
                if (currentLevel == MenuLevel.ValueAdjust)
                {
                    // In value adjust mode, up/down changes the value
                    var params_list = GetCurrentCategoryParams();
                    if (parameterIndex < params_list.Count)
                    {
                        var param = params_list[parameterIndex];
                        if (isUp)
                        {
                            param.Adjust(1); // Increase value
                        }
                        else
                        {
                            param.Adjust(-1); // Decrease value
                        }
                        return true; // We handled navigation
                    }
                }
                return false; // Use default navigation
            }

            public override bool HandleBack()
            {
                if (currentLevel == MenuLevel.ValueAdjust)
                {
                    // Cancel editing and go back without saving
                    currentLevel = MenuLevel.ParameterList;
                    SystemManager.currentMenuIndex = parameterIndex; // Return to the parameter we were editing
                    return true; // We handled the back button
                }
                else if (currentLevel == MenuLevel.ParameterList)
                {
                    // Go back to category selection
                    currentLevel = MenuLevel.Category;
                    SystemManager.currentMenuIndex = categoryIndex; // Return to the category we were in
                    return true; // We handled the back button
                }
                // At category level, let default behavior exit the module
                return false;
            }

            public override void Tick()
            {
                // Configuration module doesn't need per-frame updates
            }
        }
    }
}
