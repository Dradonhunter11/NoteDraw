using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NoteDraw.Rendering.Latex
{
    /// <summary>
    /// Avalonia Control that parses and renders a LaTeX math expression using vector graphics.
    /// Automatically re-measures and redraws whenever <see cref="LatexSource"/>, <see cref="FontSize"/>,
    /// <see cref="IsDisplayMode"/>, or <see cref="Foreground"/> changes.
    /// </summary>
    public sealed class LatexMathControl : Control
    {
        public static readonly StyledProperty<string> LatexSourceProperty =
            AvaloniaProperty.Register<LatexMathControl, string>(nameof(LatexSource), string.Empty);

        public static readonly StyledProperty<double> FontSizeProperty =
            AvaloniaProperty.Register<LatexMathControl, double>(nameof(FontSize), 18.0);

        public static readonly StyledProperty<bool> IsDisplayModeProperty =
            AvaloniaProperty.Register<LatexMathControl, bool>(nameof(IsDisplayMode), true);

        public static readonly StyledProperty<IBrush?> ForegroundProperty =
            AvaloniaProperty.Register<LatexMathControl, IBrush?>(nameof(Foreground), Brushes.White);

        private LatexParseResult _parseResult = new(SequenceNode.Empty, Array.Empty<string>());
        private MathBox _measuredBox = MathBox.Empty;

        static LatexMathControl()
        {
            AffectsMeasure<LatexMathControl>(LatexSourceProperty, FontSizeProperty, IsDisplayModeProperty);
            AffectsRender<LatexMathControl>(LatexSourceProperty, FontSizeProperty, IsDisplayModeProperty, ForegroundProperty);
        }

        public string LatexSource
        {
            get => GetValue(LatexSourceProperty);
            set => SetValue(LatexSourceProperty, value);
        }

        public double FontSize
        {
            get => GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public bool IsDisplayMode
        {
            get => GetValue(IsDisplayModeProperty);
            set => SetValue(IsDisplayModeProperty, value);
        }

        public IBrush? Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        /// <summary>
        /// Non-blocking parse diagnostics (e.g. unclosed braces while typing).
        /// </summary>
        public string DiagnosticsText => _parseResult.Diagnostics.Count == 0
            ? string.Empty
            : string.Join(" | ", _parseResult.Diagnostics);

        public bool HasWarnings => _parseResult.HasWarnings;

        public event EventHandler? ParsedChanged;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == LatexSourceProperty ||
                change.Property == FontSizeProperty ||
                change.Property == IsDisplayModeProperty ||
                change.Property == ForegroundProperty)
            {
                RebuildLayout();
            }
        }

        private void RebuildLayout()
        {
            var brush = Foreground ?? Brushes.White;
            _parseResult = LatexParser.Parse(LatexSource, IsDisplayMode);
            _measuredBox = LatexMathRenderer.Measure(_parseResult.Root, FontSize, brush, IsDisplayMode);
            ParsedChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var brush = Foreground ?? Brushes.White;
            _parseResult = LatexParser.Parse(LatexSource, IsDisplayMode);
            _measuredBox = LatexMathRenderer.Measure(_parseResult.Root, FontSize, brush, IsDisplayMode);
            return new Size(_measuredBox.Width + 12, _measuredBox.Height + 10);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var brush = Foreground ?? Brushes.White;
            _parseResult = LatexParser.Parse(LatexSource, IsDisplayMode);
            _measuredBox = LatexMathRenderer.Measure(_parseResult.Root, FontSize, brush, IsDisplayMode);

            double x = IsDisplayMode ? Math.Max(6, (Bounds.Width - _measuredBox.Width) / 2.0) : 4.0;
            double y = Math.Max(4, (Bounds.Height - _measuredBox.Height) / 2.0);
            _measuredBox.Draw(context, new Point(x, y));
        }
    }
}
