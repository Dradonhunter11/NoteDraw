using System;
using System.IO;
using System.Text.Json;
using Avalonia.Media;

namespace NoteDraw.Services
{
    /// <summary>
    /// Loads, saves, and validates separate `dark.json` / `light.json` theme files
    /// (and custom `.json` palette files) with safe hex fallbacks.
    /// </summary>
    public sealed class ThemeConfigService
    {
        public const string DarkFileName = "dark.json";
        public const string LightFileName = "light.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static string GetOptionsFilePath()
            => Path.Combine(AppContext.BaseDirectory, "canvas", "app_options.json");

        public static string? GetSavedLastVaultPath()
        {
            try
            {
                string path = GetOptionsFilePath();
                if (!File.Exists(path))
                    return null;
                var loaded = JsonSerializer.Deserialize<AppOptionsState>(File.ReadAllText(path), JsonOptions);
                if (!string.IsNullOrWhiteSpace(loaded?.LastVaultPath) && Directory.Exists(loaded.LastVaultPath))
                    return Path.GetFullPath(loaded.LastVaultPath);
            }
            catch
            {
                // Ignore malformed options file
            }
            return null;
        }

        public ThemeConfig Config { get; private set; } = new();
        public string DarkConfigPath { get; private set; } = string.Empty;
        public string LightConfigPath { get; private set; } = string.Empty;
        public string ActiveConfigPath => IsDark ? DarkConfigPath : LightConfigPath;

        public bool IsDark => !string.Equals(Config.ActiveTheme, "Light", StringComparison.OrdinalIgnoreCase);

        public ThemePalette CurrentPalette => IsDark ? Config.Dark : Config.Light;

        public ThemeConfig Load(string? vaultPath = null)
        {
            DarkConfigPath = ResolveDarkConfigPath(vaultPath);
            LightConfigPath = ResolveLightConfigPath(vaultPath);

            Config.Dark = LoadOrSeedPalette(DarkConfigPath, DarkFileName, isDark: true);
            Config.Light = LoadOrSeedPalette(LightConfigPath, LightFileName, isDark: false);
            SyncConfigTypography();
            LoadOptions();
            return Config;
        }

        public void LoadOptions()
        {
            try
            {
                string path = GetOptionsFilePath();
                if (!File.Exists(path))
                    return;
                var loaded = JsonSerializer.Deserialize<AppOptionsState>(File.ReadAllText(path), JsonOptions);
                if (loaded is not null)
                {
                    Config.AutoSave = loaded.AutoSave;
                    if (!string.IsNullOrWhiteSpace(loaded.LastVaultPath))
                        Config.LastVaultPath = loaded.LastVaultPath;
                }
            }
            catch
            {
                // Ignore malformed options file and keep defaults
            }
        }

        public void SaveOptions()
        {
            try
            {
                string path = GetOptionsFilePath();
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var state = new AppOptionsState
                {
                    AutoSave = Config.AutoSave,
                    LastVaultPath = Config.LastVaultPath
                };
                File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
            }
            catch
            {
                // Non-fatal persistence failure
            }
        }

        public ThemePalette LoadPaletteFromFile(string filePath, bool? applyAsDark = null)
        {
            string fullPath = Path.GetFullPath(filePath);
            string fileName = Path.GetFileName(fullPath);

            ThemePalette palette;
            if (!File.Exists(fullPath))
            {
                bool seedDark = applyAsDark ?? !fileName.Contains("light", StringComparison.OrdinalIgnoreCase);
                palette = seedDark ? ThemePalette.CreateClassicDark() : ThemePalette.CreateClassicLight();
                SavePaletteToFile(palette, fullPath);
            }
            else
            {
                palette = DeserializePaletteOrDefault(
                    File.ReadAllText(fullPath),
                    defaultIsDark: applyAsDark ?? !fileName.Contains("light", StringComparison.OrdinalIgnoreCase));
            }

            bool isDark = applyAsDark ??
                (!fileName.Contains("light", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(palette.ThemeName, "Light", StringComparison.OrdinalIgnoreCase));

            NormalizePalette(palette, isDark);

            if (isDark)
            {
                Config.Dark = palette;
                DarkConfigPath = fullPath;
                Config.ActiveTheme = "Dark";
            }
            else
            {
                Config.Light = palette;
                LightConfigPath = fullPath;
                Config.ActiveTheme = "Light";
            }

            SyncConfigTypography();
            return palette;
        }

        public void SavePaletteToFile(ThemePalette palette, string filePath)
        {
            bool isDark = !Path.GetFileName(filePath).Contains("light", StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(palette.ThemeName, "Light", StringComparison.OrdinalIgnoreCase);
            var safePalette = palette ?? (isDark ? ThemePalette.CreateClassicDark() : ThemePalette.CreateClassicLight());
            NormalizePalette(safePalette, isDark);

            string fullPath = Path.GetFullPath(filePath);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, JsonSerializer.Serialize(safePalette, JsonOptions));

            if (isDark)
            {
                Config.Dark = safePalette;
                DarkConfigPath = fullPath;
            }
            else
            {
                Config.Light = safePalette;
                LightConfigPath = fullPath;
            }

            SyncConfigTypography();
        }

        public ThemeConfig LoadFromFile(string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                if (IsLightFileName(fullPath) || IsDarkFileName(fullPath))
                {
                    LoadPaletteFromFile(fullPath, applyAsDark: IsDarkFileName(fullPath));
                    return Config;
                }

                Config = new ThemeConfig();
                SaveToFile(Config, fullPath);
                return Config;
            }

            try
            {
                string json = File.ReadAllText(fullPath);
                if (LooksLikeConfigWrapper(json))
                {
                    Config = DeserializeConfigOrDefault(json);
                    DarkConfigPath = fullPath;
                    LightConfigPath = fullPath;
                    SyncConfigTypography();
                }
                else
                {
                    LoadPaletteFromFile(fullPath);
                }
            }
            catch
            {
                Config = new ThemeConfig();
                SyncConfigTypography();
            }

            return Config;
        }

        public void Save(ThemeConfig config, string? vaultPath = null)
        {
            Config = config ?? new ThemeConfig();
            DarkConfigPath = ResolveDarkConfigPath(vaultPath);
            LightConfigPath = ResolveLightConfigPath(vaultPath);
            SavePaletteToFile(Config.Dark, DarkConfigPath);
            SavePaletteToFile(Config.Light, LightConfigPath);
        }

        public void SaveToFile(ThemeConfig config, string filePath)
        {
            Config = config ?? new ThemeConfig();
            string fullPath = Path.GetFullPath(filePath);

            if (IsLightFileName(fullPath) || string.Equals(fullPath, LightConfigPath, StringComparison.OrdinalIgnoreCase))
            {
                SavePaletteToFile(Config.Light, fullPath);
                return;
            }

            if (IsDarkFileName(fullPath) || string.Equals(fullPath, DarkConfigPath, StringComparison.OrdinalIgnoreCase))
            {
                SavePaletteToFile(Config.Dark, fullPath);
                return;
            }

            DarkConfigPath = fullPath;
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, JsonSerializer.Serialize(Config, JsonOptions));
        }

        public static string ResolveDarkConfigPath(string? vaultPath)
            => ResolveNamedPath(vaultPath, DarkFileName);

        public static string ResolveLightConfigPath(string? vaultPath)
            => ResolveNamedPath(vaultPath, LightFileName);

        public static Color ParseColor(string? hex, string fallbackHex)
        {
            if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out var parsed))
                return parsed;
            return Color.TryParse(fallbackHex, out var fallback) ? fallback : Colors.Gray;
        }

        public static SolidColorBrush ParseBrush(string? hex, string fallbackHex)
            => new(ParseColor(hex, fallbackHex));

        private static string ResolveNamedPath(string? vaultPath, string fileName)
        {
            string baseDir = !string.IsNullOrWhiteSpace(vaultPath)
                ? vaultPath
                : Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(baseDir, fileName));
        }

        private static bool IsDarkFileName(string path)
            => string.Equals(Path.GetFileName(path), DarkFileName, StringComparison.OrdinalIgnoreCase);

        private static bool IsLightFileName(string path)
            => string.Equals(Path.GetFileName(path), LightFileName, StringComparison.OrdinalIgnoreCase);

        private ThemePalette LoadOrSeedPalette(string targetPath, string seedFileName, bool isDark)
        {
            if (!File.Exists(targetPath))
            {
                string repoSeed = Path.Combine(Directory.GetCurrentDirectory(), seedFileName);
                ThemePalette seeded = !string.Equals(repoSeed, targetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(repoSeed)
                    ? DeserializePaletteOrDefault(File.ReadAllText(repoSeed), isDark)
                    : (isDark ? ThemePalette.CreateClassicDark() : ThemePalette.CreateClassicLight());

                SavePaletteToFile(seeded, targetPath);
                return seeded;
            }

            try
            {
                return DeserializePaletteOrDefault(File.ReadAllText(targetPath), isDark);
            }
            catch
            {
                return isDark ? ThemePalette.CreateClassicDark() : ThemePalette.CreateClassicLight();
            }
        }

        private void SyncConfigTypography()
        {
            var p = CurrentPalette;
            NormalizePalette(p, IsDark);
            Config.EditorFontSize = p.EditorFontSize;
            Config.PreviewFontSize = p.PreviewFontSize;
            Config.EditorFontFamily = p.EditorFontFamily;
        }

        private static void NormalizePalette(ThemePalette palette, bool defaultIsDark)
        {
            if (string.IsNullOrWhiteSpace(palette.ThemeName))
                palette.ThemeName = defaultIsDark ? "Dark" : "Light";
            if (palette.EditorFontSize <= 6 || palette.EditorFontSize > 48)
                palette.EditorFontSize = 13.5;
            if (palette.PreviewFontSize <= 6 || palette.PreviewFontSize > 48)
                palette.PreviewFontSize = 14.5;
            if (string.IsNullOrWhiteSpace(palette.EditorFontFamily))
                palette.EditorFontFamily = "Cascadia Code, Consolas, Courier New, monospace";
        }

        private static bool LooksLikeConfigWrapper(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return false;
                return doc.RootElement.TryGetProperty("Dark", out _) ||
                       doc.RootElement.TryGetProperty("dark", out _) ||
                       doc.RootElement.TryGetProperty("Light", out _) ||
                       doc.RootElement.TryGetProperty("light", out _) ||
                       doc.RootElement.TryGetProperty("ActiveTheme", out _) ||
                       doc.RootElement.TryGetProperty("activeTheme", out _);
            }
            catch
            {
                return false;
            }
        }

        private static ThemePalette DeserializePaletteOrDefault(string? json, bool defaultIsDark)
        {
            ThemePalette Fallback() => defaultIsDark ? ThemePalette.CreateClassicDark() : ThemePalette.CreateClassicLight();
            if (string.IsNullOrWhiteSpace(json))
                return Fallback();

            try
            {
                var parsed = JsonSerializer.Deserialize<ThemePalette>(json, JsonOptions) ?? Fallback();
                NormalizePalette(parsed, defaultIsDark);
                return parsed;
            }
            catch
            {
                return Fallback();
            }
        }

        private static ThemeConfig DeserializeConfigOrDefault(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new ThemeConfig();

            try
            {
                var parsed = JsonSerializer.Deserialize<ThemeConfig>(json, JsonOptions) ?? new ThemeConfig();
                parsed.Dark ??= ThemePalette.CreateClassicDark();
                parsed.Light ??= ThemePalette.CreateClassicLight();
                NormalizePalette(parsed.Dark, defaultIsDark: true);
                NormalizePalette(parsed.Light, defaultIsDark: false);
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
