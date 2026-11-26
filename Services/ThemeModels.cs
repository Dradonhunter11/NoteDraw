namespace NoteDraw.Services
{
    /// <summary>
    /// Color palette and typography tokens loaded from `dark.json` or `light.json`
    /// (or any custom theme JSON file).
    /// </summary>
    public sealed class ThemePalette
    {
        public string ThemeName { get; set; } = "Dark";
        public double EditorFontSize { get; set; } = 13.5;
        public double PreviewFontSize { get; set; } = 14.5;
        public string EditorFontFamily { get; set; } = "Cascadia Code, Consolas, Courier New, monospace";

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
        public string CanvasBackground { get; set; } = "#FFFFFF";
        public string CanvasGridColor { get; set; } = "#E5E5E5";

        public static ThemePalette CreateClassicDark() => new();

        public static ThemePalette CreateClassicLight() => new()
        {
            ThemeName = "Light",
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
    /// Combined runtime theme state backed by separate `dark.json` and `light.json` files.
    /// </summary>
    public sealed class ThemeConfig
    {
        public string ActiveTheme { get; set; } = "Dark";
        public double EditorFontSize { get; set; } = 13.5;
        public double PreviewFontSize { get; set; } = 14.5;
        public string EditorFontFamily { get; set; } = "Cascadia Code, Consolas, Courier New, monospace";
        public bool AutoSave { get; set; } = true;
        public string? LastVaultPath { get; set; }
        public ThemePalette Dark { get; set; } = ThemePalette.CreateClassicDark();
        public ThemePalette Light { get; set; } = ThemePalette.CreateClassicLight();
    }

    internal sealed class AppOptionsState
    {
        public bool AutoSave { get; set; } = true;
        public string? LastVaultPath { get; set; }
    }
}
