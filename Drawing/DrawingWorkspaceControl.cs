using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using NoteDraw.Drawing.Commands;
using NoteDraw.Drawing.Tools;
using NoteDraw.Services;

namespace NoteDraw.Drawing
{
    /// <summary>
    /// Complete interactive drawing studio containing the top options/navigation bar,
    /// vertical side tool bar with vector icons, white vector canvas with live cursor preview,
    /// right-click drawing sub-menu with 2D HSV/RGB color selector, and inline Text/LaTeX prompt.
    /// </summary>
    public sealed partial class DrawingWorkspaceControl : Border
    {
        private readonly DrawingCanvasControl _canvas;
        private readonly Dictionary<DrawingToolKind, List<Button>> _toolButtons = new();
        private readonly Dictionary<DrawingToolKind, SelectDrawingToolCommand> _toolCommands = new();
        private readonly Border _sideToolBar = new();
        private readonly Button _colorSelectorBtn;
        private readonly Border _colorBtnDot = new();
        private readonly TextBlock _colorBtnText = new();
        private readonly StackPanel _toolbarQuickColorsPanel = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center
        };
        private readonly ComboBox _backgroundCombo;
        private readonly Slider _thicknessSlider;
        private readonly TextBlock _thicknessLabel;
        private readonly TextBlock _zoomLabel;
        private readonly Slider _zoomSlider;
        private readonly TextBox _canvasWidthBox = new();
        private readonly TextBox _canvasHeightBox = new();
        private readonly Border _resizeHandleBar = new();
        private readonly TextBlock _resizeHandleLabel = new();
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
        private readonly Slider _popupZoomSlider = new();
        private readonly TextBlock _popupZoomLabel = new();
        private readonly WrapPanel _popupRememberedColorsWrap = new() { Orientation = Orientation.Horizontal };

        private static readonly JsonSerializerOptions MemoryJsonOptions = new() { WriteIndented = true };
        private static readonly List<string> _rememberedColors = new()
        {
            "#0F172A", "#3B82F6", "#EF4444", "#10B981", "#F59E0B", "#A855F7", "#06B6D4", "#EC4899"
        };

        public static string LastSelectedColorHex { get; private set; } = "#0F172A";
        public static IReadOnlyList<string> RememberedColors => _rememberedColors;
        public static Bitmap? SharedInAppClipboardBitmap { get; set; }

        private Point _pendingTextPosition = new(80, 80);
        private Point _lastContextSubMenuCanvasPoint = new(40, 40);
        private bool _syncingBackground;
        private bool _syncingThickness;
        private bool _syncingZoom;
        private bool _syncingColorSelector;
        private bool _syncingCanvasSize;
        private bool _isPointerPressedInsideCanvasFrame;
        private bool _isDraggingResizeHandle;
        private Point _resizeDragStartScreen;
        private double _resizeStartHeight;
        private ThemePalette? _palette;

        static DrawingWorkspaceControl()
        {
            LoadRememberedColorsFromDisk();
        }

        public DrawingCanvasControl Canvas => _canvas;
        internal Border SideToolBar => _sideToolBar;
        internal IReadOnlyDictionary<DrawingToolKind, SelectDrawingToolCommand> ToolCommands => _toolCommands;
        internal IReadOnlyDictionary<DrawingToolKind, List<Button>> ToolButtons => _toolButtons;
        public bool IsContextSubMenuOpen => _contextSubMenuPopup.IsVisible;
        public bool IsPointerPressedInCanvas => _canvas.IsPointerPressedOnCanvas || _isPointerPressedInsideCanvasFrame;

        public bool Undo() => _canvas.Undo();
        public bool Redo() => _canvas.Redo();

        public event EventHandler? DocumentChanged;
        public event EventHandler<Bitmap>? CanvasBitmapCopied;

        public DrawingWorkspaceControl(DrawingDocument? initialDocument = null, bool compactInlineMode = false, bool isDarkTheme = true)
        {
            _canvas = new DrawingCanvasControl
            {
                IsDarkTheme = isDarkTheme,
                MinWidth = 80,
                MinHeight = 60
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

            // Row 0: Canvas Actions & Options Toolbar (Color Selector + Thickness + Background + Zoom + Size + Export)
            var toolbarWrap = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6)
            };

            foreach (var tool in DrawingToolRegistry.Default.All)
            {
                var toolCmd = new SelectDrawingToolCommand(_canvas, tool, _ => RefreshSelectionVisuals());
                _toolCommands[tool.Kind] = toolCmd;
            }

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
            toolbarWrap.Children.Add(_toolbarQuickColorsPanel);

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

            // Actions: Copy/Paste, Clear, Zoom, Export
            var copyCanvasBtn = CreateActionButton("📋 Copy", () => _ = CopyCanvasToClipboardAsync());
            ToolTip.SetTip(copyCanvasBtn, $"Copy Canvas Image to Clipboard ({PlatformShortcuts.Format("C")})");
            toolbarWrap.Children.Add(copyCanvasBtn);

            var pasteImageBtn = CreateActionButton("📋 Paste", () => _ = PasteImageFromClipboardAsync(_canvas.HoverCanvasPosition, allowFilePickerFallback: true));
            ToolTip.SetTip(pasteImageBtn, $"Paste Image onto Canvas ({PlatformShortcuts.Format("V")})");
            toolbarWrap.Children.Add(pasteImageBtn);

            toolbarWrap.Children.Add(CreateActionButton("Clear", () => _canvas.ClearCanvas()));
            toolbarWrap.Children.Add(CreateActionButton("-", () => _canvas.ZoomByFactor(1.0 / 1.15)));
            _zoomSlider = new Slider
            {
                Minimum = 25,
                Maximum = 400,
                Value = 100,
                Width = 75,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0)
            };
            ToolTip.SetTip(_zoomSlider, "Zoom Canvas (25% – 400%) • Also use Mouse Wheel / + / - while drawing");
            _zoomSlider.PropertyChanged += (_, args) =>
            {
                if (!_syncingZoom && args.Property == Slider.ValueProperty)
                    SetZoomPercent(_zoomSlider.Value);
            };
            toolbarWrap.Children.Add(_zoomSlider);
            _zoomLabel = new TextBlock
            {
                Text = "100%",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Margin = new Thickness(4, 0)
            };
            toolbarWrap.Children.Add(_zoomLabel);
            toolbarWrap.Children.Add(CreateActionButton("+", () => _canvas.ZoomByFactor(1.15)));
            toolbarWrap.Children.Add(CreateActionButton("1:1", () => _canvas.ResetZoomAndPan()));
            toolbarWrap.Children.Add(CreateActionButton("PNG", async () => await ExportImageAsync(isSvg: false)));
            toolbarWrap.Children.Add(CreateActionButton("SVG", async () => await ExportImageAsync(isSvg: true)));

            toolbarWrap.Children.Add(CreateSeparator());
            toolbarWrap.Children.Add(new TextBlock
            {
                Text = "Size:",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Margin = new Thickness(2, 0, 4, 0)
            });
            ConfigureCanvasSizeBox(_canvasWidthBox, "Canvas width in pixels (Enter or click Resize)");
            ConfigureCanvasSizeBox(_canvasHeightBox, "Canvas height in pixels (Enter or click Resize)");
            toolbarWrap.Children.Add(_canvasWidthBox);
            toolbarWrap.Children.Add(new TextBlock
            {
                Text = "×",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Margin = new Thickness(3, 0)
            });
            toolbarWrap.Children.Add(_canvasHeightBox);
            toolbarWrap.Children.Add(CreateActionButton("Resize", ApplyCanvasSizeFromInputs));
            toolbarWrap.Children.Add(CreateActionButton("Fit Bounds", () =>
            {
                _canvas.FitCanvasToUsedBounds(20.0);
                SyncCanvasSizeUi();
            }));

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
            _canvasFrame.HorizontalAlignment = HorizontalAlignment.Left;
            _canvasFrame.ClipToBounds = true;
            _canvasFrame.Child = canvasHostGrid;

            // Track pointer presses originating inside the canvas frame and suppress ancestor scroll gestures.
            _canvasFrame.AddHandler(PointerPressedEvent, (_, pe) =>
            {
                _isPointerPressedInsideCanvasFrame = true;
                pe.PreventGestureRecognition();
            }, RoutingStrategies.Tunnel);
            _canvasFrame.AddHandler(PointerReleasedEvent, (_, _) => _isPointerPressedInsideCanvasFrame = false, RoutingStrategies.Tunnel);
            _canvasFrame.AddHandler(PointerCaptureLostEvent, (_, _) => _isPointerPressedInsideCanvasFrame = false, RoutingStrategies.Tunnel);

            _resizeHandleLabel.Text = "⋮⋮ Drag bottom edge to resize canvas height • or enter W × H / Fit Bounds above ⋮⋮";
            _resizeHandleLabel.FontSize = 10.5;
            _resizeHandleLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _resizeHandleLabel.VerticalAlignment = VerticalAlignment.Center;

            _resizeHandleBar.Height = 18;
            _resizeHandleBar.CornerRadius = new CornerRadius(4);
            _resizeHandleBar.BorderThickness = new Thickness(1);
            _resizeHandleBar.HorizontalAlignment = HorizontalAlignment.Left;
            _resizeHandleBar.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
            _resizeHandleBar.Child = _resizeHandleLabel;
            ToolTip.SetTip(_resizeHandleBar, "Drag up/down to resize the canvas height");

            _resizeHandleBar.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(_resizeHandleBar).Properties.IsLeftButtonPressed)
                    return;
                e.PreventGestureRecognition();
                _isDraggingResizeHandle = true;
                _resizeDragStartScreen = e.GetPosition(rootGrid);
                _resizeStartHeight = _canvas.Document.Height > 0 ? _canvas.Document.Height : 720;
                e.Pointer.Capture(_resizeHandleBar);
                e.Handled = true;
            };
            _resizeHandleBar.PointerMoved += (_, e) =>
            {
                if (!_isDraggingResizeHandle)
                    return;
                e.PreventGestureRecognition();
                var pos = e.GetPosition(rootGrid);
                double nextH = Math.Clamp(Math.Round(_resizeStartHeight + (pos.Y - _resizeDragStartScreen.Y)), 100.0, 4096.0);
                _canvasFrame.Height = nextH;
                _canvasHeightBox.Text = ((int)nextH).ToString(CultureInfo.InvariantCulture);
                e.Handled = true;
            };
            _resizeHandleBar.PointerReleased += (_, e) =>
            {
                if (!_isDraggingResizeHandle)
                    return;
                _isDraggingResizeHandle = false;
                if (e.Pointer.Captured == _resizeHandleBar)
                    e.Pointer.Capture(null);
                var pos = e.GetPosition(rootGrid);
                double nextH = Math.Clamp(Math.Round(_resizeStartHeight + (pos.Y - _resizeDragStartScreen.Y)), 100.0, 4096.0);
                _canvas.ResizeCanvas(_canvas.Document.Width, nextH);
                SyncCanvasSizeUi();
                e.Handled = true;
            };

            var canvasScroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                MaxHeight = compactInlineMode ? 440 : double.PositiveInfinity,
                Content = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Spacing = 4,
                    Children = { _canvasFrame, _resizeHandleBar }
                }
            };
            // Only scroll if the drag/gesture started outside the canvas window itself.
            canvasScroller.AddHandler(ScrollGestureEvent, (_, se) =>
            {
                if (IsPointerPressedInCanvas || _isDraggingResizeHandle)
                    se.Handled = true;
            }, RoutingStrategies.Tunnel);
            canvasScroller.AddHandler(ScrollGestureInertiaStartingEvent, (_, se) =>
            {
                if (IsPointerPressedInCanvas || _isDraggingResizeHandle)
                    se.Handled = true;
            }, RoutingStrategies.Tunnel);
            canvasScroller.AddHandler(PointerWheelChangedEvent, (_, we) =>
            {
                if (we.Source is Visual v && v.FindAncestorOfType<DrawingCanvasControl>() is not null)
                    we.Handled = true;
            }, RoutingStrategies.Tunnel);

            // Side Tool Bar (Row 2, Column 0) + Canvas Viewport (Row 2, Column 1)
            var sideToolsStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 3,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            DrawingToolGroup? previousGroup = null;
            foreach (var tool in DrawingToolRegistry.Default.All)
            {
                if (previousGroup is not null && previousGroup.Value != tool.Group)
                {
                    sideToolsStack.Children.Add(new Border
                    {
                        Height = 1,
                        Margin = new Thickness(4, 3),
                        Background = new SolidColorBrush(Color.FromArgb(80, 148, 163, 184))
                    });
                }
                previousGroup = tool.Group;
                AddToolButton(sideToolsStack, tool, buttonSize: 34, iconSize: 18);
            }

            _sideToolBar.Width = 46;
            _sideToolBar.CornerRadius = new CornerRadius(6);
            _sideToolBar.BorderThickness = new Thickness(1);
            _sideToolBar.Padding = new Thickness(4);
            _sideToolBar.VerticalAlignment = VerticalAlignment.Stretch;
            _sideToolBar.Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = sideToolsStack
            };

            var workspaceBodyGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 6
            };
            Grid.SetColumn(_sideToolBar, 0);
            workspaceBodyGrid.Children.Add(_sideToolBar);
            Grid.SetColumn(canvasScroller, 1);
            workspaceBodyGrid.Children.Add(canvasScroller);

            Grid.SetRow(workspaceBodyGrid, 2);
            rootGrid.Children.Add(workspaceBodyGrid);

            Child = rootGrid;
            PointerPressed += (_, e) => e.Handled = true;

            _canvas.DocumentChanged += (_, e) =>
            {
                SyncBackgroundCombo();
                SyncCanvasSizeUi();
                DocumentChanged?.Invoke(this, e);
            };
            _canvas.ViewportChanged += (_, _) => SyncZoomUi();
            _canvas.TextLabelPlacementRequested += (_, pos) =>
            {
                _pendingTextPosition = pos;
                _textPromptBar.IsVisible = true;
                _textPromptBox.Focus();
            };
            _canvas.ContextSubMenuRequested += (_, screenPos) => ShowContextSubMenuAt(screenPos);
            _canvas.ColorPickedByPipette += (_, pickedHex) => SelectCustomColor(pickedHex);
            _canvas.PasteImageRequested += (_, canvasPos) => _ = PasteImageFromClipboardAsync(canvasPos, allowFilePickerFallback: false);
            _canvas.CopyCanvasRequested += (_, _) => _ = CopyCanvasToClipboardAsync();
            _canvas.PropertyChanged += (_, args) =>
            {
                if (args.Property == DrawingCanvasControl.ActiveToolProperty ||
                    args.Property == DrawingCanvasControl.ActiveColorHexProperty)
                {
                    RefreshSelectionVisuals();
                }
            };
            _canvas.CanvasLeftClicked += (_, _) =>
            {
                if (_contextSubMenuPopup.IsVisible)
                {
                    RememberColor(_canvas.ActiveColorHex);
                    RebuildQuickColorStrips();
                    _contextSubMenuPopup.IsVisible = false;
                }
            };

            _canvas.ActiveColorHex = LastSelectedColorHex;
            if (Color.TryParse(LastSelectedColorHex, out var initialColor))
            {
                ApplyColorToSelectorUi(initialColor, LastSelectedColorHex, syncSpectrum: true, syncHexText: true);
            }
            RebuildQuickColorStrips();
            SyncCanvasSizeUi();
            ApplyTheme(isDarkTheme);
            RefreshSelectionVisuals();
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

        /// <summary>
        /// Sets the drawing canvas zoom percentage (clamped between 25% and 400%) around the active cursor or canvas center.
        /// </summary>
        public void SetZoomPercent(double percent)
        {
            double clampedPct = Math.Clamp(percent, 25.0, 400.0);
            _canvas.SetZoomAroundPoint(clampedPct / 100.0);
            SyncZoomUi();
        }

        private void SyncZoomUi()
        {
            if (_syncingZoom)
                return;
            _syncingZoom = true;
            try
            {
                double pct = Math.Round(_canvas.Zoom * 100.0);
                string label = $"{pct:0}%";
                _zoomLabel.Text = label;
                _popupZoomLabel.Text = label;
                _zoomSlider.Value = Math.Clamp(pct, _zoomSlider.Minimum, _zoomSlider.Maximum);
                _popupZoomSlider.Value = Math.Clamp(pct, _popupZoomSlider.Minimum, _popupZoomSlider.Maximum);
            }
            finally
            {
                _syncingZoom = false;
            }
        }

        public void LoadDocument(DrawingDocument document)
        {
            _canvas.LoadDocument(document);
            SyncBackgroundCombo();
            SyncCanvasSizeUi();
            SyncZoomUi();
        }

        private void ConfigureCanvasSizeBox(TextBox box, string tooltip)
        {
            box.Width = 54;
            box.FontSize = 11;
            box.Padding = new Thickness(5, 2);
            box.Margin = new Thickness(0, 2);
            ToolTip.SetTip(box, tooltip);
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ApplyCanvasSizeFromInputs();
                    e.Handled = true;
                }
            };
        }

        private void ApplyCanvasSizeFromInputs()
        {
            double currentW = _canvas.Document.Width > 0 ? _canvas.Document.Width : 1200;
            double currentH = _canvas.Document.Height > 0 ? _canvas.Document.Height : 720;

            double nextW = double.TryParse(_canvasWidthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedW)
                ? parsedW
                : currentW;
            double nextH = double.TryParse(_canvasHeightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedH)
                ? parsedH
                : currentH;

            _canvas.ResizeCanvas(nextW, nextH);
            SyncCanvasSizeUi();
        }

        private void SyncCanvasSizeUi()
        {
            if (_syncingCanvasSize)
                return;
            _syncingCanvasSize = true;
            try
            {
                double docW = Math.Clamp(_canvas.Document.Width > 0 ? _canvas.Document.Width : 1200, 120.0, 4096.0);
                double docH = Math.Clamp(_canvas.Document.Height > 0 ? _canvas.Document.Height : 720, 100.0, 4096.0);
                _canvasWidthBox.Text = ((int)Math.Round(docW)).ToString(CultureInfo.InvariantCulture);
                _canvasHeightBox.Text = ((int)Math.Round(docH)).ToString(CultureInfo.InvariantCulture);
                _canvasFrame.Width = docW;
                _canvasFrame.Height = docH;
                _resizeHandleBar.Width = docW;
            }
            finally
            {
                _syncingCanvasSize = false;
            }
        }

        private void SyncBackgroundCombo()
        {
            _syncingBackground = true;
            _backgroundCombo.SelectedItem = _canvas.Document.Background;
            _syncingBackground = false;
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
            _sideToolBar.Background = ThemeConfigService.ParseBrush(palette.PanelBackground, isDarkTheme ? "#252526" : "#FFFFFF");
            _sideToolBar.BorderBrush = ThemeConfigService.ParseBrush(palette.BorderColor, isDarkTheme ? "#3C3C3C" : "#D4D4D4");
            _canvasFrame.BorderBrush = ThemeConfigService.ParseBrush(palette.BorderColor, isDarkTheme ? "#3C3C3C" : "#D4D4D4");
            _textPromptBar.Background = ThemeConfigService.ParseBrush(palette.HeaderBackground, isDarkTheme ? "#2D2D2D" : "#E8E8E8");
            _contextSubMenuPopup.Background = ThemeConfigService.ParseBrush(palette.PanelBackground, isDarkTheme ? "#252526" : "#FFFFFF");
            _contextSubMenuPopup.BorderBrush = ThemeConfigService.ParseBrush(palette.BorderColor, isDarkTheme ? "#3C3C3C" : "#D4D4D4");

            var muted = ThemeConfigService.ParseBrush(palette.MutedTextColor, isDarkTheme ? "#9E9E9E" : "#616161");
            _resizeHandleBar.Background = ThemeConfigService.ParseBrush(palette.HeaderBackground, isDarkTheme ? "#2D2D2D" : "#E8E8E8");
            _resizeHandleBar.BorderBrush = ThemeConfigService.ParseBrush(palette.BorderColor, isDarkTheme ? "#3C3C3C" : "#D4D4D4");
            _resizeHandleLabel.Foreground = muted;
            _thicknessLabel.Foreground = muted;
            _zoomLabel.Foreground = muted;
            _popupZoomLabel.Foreground = muted;
            _redValLabel.Foreground = muted;
            _greenValLabel.Foreground = muted;
            _blueValLabel.Foreground = muted;
            _popupThicknessLabel.Foreground = muted;
            RebuildQuickColorStrips();
            RefreshSelectionVisuals();
        }

        public async System.Threading.Tasks.Task<bool> CopyCanvasToClipboardAsync()
        {
            var bmp = _canvas.RenderToBitmap();
            SharedInAppClipboardBitmap = bmp;
            CanvasBitmapCopied?.Invoke(this, bmp);

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                try
                {
                    await clipboard.SetBitmapAsync(bmp);
                }
                catch
                {
                    // In-app SharedInAppClipboardBitmap remains available even if system clipboard is busy
                }
            }

            return true;
        }

        public async System.Threading.Tasks.Task<bool> PasteImageFromClipboardAsync(
            Point? canvasPos = null,
            bool allowFilePickerFallback = false)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var clipboard = topLevel?.Clipboard;

            if (clipboard is not null)
            {
                try
                {
                    var bmp = await clipboard.TryGetBitmapAsync();
                    if (bmp is not null && _canvas.PasteBitmap(bmp, canvasPos) is not null)
                        return true;
                }
                catch
                {
                    // Continue checking clipboard file list or in-app bitmap
                }

                try
                {
                    var files = await clipboard.TryGetFilesAsync();
                    if (files is not null)
                    {
                        foreach (var item in files)
                        {
                            string? path = item.TryGetLocalPath();
                            if (!string.IsNullOrWhiteSpace(path) &&
                                IsSupportedImageExtension(path) &&
                                _canvas.PasteImageFile(path, canvasPos) is not null)
                            {
                                return true;
                            }
                        }
                    }
                }
                catch
                {
                    // Continue checking in-app clipboard bitmap / picker fallback
                }
            }

            if (SharedInAppClipboardBitmap is not null &&
                _canvas.PasteBitmap(SharedInAppClipboardBitmap, canvasPos) is not null)
            {
                return true;
            }

            if (allowFilePickerFallback && topLevel?.StorageProvider is not null)
            {
                var picked = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Image to Paste onto Canvas",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Images")
                        {
                            Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" }
                        }
                    }
                });

                string? pickedPath = picked.FirstOrDefault()?.TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(pickedPath) &&
                    IsSupportedImageExtension(pickedPath) &&
                    _canvas.PasteImageFile(pickedPath, canvasPos) is not null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSupportedImageExtension(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
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

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || IsEventFromTextInput(e.Source as Visual))
                return;

            if (PlatformShortcuts.HasPrimaryModifier(e.KeyModifiers) &&
                !e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
                !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                if (e.Key == Key.V)
                {
                    _ = PasteImageFromClipboardAsync(_canvas.HoverCanvasPosition, allowFilePickerFallback: false);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.C)
                {
                    _ = CopyCanvasToClipboardAsync();
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Escape && _canvas.HasSelectionArea)
            {
                _canvas.ClearSelectionArea();
                e.Handled = true;
                return;
            }

            if (_canvas.TryHandleToolShortcut(e.Key, e.KeyModifiers))
            {
                RefreshSelectionVisuals();
                e.Handled = true;
            }
        }

        private bool IsEventFromTextInput(Visual? source)
        {
            if (source is TextBox || source?.FindAncestorOfType<TextBox>() is not null)
                return true;

            if (_textPromptBox.IsFocused ||
                _canvasWidthBox.IsFocused ||
                _canvasHeightBox.IsFocused ||
                _colorHexInput.IsFocused)
                return true;

            return TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;
        }

        private void AddToolButton(Panel parent, IDrawingTool tool, double buttonSize, double iconSize)
        {
            if (!_toolCommands.TryGetValue(tool.Kind, out var toolCmd))
            {
                toolCmd = new SelectDrawingToolCommand(_canvas, tool, _ => RefreshSelectionVisuals());
                _toolCommands[tool.Kind] = toolCmd;
            }

            var btn = new Button
            {
                Width = buttonSize,
                Height = buttonSize,
                Padding = new Thickness(0),
                Margin = new Thickness(1.5),
                CornerRadius = new CornerRadius(6),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Command = toolCmd,
                Content = new PathIcon
                {
                    Data = StreamGeometry.Parse(tool.IconPathData),
                    Width = iconSize,
                    Height = iconSize
                }
            };
            ToolTip.SetTip(btn, tool.Tooltip);

            if (!_toolButtons.TryGetValue(tool.Kind, out var list))
            {
                list = new List<Button>();
                _toolButtons[tool.Kind] = list;
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

    }
}
