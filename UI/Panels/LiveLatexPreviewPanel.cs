using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NoteDraw.Rendering.Latex;
using NoteDraw.Services;

namespace NoteDraw.UI.Panels
{
    internal sealed class LiveLatexPreviewPanel : Border, IWorkspaceThemeAware
    {
        private const string DefaultFormula = "\\int_0^\\infty e^{-x^2}\\,dx = \\frac{\\sqrt{\\pi}}{2}";

        private readonly IWorkspaceMediator _mediator;
        private readonly TextBlock _badge = new();
        private readonly LatexMathControl _mathControl = new();
        private readonly TextBlock _diagnostics = new();
        private readonly TextBox _scratchpad = new();
        private readonly List<Button> _accentButtons = new();
        private bool _syncingScratchpad;

        public LiveLatexPreviewPanel(IWorkspaceMediator mediator)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            BuildLayout();
        }

        public bool IsInputFocused => _scratchpad.IsFocused;

        public void UpdateFromCursor(string editorText, int caretIndex, bool isMarkdownDocument)
        {
            if (!isMarkdownDocument)
            {
                _mediator.UpdateLatexStatus("LaTeX: Drawing mode");
                return;
            }

            string text = editorText ?? string.Empty;
            int clampedCaret = Math.Clamp(caretIndex, 0, text.Length);
            var ctx = LatexLiveDetector.DetectAtCursor(text, clampedCaret);

            if (ctx.IsFound)
            {
                string closedNote = ctx.IsClosed ? "" : " • typing...";
                string badge = $"Active: {ctx.BlockKind} (Lines {ctx.StartLine}–{ctx.EndLine}){closedNote}";
                RenderFormula(ctx.LatexCode, badge, ctx.IsDisplayMode, ctx.IsClosed);

                _syncingScratchpad = true;
                _scratchpad.Text = ctx.LatexCode;
                _syncingScratchpad = false;
                _mediator.UpdateLatexStatus($"LaTeX: {ctx.BlockKind}");
                return;
            }

            _badge.Text = "Move cursor inside ```latex, ```math, $$...$$, or $...$ to live-preview while typing:";
            if (string.IsNullOrWhiteSpace(_scratchpad.Text))
            {
                _mathControl.LatexSource = DefaultFormula;
                _mathControl.IsDisplayMode = true;
                _diagnostics.IsVisible = false;
            }
            _mediator.UpdateLatexStatus("LaTeX: Ready");
        }

        public void ApplyTheme(ThemePalette palette, bool isDark)
        {
            Background = ThemeConfigService.ParseBrush(palette.HeaderBackground, isDark ? "#2D2D2D" : "#E8E8E8");
            BorderBrush = ThemeConfigService.ParseBrush(palette.BorderColor, isDark ? "#3C3C3C" : "#D4D4D4");
            _mathControl.Foreground = WorkspaceUiFactory.ResolveForegroundBrush(palette, isDark);
            _diagnostics.Foreground = ThemeConfigService.ParseBrush(palette.WarningColor, isDark ? "#FCE100" : "#9D5D00");

            IBrush accent = WorkspaceUiFactory.ResolveAccentBrush(palette, isDark);
            foreach (var btn in _accentButtons)
            {
                btn.Background = accent;
                btn.Foreground = Brushes.White;
            }
        }

        private void RenderFormula(string latexCode, string badgeText, bool isDisplayMode, bool isClosed)
        {
            _badge.Text = badgeText;
            _mathControl.IsDisplayMode = isDisplayMode;
            _mathControl.LatexSource = latexCode;

            string diag = _mathControl.DiagnosticsText;
            if (!isClosed && string.IsNullOrEmpty(diag))
                diag = "Live preview active — waiting for closing delimiter";

            _diagnostics.Text = diag;
            _diagnostics.IsVisible = !string.IsNullOrEmpty(diag);
        }

        private void BuildLayout()
        {
            _badge.FontSize = 11;
            _badge.FontWeight = FontWeight.SemiBold;
            _badge.VerticalAlignment = VerticalAlignment.Center;

            _mathControl.FontSize = 20;
            _mathControl.IsDisplayMode = true;
            _mathControl.MinHeight = 56;
            _mathControl.HorizontalAlignment = HorizontalAlignment.Center;

            _diagnostics.FontSize = 11;
            _diagnostics.IsVisible = false;

            _scratchpad.PlaceholderText = "LaTeX quick scratchpad (e.g. \\frac{\\partial u}{\\partial t} = \\alpha \\nabla^2 u) — previews live here";
            _scratchpad.FontSize = 12;
            _scratchpad.FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace");
            _scratchpad.TextChanged += (_, _) =>
            {
                if (_syncingScratchpad)
                    return;
                string code = _scratchpad.Text ?? string.Empty;
                RenderFormula(code, "Scratchpad Live Preview", isDisplayMode: true, isClosed: true);
            };

            var insertFromScratchBtn = WorkspaceUiFactory.CreateToolbarButton(
                "Insert at Caret",
                () =>
                {
                    string tex = _scratchpad.Text?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(tex))
                        _mediator.InsertLatexSnippet(tex);
                },
                accent: true,
                accentBrush: _mediator.CurrentAccentBrush,
                accentRegistry: _accentButtons);

            var scratchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
            scratchRow.Children.Add(_scratchpad);
            Grid.SetColumn(insertFromScratchBtn, 1);
            scratchRow.Children.Add(insertFromScratchBtn);

            var previewScroll = new ScrollViewer
            {
                MaxHeight = 140,
                MinHeight = 64,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = _mathControl
            };

            var content = new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(12, 8),
                Children = { _badge, previewScroll, _diagnostics, scratchRow }
            };

            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            grid.Children.Add(WorkspaceUiFactory.CreateViewHeader(
                "LIVE LATEX CODE PREVIEW",
                WorkspaceViewKind.LiveLatexPreview,
                _mediator.CloseView));
            Grid.SetRow(content, 1);
            grid.Children.Add(content);

            BorderThickness = new Thickness(0, 1, 0, 0);
            Child = grid;
        }
    }
}
