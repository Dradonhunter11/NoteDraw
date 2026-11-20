using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NoteDraw.Services;

namespace NoteDraw.Drawing
{
    /// <summary>
    /// Complete interactive drawing studio containing the drawing toolbar,
    /// white vector canvas with live cursor preview, right-click drawing sub-menu
    /// with 2D HSV/RGB color selector, and inline Text/LaTeX prompt.
    /// </summary>
    public sealed class DrawingWorkspaceControl : Border
    {
        private readonly DrawingCanvasControl _canvas;
        private readonly Dictionary<DrawingToolKind, List<Button>> _toolButtons = new();
        private readonly Button _undoBtn;
        private readonly Button _redoBtn;
        private readonly Button _colorSelectorBtn;
        private readonly Border _colorBtnDot = new();
        private readonly TextBlock _colorBtnText = new();
        private readonly ComboBox _backgroundCombo;
        private readonly Slider _thicknessSlider;
        private readonly TextBlock _thicknessLabel;
        private readonly TextBlock _zoomLabel;
        private readonly Border _textPromptBar = new();
        private readonly TextBox _textPromptBox;

        // Floating Color Selector & Right-Click Sub-Menu overlay controls
        private readonly Border _canvasFrame = new();
        private readonly Avalonia.Controls.Canvas _popupLayer = new();
        private readonly Border _contextSubMenuPopup = new();
        private readonly ColorSpectrumPickerControl _spectrumPicker = new();
        private readonly Slider _hueSlider = new();
        private readonly Slider _redSlider = new();
        private readonly Slider _greenSlider = new();
        private readonly Slider _blueSlider = new();
        private readonly TextBlock _redValLabel = new();
        private readonly TextBlock _greenValLabel = new();
        private readonly TextBlock _blueValLabel = new();
        private readonly TextBox _colorHexInput = new();
        private readonly Border _selectorPreviewSwatch = new();
        private readonly Slider _popupThicknessSlider = new();
        private readonly TextBlock _popupThicknessLabel = new();

        private Point _pendingTextPosition = new(80, 80);
        private bool _syncingBackground;
        private bool _syncingThickness;
        private bool _syncingColorSelector;
        private ThemePalette? _palette;

        public DrawingCanvasControl Canvas => _canvas;
        public bool IsContextSubMenuOpen => _contextSubMenuPopup.IsVisible;

        public bool Undo() => _canvas.Undo();
        public bool Redo() => _canvas.Redo();

        public event EventHandler? DocumentChanged;

        public DrawingWorkspaceControl(DrawingDocument? initialDocument = null, bool compactInlineMode = false, bool isDarkTheme = true)
        {
            _canvas = new DrawingCanvasControl
            {
                IsDarkTheme = isDarkTheme,
                MinHeight = compactInlineMode ? 320 : 480
            };
            if (initialDocument is not null)
                _canvas.LoadDocument(initialDocument);

            CornerRadius = new CornerRadius(8);
            BorderThickness = new Thickness(1);
            Padding = new Thickness(8);

            var rootGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*")
            };

            // Row 0: Primary Drawing Toolbar (Undo / Redo + tools + compact Color Selector button)
            var toolbarWrap = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6)
            };

            _undoBtn = CreateActionButton("↶ Undo", () => _canvas.Undo());
            _redoBtn = CreateActionButton("↷ Redo", () => _canvas.Redo());
            _undoBtn.FontWeight = FontWeight.SemiBold;
            _redoBtn.FontWeight = FontWeight.SemiBold;
            ToolTip.SetTip(_undoBtn, "Undo Drawing Action (Ctrl+Z)");
            ToolTip.SetTip(_redoBtn, "Redo Drawing Action (Ctrl+Y)");
            toolbarWrap.Children.Add(_undoBtn);
            toolbarWrap.Children.Add(_redoBtn);
            toolbarWrap.Children.Add(CreateSeparator());

            AddToolButton(toolbarWrap, DrawingToolKind.Pen, "Pen");
            AddToolButton(toolbarWrap, DrawingToolKind.Highlighter, "Highlight");
            AddToolButton(toolbarWrap, DrawingToolKind.Eraser, "Eraser");
            AddToolButton(toolbarWrap, DrawingToolKind.Line, "Line");
            AddToolButton(toolbarWrap, DrawingToolKind.Arrow, "Arrow");
            AddToolButton(toolbarWrap, DrawingToolKind.Rectangle, "Rect");
            AddToolButton(toolbarWrap, DrawingToolKind.Ellipse, "Ellipse");
            AddToolButton(toolbarWrap, DrawingToolKind.TextLabel, "Text/$LaTeX$");
            AddToolButton(toolbarWrap, DrawingToolKind.SelectPan, "Move/Pan");

            toolbarWrap.Children.Add(CreateSeparator());

            // Compact Color Selector trigger button (opens the full color selector & sub-menu)
            _colorBtnDot.Width = 14;
            _colorBtnDot.Height = 14;
            _colorBtnDot.CornerRadius = new CornerRadius(7);
            _colorBtnDot.BorderThickness = new Thickness(1);
            _colorBtnDot.BorderBrush = new SolidColorBrush(Color.Parse("#64748B"));

            _colorBtnText.FontSize = 11;
            _colorBtnText.FontWeight = FontWeight.SemiBold;
            _colorBtnText.VerticalAlignment = VerticalAlignment.Center;

            _colorSelectorBtn = new Button
            {
                Padding = new Thickness(8, 4),
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(5),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { _colorBtnDot, _colorBtnText }
                }
            };
            ToolTip.SetTip(_colorSelectorBtn, "Open Color Selector & Drawing Sub-Menu (or right-click on canvas)");
            _colorSelectorBtn.Click += (_, _) =>
            {
                if (_contextSubMenuPopup.IsVisible)
                    _contextSubMenuPopup.IsVisible = false;
                else
                    ShowContextSubMenuAt(new Point(10, 8));
            };
            toolbarWrap.Children.Add(_colorSelectorBtn);

            toolbarWrap.Children.Add(CreateSeparator());

            // Thickness slider
            _thicknessLabel = new TextBlock
            {
                Text = "3px",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Margin = new Thickness(2, 0, 4, 0)
            };
            _thicknessSlider = new Slider
            {
                Minimum = 1,
                Maximum = 24,
                Value = 3,
                Width = 75,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            _thicknessSlider.PropertyChanged += (_, args) =>
            {
                if (!_syncingThickness && args.Property == Slider.ValueProperty)
                    SetActiveThickness(_thicknessSlider.Value);
            };
            toolbarWrap.Children.Add(_thicknessSlider);
            toolbarWrap.Children.Add(_thicknessLabel);

            // Background selector
            _backgroundCombo = new ComboBox
            {
                ItemsSource = new[]
                {
                    CanvasBackgroundKind.Grid,
                    CanvasBackgroundKind.Dotted,
                    CanvasBackgroundKind.Ruled,
                    CanvasBackgroundKind.Blank
                },
                SelectedItem = _canvas.Document.Background,
                MinWidth = 86,
                FontSize = 11,
                Margin = new Thickness(4, 2)
            };
            _backgroundCombo.SelectionChanged += (_, _) =>
            {
                if (!_syncingBackground && _backgroundCombo.SelectedItem is CanvasBackgroundKind bg)
                    _canvas.SetBackground(bg);
            };
            toolbarWrap.Children.Add(_backgroundCombo);

            toolbarWrap.Children.Add(CreateSeparator());

            // Actions: Clear, Zoom, Export
            toolbarWrap.Children.Add(CreateActionButton("Clear", () => _canvas.ClearCanvas()));
            toolbarWrap.Children.Add(CreateActionButton("-", () => _canvas.SetZoom(_canvas.Zoom / 1.15)));
            _zoomLabel = new TextBlock
            {
                Text = "100%",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Margin = new Thickness(4, 0)
            };
            toolbarWrap.Children.Add(_zoomLabel);
            toolbarWrap.Children.Add(CreateActionButton("+", () => _canvas.SetZoom(_canvas.Zoom * 1.15)));
            toolbarWrap.Children.Add(CreateActionButton("1:1", () => _canvas.ResetZoomAndPan()));
            toolbarWrap.Children.Add(CreateActionButton("PNG", async () => await ExportImageAsync(isSvg: false)));
            toolbarWrap.Children.Add(CreateActionButton("SVG", async () => await ExportImageAsync(isSvg: true)));

            Grid.SetRow(toolbarWrap, 0);
            rootGrid.Children.Add(toolbarWrap);

            // Row 1: Inline Text / LaTeX Annotation Prompt Bar
            _textPromptBox = new TextBox
            {
                PlaceholderText = "Type text or $LaTeX$ (e.g. $\\frac{a}{b}$ or \\int_0^1 x\\,dx) and press Enter...",
                MinWidth = 300,
                FontSize = 12
            };
            _textPromptBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitTextPrompt();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    _textPromptBar.IsVisible = false;
                    e.Handled = true;
                }
            };

            var placeBtn = CreateActionButton("Place on Canvas", CommitTextPrompt);
            var cancelBtn = CreateActionButton("Cancel", () => _textPromptBar.IsVisible = false);

            var promptPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Canvas Note / LaTeX:",
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold
                    },
                    _textPromptBox,
                    placeBtn,
                    cancelBtn
                }
            };

            _textPromptBar.IsVisible = false;
            _textPromptBar.CornerRadius = new CornerRadius(6);
            _textPromptBar.Padding = new Thickness(8, 5);
            _textPromptBar.Margin = new Thickness(0, 0, 0, 6);
            _textPromptBar.Child = promptPanel;
            Grid.SetRow(_textPromptBar, 1);
            rootGrid.Children.Add(_textPromptBar);

            // Row 2: Drawing Canvas + Floating Right-Click Color Selector Sub-Menu
            BuildContextSubMenuPopup();

            _popupLayer.Background = null;
            _popupLayer.Children.Add(_contextSubMenuPopup);

            var canvasHostGrid = new Grid();
            canvasHostGrid.Children.Add(_canvas);
            canvasHostGrid.Children.Add(_popupLayer);

            _canvasFrame.CornerRadius = new CornerRadius(6);
            _canvasFrame.BorderThickness = new Thickness(1);
            _canvasFrame.ClipToBounds = true;
            _canvasFrame.Child = canvasHostGrid;
            if (compactInlineMode)
            {
                _canvasFrame.Height = 360;
            }
            Grid.SetRow(_canvasFrame, 2);
            rootGrid.Children.Add(_canvasFrame);

            Child = rootGrid;

            _canvas.DocumentChanged += (_, e) =>
            {
                SyncBackgroundCombo();
                DocumentChanged?.Invoke(this, e);
            };
            _canvas.HistoryChanged += (_, _) => RefreshHistoryButtons();
            _canvas.ViewportChanged += (_, _) => _zoomLabel.Text = $"{Math.Round(_canvas.Zoom * 100)}%";
            _canvas.TextLabelPlacementRequested += (_, pos) =>
            {
                _pendingTextPosition = pos;
                _textPromptBar.IsVisible = true;
                _textPromptBox.Focus();
            };
            _canvas.ContextSubMenuRequested += (_, screenPos) => ShowContextSubMenuAt(screenPos);
            _canvas.CanvasLeftClicked += (_, _) => _contextSubMenuPopup.IsVisible = false;

            SelectCustomColor(_canvas.ActiveColorHex);
            ApplyTheme(isDarkTheme);
            RefreshSelectionVisuals();
            RefreshHistoryButtons();
        }

        private void BuildContextSubMenuPopup()
        {
            // 1. Header
            var closePopupBtn = new Button
            {
                Content = "×",
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Padding = new Thickness(6, 1),
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            closePopupBtn.Click += (_, _) => _contextSubMenuPopup.IsVisible = false;

            var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            headerGrid.Children.Add(new TextBlock
            {
                Text = "🎨 Drawing Sub-Menu & Color Selector",
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(closePopupBtn, 1);
            headerGrid.Children.Add(closePopupBtn);

            // 2. 2D Saturation/Value Spectrum + Hue Bar
            _spectrumPicker.Height = 115;
            _spectrumPicker.ColorPicked += (_, hsv) =>
            {
                var c = HsvToColor(hsv.H, hsv.S, hsv.V);
                ApplyColorFromControls(c);
            };

            _hueSlider.Minimum = 0;
            _hueSlider.Maximum = 360;
            _hueSlider.VerticalAlignment = VerticalAlignment.Center;
            _hueSlider.PropertyChanged += (_, args) =>
            {
                if (!_syncingColorSelector && args.Property == Slider.ValueProperty)
                {
                    _spectrumPicker.SetHsv(_hueSlider.Value, _spectrumPicker.Saturation, _spectrumPicker.Value);
                    var c = HsvToColor(_hueSlider.Value, _spectrumPicker.Saturation, _spectrumPicker.Value);
                    ApplyColorFromControls(c, syncSpectrum: false);
                }
            };

            var rainbowBorder = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(4, 1),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#FF0000"), 0.00),
                        new GradientStop(Color.Parse("#FFFF00"), 0.17),
                        new GradientStop(Color.Parse("#00FF00"), 0.33),
                        new GradientStop(Color.Parse("#00FFFF"), 0.50),
                        new GradientStop(Color.Parse("#0000FF"), 0.67),
                        new GradientStop(Color.Parse("#FF00FF"), 0.83),
                        new GradientStop(Color.Parse("#FF0000"), 1.00)
                    }
                },
                Child = _hueSlider
            };

            // 3. RGB Sliders + Live Preview Swatch + Hex Input
            var rgbStack = new StackPanel { Spacing = 2 };
            rgbStack.Children.Add(CreateChannelRow("R", _redSlider, _redValLabel));
            rgbStack.Children.Add(CreateChannelRow("G", _greenSlider, _greenValLabel));
            rgbStack.Children.Add(CreateChannelRow("B", _blueSlider, _blueValLabel));

            _selectorPreviewSwatch.Width = 28;
            _selectorPreviewSwatch.Height = 24;
            _selectorPreviewSwatch.CornerRadius = new CornerRadius(5);
            _selectorPreviewSwatch.BorderThickness = new Thickness(1);
            _selectorPreviewSwatch.BorderBrush = new SolidColorBrush(Color.Parse("#64748B"));

            _colorHexInput.FontSize = 11.5;
            _colorHexInput.FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace");
            _colorHexInput.PlaceholderText = "#RRGGBB";
            _colorHexInput.TextChanged += (_, _) =>
            {
                if (_syncingColorSelector) return;
                string raw = (_colorHexInput.Text ?? string.Empty).Trim();
                if (Color.TryParse(raw, out var parsed))
                {
                    SelectCustomColor($"#{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}");
                }
            };

            var hexRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6 };
            hexRow.Children.Add(_selectorPreviewSwatch);
            Grid.SetColumn(_colorHexInput, 1);
            hexRow.Children.Add(_colorHexInput);

            // 4. Quick Preset Palette Chips (12 common colors)
            var presetsWrap = new WrapPanel { Orientation = Orientation.Horizontal };
            var presets = new (string Hex, string Name)[]
            {
                ("#0F172A", "Ink Black"),
                ("#3B82F6", "Blue"),
                ("#EF4444", "Red"),
                ("#10B981", "Green"),
                ("#F59E0B", "Orange"),
                ("#A855F7", "Purple"),
                ("#06B6D4", "Teal"),
                ("#FACC15", "Yellow"),
                ("#EC4899", "Pink"),
                ("#78350F", "Brown"),
                ("#64748B", "Slate"),
                ("#FFFFFF", "White")
            };
            foreach (var (hex, name) in presets)
            {
                string colorHex = hex;
                var chip = new Button
                {
                    Width = 22,
                    Height = 22,
                    Padding = new Thickness(0),
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(11),
                    Background = new SolidColorBrush(Color.Parse(colorHex)),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.Parse("#64748B"))
                };
                ToolTip.SetTip(chip, $"{name} ({colorHex})");
                chip.Click += (_, _) => SelectCustomColor(colorHex);
                presetsWrap.Children.Add(chip);
            }

            // 5. Quick Tool & Brush Size Section
            var quickToolsWrap = new WrapPanel { Orientation = Orientation.Horizontal };
            AddToolButton(quickToolsWrap, DrawingToolKind.Pen, "Pen");
            AddToolButton(quickToolsWrap, DrawingToolKind.Highlighter, "Highlight");
            AddToolButton(quickToolsWrap, DrawingToolKind.Eraser, "Eraser");
            AddToolButton(quickToolsWrap, DrawingToolKind.Line, "Line");
            AddToolButton(quickToolsWrap, DrawingToolKind.Arrow, "Arrow");
            AddToolButton(quickToolsWrap, DrawingToolKind.Rectangle, "Rect");
            AddToolButton(quickToolsWrap, DrawingToolKind.Ellipse, "Ellipse");
            AddToolButton(quickToolsWrap, DrawingToolKind.TextLabel, "Text");
            AddToolButton(quickToolsWrap, DrawingToolKind.SelectPan, "Move");

            _popupThicknessSlider.Minimum = 1;
            _popupThicknessSlider.Maximum = 24;
            _popupThicknessSlider.Value = 3;
            _popupThicknessSlider.VerticalAlignment = VerticalAlignment.Center;
            _popupThicknessSlider.PropertyChanged += (_, args) =>
            {
                if (!_syncingThickness && args.Property == Slider.ValueProperty)
                    SetActiveThickness(_popupThicknessSlider.Value);
            };
            _popupThicknessLabel.Text = "3px";
            _popupThicknessLabel.FontSize = 11;
            _popupThicknessLabel.VerticalAlignment = VerticalAlignment.Center;

            var sizeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
            sizeRow.Children.Add(new TextBlock
            {
                Text = "Size:",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(_popupThicknessSlider, 1);
            sizeRow.Children.Add(_popupThicknessSlider);
            Grid.SetColumn(_popupThicknessLabel, 2);
            sizeRow.Children.Add(_popupThicknessLabel);

            _contextSubMenuPopup.IsVisible = false;
            _contextSubMenuPopup.Width = 276;
            _contextSubMenuPopup.CornerRadius = new CornerRadius(8);
            _contextSubMenuPopup.BorderThickness = new Thickness(1);
            _contextSubMenuPopup.Padding = new Thickness(10);
            _contextSubMenuPopup.Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    headerGrid,
                    _spectrumPicker,
                    rainbowBorder,
                    rgbStack,
                    hexRow,
                    presetsWrap,
                    new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(70, 148, 163, 184)) },
                    quickToolsWrap,
                    sizeRow
                }
            };
        }

        private Grid CreateChannelRow(string label, Slider slider, TextBlock valLabel)
        {
            slider.Minimum = 0;
            slider.Maximum = 255;
            slider.VerticalAlignment = VerticalAlignment.Center;
            slider.PropertyChanged += (_, args) =>
            {
                if (!_syncingColorSelector && args.Property == Slider.ValueProperty)
                {
                    var c = Color.FromRgb(
                        (byte)Math.Round(_redSlider.Value),
                        (byte)Math.Round(_greenSlider.Value),
                        (byte)Math.Round(_blueSlider.Value));
                    ApplyColorFromControls(c);
                }
            };

            valLabel.Text = "0";
            valLabel.FontSize = 10.5;
            valLabel.Width = 26;
            valLabel.TextAlignment = TextAlignment.Right;
            valLabel.VerticalAlignment = VerticalAlignment.Center;

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*,28"), ColumnSpacing = 4 };
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10.5,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);
            Grid.SetColumn(valLabel, 2);
            row.Children.Add(valLabel);
            return row;
        }

        public void ShowContextSubMenuAt(Point canvasRelativePos)
        {
            double maxW = _canvasFrame.Bounds.Width > 40 ? _canvasFrame.Bounds.Width : 540;
            double maxH = _canvasFrame.Bounds.Height > 40 ? _canvasFrame.Bounds.Height : 420;
            double popupW = _contextSubMenuPopup.Width;
            double popupH = 420;

            double x = Math.Clamp(canvasRelativePos.X + 8, 6, Math.Max(6, maxW - popupW - 8));
            double y = Math.Clamp(canvasRelativePos.Y + 8, 6, Math.Max(6, maxH - popupH - 8));

            Avalonia.Controls.Canvas.SetLeft(_contextSubMenuPopup, x);
            Avalonia.Controls.Canvas.SetTop(_contextSubMenuPopup, y);
            _contextSubMenuPopup.IsVisible = true;
        }

        public void SelectCustomColor(string hex)
        {
            string normalized;
            Color color;
            if (string.Equals(hex, "AUTO", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "#0F172A";
                color = Color.Parse(normalized);
                _canvas.ActiveColorHex = "AUTO";
            }
            else if (Color.TryParse(hex?.Trim(), out var parsed))
            {
                normalized = $"#{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}";
                color = parsed;
                _canvas.ActiveColorHex = normalized;
            }
            else
            {
                return;
            }

            ApplyColorToSelectorUi(color, normalized, syncSpectrum: true, syncHexText: true);
            RefreshSelectionVisuals();
        }

        private void ApplyColorFromControls(Color c, bool syncSpectrum = true)
        {
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            _canvas.ActiveColorHex = hex;
            ApplyColorToSelectorUi(c, hex, syncSpectrum, syncHexText: true);
            RefreshSelectionVisuals();
        }

        private void ApplyColorToSelectorUi(Color c, string hex, bool syncSpectrum, bool syncHexText)
        {
            _syncingColorSelector = true;
            try
            {
                var brush = new SolidColorBrush(c);
                _colorBtnDot.Background = brush;
                _colorBtnText.Text = $"Color ({hex})";
                _selectorPreviewSwatch.Background = brush;

                _redSlider.Value = c.R;
                _greenSlider.Value = c.G;
                _blueSlider.Value = c.B;
                _redValLabel.Text = c.R.ToString();
                _greenValLabel.Text = c.G.ToString();
                _blueValLabel.Text = c.B.ToString();

                if (syncSpectrum)
                {
                    var (h, s, v) = ColorToHsv(c);
                    _hueSlider.Value = h;
                    _spectrumPicker.SetHsv(h, s, v);
                }

                if (syncHexText && !string.Equals(_colorHexInput.Text, hex, StringComparison.OrdinalIgnoreCase))
                    _colorHexInput.Text = hex;
            }
            finally
            {
                _syncingColorSelector = false;
            }
        }

        private void SetActiveThickness(double rawValue)
        {
            double val = Math.Round(Math.Clamp(rawValue, 1, 24), 1);
            _syncingThickness = true;
            try
            {
                _canvas.ActiveThickness = val;
                _thicknessSlider.Value = val;
                _popupThicknessSlider.Value = val;
                _thicknessLabel.Text = $"{val:0.#}px";
                _popupThicknessLabel.Text = $"{val:0.#}px";
            }
            finally
            {
                _syncingThickness = false;
            }
        }

        public void LoadDocument(DrawingDocument document)
        {
            _canvas.LoadDocument(document);
            SyncBackgroundCombo();
            RefreshHistoryButtons();
        }

        private void SyncBackgroundCombo()
        {
            _syncingBackground = true;
            _backgroundCombo.SelectedItem = _canvas.Document.Background;
            _syncingBackground = false;
        }

        private void RefreshHistoryButtons()
        {
            _undoBtn.IsEnabled = _canvas.CanUndo;
            _redoBtn.IsEnabled = _canvas.CanRedo;
            _undoBtn.Content = _canvas.UndoCount > 0 ? $"↶ Undo ({_canvas.UndoCount})" : "↶ Undo";
            _redoBtn.Content = _canvas.RedoCount > 0 ? $"↷ Redo ({_canvas.RedoCount})" : "↷ Redo";
        }

        public void ApplyTheme(bool isDarkTheme)
        {
            var palette = isDarkTheme ? ThemePalette.CreateClassicDark() : ThemePalette.CreateClassicLight();
            ApplyThemePalette(palette, isDarkTheme);
        }

        public void ApplyThemePalette(ThemePalette palette, bool isDarkTheme)
        {
            _palette = palette;
            _canvas.IsDarkTheme = isDarkTheme;
            _canvas.ApplyPaletteColors(palette.CanvasBackground, palette.CanvasGridColor);
            Background = ThemeConfigService.ParseBrush(palette.CardBackground, isDarkTheme ? "#2A2D2E" : "#F8F8F8");
            BorderBrush = ThemeConfigService.ParseBrush(palette.BorderColor, isDarkTheme ? "#3C3C3C" : "#D4D4D4");
            _canvasFrame.BorderBrush = ThemeConfigService.ParseBrush(palette.BorderColor, isDarkTheme ? "#3C3C3C" : "#D4D4D4");
            _textPromptBar.Background = ThemeConfigService.ParseBrush(palette.HeaderBackground, isDarkTheme ? "#2D2D2D" : "#E8E8E8");
            _contextSubMenuPopup.Background = ThemeConfigService.ParseBrush(palette.PanelBackground, isDarkTheme ? "#252526" : "#FFFFFF");
            _contextSubMenuPopup.BorderBrush = ThemeConfigService.ParseBrush(palette.BorderColor, isDarkTheme ? "#3C3C3C" : "#D4D4D4");

            var muted = ThemeConfigService.ParseBrush(palette.MutedTextColor, isDarkTheme ? "#9E9E9E" : "#616161");
            _thicknessLabel.Foreground = muted;
            _zoomLabel.Foreground = muted;
            _redValLabel.Foreground = muted;
            _greenValLabel.Foreground = muted;
            _blueValLabel.Foreground = muted;
            _popupThicknessLabel.Foreground = muted;
            RefreshSelectionVisuals();
        }

        public async System.Threading.Tasks.Task ExportImageAsync(bool isSvg)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = isSvg ? "Export Drawing as SVG" : "Export Drawing as PNG",
                DefaultExtension = isSvg ? "svg" : "png",
                SuggestedFileName = isSvg ? "drawing.svg" : "drawing.png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(isSvg ? "SVG Vector Image" : "PNG Image")
                    {
                        Patterns = new[] { isSvg ? "*.svg" : "*.png" }
                    }
                }
            });

            if (file is null) return;
            string? path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;

            if (isSvg)
            {
                string svg = _canvas.Document.ExportToSvg(_canvas.IsDarkTheme);
                await File.WriteAllTextAsync(path, svg);
            }
            else
            {
                _canvas.ExportToPng(path);
            }
        }

        private void CommitTextPrompt()
        {
            if (!string.IsNullOrWhiteSpace(_textPromptBox.Text))
                _canvas.AddTextLabel(_pendingTextPosition, _textPromptBox.Text);
            _textPromptBox.Text = string.Empty;
            _textPromptBar.IsVisible = false;
        }

        private void AddToolButton(Panel parent, DrawingToolKind tool, string label)
        {
            var btn = new Button
            {
                Content = label,
                FontSize = 11,
                Padding = new Thickness(7, 4),
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(5)
            };
            btn.Click += (_, _) =>
            {
                _canvas.ActiveTool = tool;
                if (tool == DrawingToolKind.TextLabel)
                    ToolTip.SetTip(_canvas, "Click anywhere on the canvas to place a Text or $LaTeX$ label");
                else
                    ToolTip.SetTip(_canvas, null);
                RefreshSelectionVisuals();
            };

            if (!_toolButtons.TryGetValue(tool, out var list))
            {
                list = new List<Button>();
                _toolButtons[tool] = list;
            }
            list.Add(btn);
            parent.Children.Add(btn);
        }

        private static Button CreateActionButton(string label, Action onClick)
        {
            var btn = new Button
            {
                Content = label,
                FontSize = 11,
                Padding = new Thickness(7, 4),
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(5)
            };
            btn.Click += (_, _) => onClick();
            return btn;
        }

        private static Border CreateSeparator() => new()
        {
            Width = 1,
            Height = 20,
            Margin = new Thickness(5, 2),
            Background = new SolidColorBrush(Color.FromArgb(80, 148, 163, 184)),
            VerticalAlignment = VerticalAlignment.Center
        };

        private void RefreshSelectionVisuals()
        {
            var activeBg = _palette is not null
                ? ThemeConfigService.ParseBrush(_palette.AccentColor, "#0078D4")
                : new SolidColorBrush(Color.Parse("#0078D4"));
            var activeFg = Brushes.White;

            foreach (var (tool, buttons) in _toolButtons)
            {
                bool active = tool == _canvas.ActiveTool;
                foreach (var btn in buttons)
                {
                    if (active)
                    {
                        btn.Background = activeBg;
                        btn.Foreground = activeFg;
                    }
                    else
                    {
                        btn.ClearValue(BackgroundProperty);
                        btn.ClearValue(Button.ForegroundProperty);
                    }
                }
            }
        }

        private static (double H, double S, double V) ColorToHsv(Color c)
        {
            double r = c.R / 255.0;
            double g = c.G / 255.0;
            double b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double d = max - min;

            double h = 0;
            if (d > 0.0001)
            {
                if (max == r) h = 60.0 * (((g - b) / d) % 6.0);
                else if (max == g) h = 60.0 * (((b - r) / d) + 2.0);
                else h = 60.0 * (((r - g) / d) + 4.0);
                if (h < 0) h += 360.0;
            }
            double s = max <= 0.0001 ? 0.0 : d / max;
            return (h, s, max);
        }

        internal static Color HsvToColor(double h, double s, double v)
        {
            h = ((h % 360.0) + 360.0) % 360.0;
            s = Math.Clamp(s, 0.0, 1.0);
            v = Math.Clamp(v, 0.0, 1.0);

            double c = v * s;
            double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
            double m = v - c;

            double r, g, b;
            if (h < 60) (r, g, b) = (c, x, 0);
            else if (h < 120) (r, g, b) = (x, c, 0);
            else if (h < 180) (r, g, b) = (0, c, x);
            else if (h < 240) (r, g, b) = (0, x, c);
            else if (h < 300) (r, g, b) = (x, 0, c);
            else (r, g, b) = (c, 0, x);

            return Color.FromRgb(
                (byte)Math.Clamp((int)Math.Round((r + m) * 255.0), 0, 255),
                (byte)Math.Clamp((int)Math.Round((g + m) * 255.0), 0, 255),
                (byte)Math.Clamp((int)Math.Round((b + m) * 255.0), 0, 255));
        }
    }

    /// <summary>
    /// 2D Saturation (horizontal) × Value/Brightness (vertical) HSV spectrum control.
    /// </summary>
    internal sealed class ColorSpectrumPickerControl : Control
    {
        private bool _isDragging;

        public double Hue { get; private set; } = 220;
        public double Saturation { get; private set; } = 0.8;
        public double Value { get; private set; } = 0.8;

        public event EventHandler<(double H, double S, double V)>? ColorPicked;

        public ColorSpectrumPickerControl()
        {
            ClipToBounds = true;
        }

        public void SetHsv(double h, double s, double v)
        {
            Hue = ((h % 360.0) + 360.0) % 360.0;
            Saturation = Math.Clamp(s, 0.0, 1.0);
            Value = Math.Clamp(v, 0.0, 1.0);
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _isDragging = true;
                e.Pointer.Capture(this);
                PickFromPoint(e.GetPosition(this));
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_isDragging)
            {
                PickFromPoint(e.GetPosition(this));
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_isDragging)
            {
                _isDragging = false;
                e.Pointer.Capture(null);
                e.Handled = true;
            }
        }

        private void PickFromPoint(Point pt)
        {
            double w = Math.Max(1.0, Bounds.Width);
            double h = Math.Max(1.0, Bounds.Height);
            Saturation = Math.Clamp(pt.X / w, 0.0, 1.0);
            Value = Math.Clamp(1.0 - (pt.Y / h), 0.0, 1.0);
            InvalidateVisual();
            ColorPicked?.Invoke(this, (Hue, Saturation, Value));
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var rect = new Rect(Bounds.Size);
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            var pureHue = DrawingWorkspaceControl.HsvToColor(Hue, 1.0, 1.0);
            var satBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Colors.White, 0),
                    new GradientStop(pureHue, 1)
                }
            };
            var valBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                    new GradientStop(Color.FromArgb(255, 0, 0, 0), 1)
                }
            };

            context.DrawRectangle(satBrush, null, rect, 5, 5);
            context.DrawRectangle(valBrush, new Pen(new SolidColorBrush(Color.Parse("#64748B")), 1.0), rect, 5, 5);

            var selectorPos = new Point(Saturation * rect.Width, (1.0 - Value) * rect.Height);
            context.DrawEllipse(null, new Pen(Brushes.Black, 2.2), selectorPos, 5.5, 5.5);
            context.DrawEllipse(null, new Pen(Brushes.White, 1.4), selectorPos, 5.5, 5.5);
        }
    }
}
