using System;
using System.IO;
using System.Text.Json;
using Avalonia.Media;

namespace NoteDraw.Services
{
    /// <summary>
    /// Color palette tokens for either Dark or Light mode.
    /// Edit via `theme.json` to customize all NoteDraw surfaces, borders, text, and canvas colors.
    /// </summary>
    public sealed class ThemePalette
    {
        public string ChromeBackground { get; set; } = "#1E1E1E";
        public string PanelBackground { get; set; } = "#252526";
        public string HeaderBackground { get; set; } = "#2D2D2D";
        public string CardBackground { get; set; } = "#2A2D2E";
        public string CodeBackground { get; set; } = "#181818";
        public string BorderColor { get; set; } = "#3C3C3C";
        public string TextColor { get; set; } = "#E0E0E0";
        public string MutedTextColor { get; set; } = "#9E9E9E";
        public string AccentColor { get; set; } = "#0078D4";
        public string AccentTextColor { get; set; } = "#4CC2FF";
        public string AccentTintColor { get; set; } = "#263B4D";
        public string TagColor { get; set; } = "#6CCB5F";
        public string TagTintColor { get; set; } = "#233B23";
        public string WarningColor { get; set; } = "#FCE100";
        public string CanvasBackground { get; set; } = "#1E1E1E";
        public string CanvasGridColor { get; set; } = "#2E2E2E";

        public static ThemePalette CreateClassicDark() => new();

        public static ThemePalette CreateClassicLight() => new()
        {
            ChromeBackground = "#F3F3F3",
            PanelBackground = "#FFFFFF",
            HeaderBackground = "#E8E8E8",
            CardBackground = "#F8F8F8",
            CodeBackground = "#F0F0F0",
            BorderColor = "#D4D4D4",
            TextColor = "#1E1E1E",
            MutedTextColor = "#616161",
            AccentColor = "#0067C0",
            AccentTextColor = "#005FB8",
            AccentTintColor = "#D6EBFF",
            TagColor = "#107C10",
            TagTintColor = "#DFF6DD",
            WarningColor = "#9D5D00",
            CanvasBackground = "#FFFFFF",
            CanvasGridColor = "#E5E5E5"
        };
    }

    /// <summary>
    /// Root JSON structure persisted in `theme.json`.
    /// </summary>
    public sealed class ThemeConfig
    {
        public string ActiveTheme { get; set; } = "Dark";
        public double EditorFontSize { get; set; } = 13.5;
        public double PreviewFontSize { get; set; } = 14.5;
        public string EditorFontFamily { get; set; } = "Cascadia Code, Consolas, Courier New, monospace";
        public ThemePalette Dark { get; set; } = ThemePalette.CreateClassicDark();
        public ThemePalette Light { get; set; } = ThemePalette.CreateClassicLight();
    }

    /// <summary>
    /// Loads, saves, and validates `theme.json` theme customizations with safe fallbacks.
    /// </summary>
    public sealed class ThemeConfigService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public ThemeConfig Config { get; private set; } = new();
        public string ActiveConfigPath { get; private set; } = string.Empty;

        public bool IsDark => !string.Equals(Config.ActiveTheme, "Light", StringComparison.OrdinalIgnoreCase);

        public ThemePalette CurrentPalette => IsDark ? Config.Dark : Config.Light;

        public ThemeConfig Load(string? vaultPath = null)
        {
            string targetPath = ResolveConfigPath(vaultPath);
            ActiveConfigPath = targetPath;

            if (!File.Exists(targetPath))
            {
                string repoSeed = Path.Combine(Directory.GetCurrentDirectory(), "theme.json");
                if (!string.Equals(repoSeed, targetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(repoSeed))
                {
                    Config = DeserializeOrDefault(File.ReadAllText(repoSeed));
                }
                else
                {
                    Config = new ThemeConfig();
                }

                Save(Config, vaultPath);
                return Config;
            }

            try
            {
                Config = DeserializeOrDefault(File.ReadAllText(targetPath));
            }
            catch
            {
                Config = new ThemeConfig();
            }

            return Config;
        }

        public ThemeConfig LoadFromFile(string filePath)
        {
            ActiveConfigPath = Path.GetFullPath(filePath);
            if (!File.Exists(ActiveConfigPath))
            {
                Config = new ThemeConfig();
                SaveToFile(Config, ActiveConfigPath);
                return Config;
            }

            try
            {
                Config = DeserializeOrDefault(File.ReadAllText(ActiveConfigPath));
            }
            catch
            {
                Config = new ThemeConfig();
            }

            return Config;
        }

        public void Save(ThemeConfig config, string? vaultPath = null)
        {
            Config = config ?? new ThemeConfig();
            string targetPath = ResolveConfigPath(vaultPath);
            SaveToFile(Config, targetPath);
        }

        public void SaveToFile(ThemeConfig config, string filePath)
        {
            Config = config ?? new ThemeConfig();
            ActiveConfigPath = Path.GetFullPath(filePath);
            string? dir = Path.GetDirectoryName(ActiveConfigPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(ActiveConfigPath, JsonSerializer.Serialize(Config, JsonOptions));
        }

        public static string ResolveConfigPath(string? vaultPath)
        {
            if (!string.IsNullOrWhiteSpace(vaultPath))
                return Path.GetFullPath(Path.Combine(vaultPath, "theme.json"));

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "theme.json"));
        }

        public static Color ParseColor(string? hex, string fallbackHex)
        {
            if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out var parsed))
                return parsed;
            return Color.TryParse(fallbackHex, out var fallback) ? fallback : Colors.Gray;
        }

        public static SolidColorBrush ParseBrush(string? hex, string fallbackHex)
            => new(ParseColor(hex, fallbackHex));

        private static ThemeConfig DeserializeOrDefault(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new ThemeConfig();

            try
            {
                var parsed = JsonSerializer.Deserialize<ThemeConfig>(json, JsonOptions) ?? new ThemeConfig();
                parsed.Dark ??= ThemePalette.CreateClassicDark();
                parsed.Light ??= ThemePalette.CreateClassicLight();
                if (parsed.EditorFontSize <= 6 || parsed.EditorFontSize > 48)
                    parsed.EditorFontSize = 13.5;
                if (parsed.PreviewFontSize <= 6 || parsed.PreviewFontSize > 48)
                    parsed.PreviewFontSize = 14.5;
                if (string.IsNullOrWhiteSpace(parsed.EditorFontFamily))
                    parsed.EditorFontFamily = "Cascadia Code, Consolas, Courier New, monospace";
                return parsed;
            }
            catch
            {
                return new ThemeConfig();
            }
        }
    }
}
