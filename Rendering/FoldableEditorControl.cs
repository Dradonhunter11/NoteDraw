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
    /// Composite Markdown/LaTeX source editor pairing a multi-line TextBox with a
    /// synchronized left gutter showing line numbers and clickable H1-H5 section fold toggles (▼ / ▶),
    /// plus a folded-sections summary strip at the top.
    /// </summary>
    public sealed class FoldableEditorControl : Grid
    {
        private readonly EditorFoldManager _foldManager = new();
        private readonly Border _foldedBanner = new();
        private readonly WrapPanel _foldedBadgesWrap = new();
        private readonly HeadingFoldGutterControl _gutter;
        private readonly TextBox _textBox;

        private ScrollViewer? _innerScrollViewer;
        private TextPresenter? _innerPresenter;
        private ThemePalette _palette = ThemePalette.CreateClassicDark();
        private bool _isDark = true;

        public TextBox InnerTextBox => _textBox;
        public EditorFoldManager FoldManager => _foldManager;

        /// <summary>
        /// Complete unfolded document text (reconstructed from any currently folded H1-H5 sections).
        /// </summary>
        public string FullText => _foldManager.GetFullUnfoldedText(_textBox.Text ?? string.Empty);

        /// <summary>
        /// Currently visible editor text (including inline fold tokens for collapsed sections).
        /// </summary>
        public string VisibleText => _textBox.Text ?? string.Empty;

        public event EventHandler? FoldStateChanged;

        public FoldableEditorControl()
        {
            RowDefinitions = new RowDefinitions("Auto,*");

            // Top folded-sections summary banner
            _foldedBadgesWrap.Orientation = Orientation.Horizontal;
            var unfoldAllBtn = new Button
            {
                Content = "Unfold All",
                FontSize = 11,
                Padding = new Thickness(7, 2),
                Margin = new Thickness(4, 1),
                CornerRadius = new CornerRadius(4)
            };
            unfoldAllBtn.Click += (_, _) => UnfoldAllSections();

            var bannerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            bannerGrid.Children.Add(_foldedBadgesWrap);
            Grid.SetColumn(unfoldAllBtn, 1);
            bannerGrid.Children.Add(unfoldAllBtn);

            _foldedBanner.IsVisible = false;
            _foldedBanner.Padding = new Thickness(8, 4);
            _foldedBanner.BorderThickness = new Thickness(0, 0, 0, 1);
            _foldedBanner.Child = bannerGrid;
            Grid.SetRow(_foldedBanner, 0);
            Children.Add(_foldedBanner);

            // Editor + Left Gutter row
            _textBox = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                IsUndoEnabled = false,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace"),
                FontSize = 13.5,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 10, 12, 10),
                PlaceholderText = "Write Markdown, $inline math$, ```latex blocks, or ```drawing blocks..."
            };

            _gutter = new HeadingFoldGutterControl(_textBox, _foldManager);
            _gutter.Width = 58;
            _gutter.ToggleFoldRequested += (_, lineIdx) => ToggleFoldAtLine(lineIdx);

            var editorRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("58,*")
            };
            Grid.SetColumn(_gutter, 0);
            Grid.SetColumn(_textBox, 1);
            editorRow.Children.Add(_gutter);
            editorRow.Children.Add(_textBox);

            Grid.SetRow(editorRow, 1);
            Children.Add(editorRow);

            WireInternalSync();
        }

        public void LoadText(string fullText, int caretIndex = 0)
        {
            _foldManager.Clear();
            _textBox.Text = fullText ?? string.Empty;
            _textBox.CaretIndex = Math.Clamp(caretIndex, 0, (_textBox.Text ?? string.Empty).Length);
            RefreshGutterAndBanner();
        }

        public void UpdateTextPreservingCursor(string visibleText, int caretIndex)
        {
            _textBox.Text = visibleText ?? string.Empty;
            _textBox.CaretIndex = Math.Clamp(caretIndex, 0, (_textBox.Text ?? string.Empty).Length);
            RefreshGutterAndBanner();
        }

        public void ToggleFoldAtLine(int headingLineZeroBased)
        {
            string current = _textBox.Text ?? string.Empty;
            string next = _foldManager.ToggleFoldAtLine(current, headingLineZeroBased);
            if (string.Equals(current, next, StringComparison.Ordinal))
                return;

            int savedCaret = _textBox.CaretIndex;
            _textBox.Text = next;
            _textBox.CaretIndex = Math.Clamp(savedCaret, 0, next.Length);
            RefreshGutterAndBanner();
            FoldStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UnfoldAllSections()
        {
            string current = _textBox.Text ?? string.Empty;
            string next = _foldManager.UnfoldAll(current);
            if (string.Equals(current, next, StringComparison.Ordinal))
                return;

            int savedCaret = _textBox.CaretIndex;
            _textBox.Text = next;
            _textBox.CaretIndex = Math.Clamp(savedCaret, 0, next.Length);
            RefreshGutterAndBanner();
            FoldStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RefreshGutterAndBanner()
        {
            _gutter.InvalidateVisual();

            var badges = _foldManager.GetFoldedBadges(_textBox.Text ?? string.Empty);
            _foldedBadgesWrap.Children.Clear();
            _foldedBanner.IsVisible = badges.Count > 0;

            var pillBg = ThemeConfigService.ParseBrush(_palette.AccentTintColor, _isDark ? "#263B4D" : "#D6EBFF");
            var pillFg = ThemeConfigService.ParseBrush(_palette.AccentTextColor, _isDark ? "#4CC2FF" : "#005FB8");

            foreach (var b in badges)
            {
                int targetLine = b.HeadingLineZeroBased;
                var btn = new Button
                {
                    Content = $"▶ H{b.Level}: {b.Title} ({b.HiddenLineCount} lines)",
                    FontSize = 11,
                    Padding = new Thickness(7, 2),
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(4),
                    Background = pillBg,
                    Foreground = pillFg,
                    BorderThickness = new Thickness(0)
                };
                ToolTip.SetTip(btn, "Click to unfold this heading section");
                btn.Click += (_, _) => ToggleFoldAtLine(targetLine);
                _foldedBadgesWrap.Children.Add(btn);
            }
        }

        public void ApplyThemePalette(ThemePalette palette, bool isDark, double editorFontSize, string editorFontFamily)
        {
            _palette = palette;
            _isDark = isDark;

            var panelBg = ThemeConfigService.ParseBrush(palette.PanelBackground, isDark ? "#252526" : "#FFFFFF");
            var headerBg = ThemeConfigService.ParseBrush(palette.HeaderBackground, isDark ? "#2D2D2D" : "#E8E8E8");
            var border = ThemeConfigService.ParseBrush(palette.BorderColor, isDark ? "#3C3C3C" : "#D4D4D4");
            var text = ThemeConfigService.ParseBrush(palette.TextColor, isDark ? "#E0E0E0" : "#1E1E1E");

            _textBox.Background = panelBg;
            _textBox.Foreground = text;
            _textBox.FontSize = editorFontSize;
            _textBox.FontFamily = new FontFamily(editorFontFamily);

            _foldedBanner.Background = headerBg;
            _foldedBanner.BorderBrush = border;

            _gutter.ApplyThemePalette(palette, isDark);
            RefreshGutterAndBanner();
        }

        private void WireInternalSync()
        {
            _textBox.TemplateApplied += (_, _) => AttachScrollSync();
            _textBox.LayoutUpdated += (_, _) =>
            {
                if (_innerScrollViewer is null || _innerPresenter is null)
                    AttachScrollSync();
                _gutter.SetVisualHooks(_innerPresenter);
                _gutter.InvalidateVisual();
            };
            _textBox.TextChanged += (_, _) => RefreshGutterAndBanner();
        }

        private void AttachScrollSync()
        {
            var sv = _textBox.FindDescendantOfType<ScrollViewer>();
            var presenter = _textBox.FindDescendantOfType<TextPresenter>();
            if (sv is not null && !ReferenceEquals(sv, _innerScrollViewer))
            {
                _innerScrollViewer = sv;
                sv.ScrollChanged += (_, _) => _gutter.InvalidateVisual();
            }
            if (presenter is not null)
            {
                _innerPresenter = presenter;
                _gutter.SetVisualHooks(presenter);
            }
        }
    }

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
