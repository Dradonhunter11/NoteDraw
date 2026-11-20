using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace NoteDraw.Drawing
{
    /// <summary>
    /// Complete user-friendly drawing studio control containing the drawing toolbar,
    /// color palette, stroke thickness slider, background selector, inline text/LaTeX prompt,
    /// zoom/export actions, and embedded <see cref="DrawingCanvasControl"/>.
    /// </summary>
    public sealed class DrawingWorkspaceControl : Border
    {
        private readonly DrawingCanvasControl _canvas;
        private readonly Dictionary<DrawingToolKind, Button> _toolButtons = new();
        private readonly Dictionary<string, Button> _colorButtons = new(StringComparer.OrdinalIgnoreCase);
        private readonly Button _undoBtn;
        private readonly Button _redoBtn;
        private readonly ComboBox _backgroundCombo;
        private readonly Slider _thicknessSlider;
        private readonly TextBlock _thicknessLabel;
        private readonly TextBlock _zoomLabel;
        private readonly Border _textPromptBar = new();
        private readonly TextBox _textPromptBox;
        private Point _pendingTextPosition = new(80, 80);
        private bool _syncingBackground;

        public DrawingCanvasControl Canvas => _canvas;

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

            // Row 0: Primary Drawing Toolbar (Undo / Redo placed prominently at the start)
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

            // Color Palette Swatches
            var palette = new (string Hex, string Tooltip)[]
            {
                ("AUTO", "Auto Ink"),
                ("#3B82F6", "Blue"),
                ("#EF4444", "Red"),
                ("#10B981", "Green"),
                ("#F59E0B", "Orange"),
                ("#A855F7", "Purple"),
                ("#06B6D4", "Teal"),
                ("#FACC15", "Yellow")
            };
            foreach (var (hex, tooltip) in palette)
                AddColorSwatch(toolbarWrap, hex, tooltip);

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
                if (args.Property == Slider.ValueProperty)
                {
                    double val = Math.Round(_thicknessSlider.Value, 1);
                    _canvas.ActiveThickness = val;
                    _thicknessLabel.Text = $"{val:0.#}px";
                }
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

            // Row 1: Inline Text / LaTeX Annotation Prompt Bar (shown when Text/$LaTeX$ tool clicks canvas)
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

            // Row 2: Drawing Canvas surface
            var canvasFrame = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                Child = _canvas
            };
            if (compactInlineMode)
            {
                canvasFrame.Height = 330;
            }
            Grid.SetRow(canvasFrame, 2);
            rootGrid.Children.Add(canvasFrame);

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

            ApplyTheme(isDarkTheme);
            RefreshSelectionVisuals();
            RefreshHistoryButtons();
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
            _canvas.IsDarkTheme = isDarkTheme;
            Background = new SolidColorBrush(Color.Parse(isDarkTheme ? "#161B26" : "#F8FAFC"));
            BorderBrush = new SolidColorBrush(Color.Parse(isDarkTheme ? "#2B3548" : "#CBD5E1"));
            _textPromptBar.Background = new SolidColorBrush(Color.Parse(isDarkTheme ? "#1E293B" : "#E2E8F0"));
            _thicknessLabel.Foreground = new SolidColorBrush(Color.Parse(isDarkTheme ? "#CBD5E1" : "#334155"));
            _zoomLabel.Foreground = new SolidColorBrush(Color.Parse(isDarkTheme ? "#CBD5E1" : "#334155"));
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
            _toolButtons[tool] = btn;
            parent.Children.Add(btn);
        }

        private void AddColorSwatch(Panel parent, string hex, string tooltip)
        {
            IBrush swatchBrush = string.Equals(hex, "AUTO", StringComparison.OrdinalIgnoreCase)
                ? new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.Black, 0),
                        new GradientStop(Colors.White, 1)
                    }
                }
                : new SolidColorBrush(Color.Parse(hex));

            var btn = new Button
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(2),
                Padding = new Thickness(2),
                CornerRadius = new CornerRadius(11),
                Content = new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(7),
                    Background = swatchBrush
                }
            };
            ToolTip.SetTip(btn, tooltip);
            btn.Click += (_, _) =>
            {
                _canvas.ActiveColorHex = hex;
                RefreshSelectionVisuals();
            };
            _colorButtons[hex] = btn;
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
            var activeBg = new SolidColorBrush(Color.Parse("#2563EB"));
            var activeFg = Brushes.White;

            foreach (var (tool, btn) in _toolButtons)
            {
                if (tool == _canvas.ActiveTool)
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

            foreach (var (hex, btn) in _colorButtons)
            {
                if (string.Equals(hex, _canvas.ActiveColorHex, StringComparison.OrdinalIgnoreCase))
                {
                    btn.BorderBrush = activeBg;
                    btn.BorderThickness = new Thickness(2);
                }
                else
                {
                    btn.BorderThickness = new Thickness(1);
                    btn.ClearValue(BorderBrushProperty);
                }
            }
        }
    }
}
