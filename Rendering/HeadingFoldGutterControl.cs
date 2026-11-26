using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using NoteDraw.Services;

namespace NoteDraw.Rendering
{
    /// <summary>
    /// Renders line numbers and interactive H1-H5 section fold toggles (▼ / ▶)
    /// aligned with the TextBox's live TextLayout lines.
    /// </summary>
    internal sealed class HeadingFoldGutterControl : Control
    {
        private readonly TextBox _textBox;
        private readonly EditorFoldManager _foldManager;
        private readonly List<(Rect Bounds, int LineIndexZeroBased)> _foldHitTargets = new();
        private TextPresenter? _presenter;
        private ThemePalette _palette = ThemePalette.CreateClassicDark();
        private bool _isDark = true;

        public event EventHandler<int>? ToggleFoldRequested;

        public HeadingFoldGutterControl(TextBox textBox, EditorFoldManager foldManager)
        {
            _textBox = textBox;
            _foldManager = foldManager;
            ClipToBounds = true;
        }

        public void SetVisualHooks(TextPresenter? presenter) => _presenter = presenter;

        public void ApplyThemePalette(ThemePalette palette, bool isDark)
        {
            _palette = palette;
            _isDark = isDark;
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pos = e.GetPosition(this);

            foreach (var (rect, lineIdx) in _foldHitTargets)
            {
                if (rect.Contains(pos))
                {
                    ToggleFoldRequested?.Invoke(this, lineIdx);
                    e.Handled = true;
                    return;
                }
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            _foldHitTargets.Clear();

            var bounds = new Rect(Bounds.Size);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            var bgBrush = ThemeConfigService.ParseBrush(_palette.ChromeBackground, _isDark ? "#1E1E1E" : "#F3F3F3");
            var borderBrush = ThemeConfigService.ParseBrush(_palette.BorderColor, _isDark ? "#3C3C3C" : "#D4D4D4");
            var mutedBrush = ThemeConfigService.ParseBrush(_palette.MutedTextColor, _isDark ? "#9E9E9E" : "#616161");
            var accentBrush = ThemeConfigService.ParseBrush(_palette.AccentTextColor, _isDark ? "#4CC2FF" : "#005FB8");
            var foldBgBrush = ThemeConfigService.ParseBrush(_palette.AccentTintColor, _isDark ? "#263B4D" : "#D6EBFF");

            context.FillRectangle(bgBrush, bounds);
            context.DrawLine(new Pen(borderBrush, 1), new Point(bounds.Width - 0.5, 0), new Point(bounds.Width - 0.5, bounds.Height));

            string text = _textBox.Text ?? string.Empty;
            var gutterLines = _foldManager.GetGutterLines(text);
            if (gutterLines.Count == 0)
                return;

            var tfNum = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Normal);
            var tfChevron = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
            double fontSize = Math.Max(10.0, _textBox.FontSize - 2.0);

            var linePositions = ResolveLineYPositions(gutterLines, bounds.Height);
            foreach (var (info, yTop, lineH) in linePositions)
            {
                if (yTop + lineH < 0 || yTop > bounds.Height)
                    continue;

                if (info.IsFolded)
                {
                    context.FillRectangle(foldBgBrush, new Rect(0, yTop, bounds.Width - 1, lineH));
                }

                var numFt = new FormattedText(
                    info.LineNumberOneBased.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    tfNum,
                    fontSize,
                    info.IsHeadingH1ToH5 ? accentBrush : mutedBrush);

                double numX = Math.Max(2, 32 - numFt.Width);
                double numY = yTop + Math.Max(0, (lineH - numFt.Height) / 2.0);
                context.DrawText(numFt, new Point(numX, numY));

                if (info.IsHeadingH1ToH5 && info.IsFoldable)
                {
                    string glyph = info.IsFolded ? "▶" : "▼";
                    var chevronFt = new FormattedText(
                        glyph,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        tfChevron,
                        fontSize,
                        info.IsFolded ? accentBrush : mutedBrush);

                    double chevX = 38;
                    double chevY = yTop + Math.Max(0, (lineH - chevronFt.Height) / 2.0);
                    context.DrawText(chevronFt, new Point(chevX, chevY));

                    _foldHitTargets.Add((new Rect(0, yTop, bounds.Width, Math.Max(18, lineH)), info.LineIndexZeroBased));
                }
            }
        }

        private List<(EditorGutterLineInfo Info, double YTop, double Height)> ResolveLineYPositions(
            IReadOnlyList<EditorGutterLineInfo> gutterLines,
            double maxHeight)
        {
            var rows = new List<(EditorGutterLineInfo, double, double)>(gutterLines.Count);

            if (_presenter is not null && _presenter.TextLayout is { TextLines.Count: > 0 } layout)
            {
                Point? originOpt = _presenter.TranslatePoint(new Point(0, 0), this);
                double originY = originOpt?.Y ?? 10.0;

                // Build visual line Y offsets indexed by FirstTextSourceIndex
                var visualLines = new List<(int StartChar, double YTop, double Height)>(layout.TextLines.Count);
                double curY = originY;
                foreach (var tl in layout.TextLines)
                {
                    visualLines.Add((tl.FirstTextSourceIndex, curY, tl.Height));
                    curY += tl.Height;
                }

                int vIdx = 0;
                for (int i = 0; i < gutterLines.Count; i++)
                {
                    var info = gutterLines[i];
                    while (vIdx + 1 < visualLines.Count && visualLines[vIdx + 1].StartChar <= info.StartCharOffset)
                        vIdx++;

                    var vl = visualLines[Math.Min(vIdx, visualLines.Count - 1)];
                    rows.Add((info, vl.YTop, Math.Max(16.0, vl.Height)));
                }
                return rows;
            }

            double estLineH = Math.Ceiling(_textBox.FontSize * 1.45);
            double y = 10.0;
            foreach (var info in gutterLines)
            {
                rows.Add((info, y, estLineH));
                y += estLineH;
                if (y > maxHeight + 200) break;
            }
            return rows;
        }
    }
}
