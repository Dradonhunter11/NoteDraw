using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NoteDraw.Drawing.Commands;
using NoteDraw.Drawing.Tools;
using NoteDraw.Rendering.Latex;
using NoteDraw.Services;

namespace NoteDraw.Drawing
{
    /// <summary>
    /// Interactive vector hand-drawing canvas supporting smooth freehand ink,
    /// highlighter, eraser, geometric shapes, canvas LaTeX/text labels, pan/zoom,
    /// modular tools, and command-backed undo/redo.
    /// </summary>
    public sealed partial class DrawingCanvasControl : Control, ICanvasToolContext
    {
        private DrawingDocument _document = new();
        private string? _customCanvasBgHex;
        private string? _customGridColorHex;
        private readonly CanvasCommandHistory _history = new();
        private readonly Dictionary<string, Bitmap> _imageBitmapCache = new(StringComparer.Ordinal);
        private readonly Dictionary<DrawingToolKind, IDrawingTool> _tools = DrawingToolRegistry.CreateToolSet();

        private IDrawingTool? _activeInteractionTool;
        private DrawingStroke? _activeStroke;
        private DrawingDocument? _preStrokeSnapshot;
        private Point _lastPointerScreenPos;
        private Point? _hoverScreenPos;
        private bool _isPanning;
        private Rect? _draftSelectionArea;

        public Rect? SelectionArea { get; private set; }
        public bool HasSelectionArea => SelectionArea is { Width: >= 5.0, Height: >= 5.0 };
        public bool IsPointerPressedOnCanvas { get; private set; }
        public bool HasActiveStroke => _activeStroke is not null;

        public static readonly StyledProperty<bool> IsDarkThemeProperty =
            AvaloniaProperty.Register<DrawingCanvasControl, bool>(nameof(IsDarkTheme), true);

        public static readonly StyledProperty<DrawingToolKind> ActiveToolProperty =
            AvaloniaProperty.Register<DrawingCanvasControl, DrawingToolKind>(nameof(ActiveTool), DrawingToolKind.Pen);

        public static readonly StyledProperty<string> ActiveColorHexProperty =
            AvaloniaProperty.Register<DrawingCanvasControl, string>(nameof(ActiveColorHex), "AUTO");

        public static readonly StyledProperty<double> ActiveThicknessProperty =
            AvaloniaProperty.Register<DrawingCanvasControl, double>(nameof(ActiveThickness), 3.0);

        public static readonly StyledProperty<double> ZoomProperty =
            AvaloniaProperty.Register<DrawingCanvasControl, double>(nameof(Zoom), 1.0);

        static DrawingCanvasControl()
        {
            AffectsRender<DrawingCanvasControl>(IsDarkThemeProperty, ZoomProperty);
            ActiveToolProperty.Changed.AddClassHandler<DrawingCanvasControl>((c, _) => c.CancelActiveToolInteraction());
        }

        public DrawingCanvasControl()
        {
            ClipToBounds = true;
            Focusable = true;

            // Block ancestor ScrollViewer scroll-gesture recognizers whenever interaction starts on the canvas.
            AddHandler(ScrollGestureEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            AddHandler(ScrollGestureInertiaStartingEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            AddHandler(PointerTouchPadGestureMagnifyEvent, OnTouchPadMagnify, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        }

        public bool IsDarkTheme
        {
            get => GetValue(IsDarkThemeProperty);
            set => SetValue(IsDarkThemeProperty, value);
        }

        public DrawingToolKind ActiveTool
        {
            get => GetValue(ActiveToolProperty);
            set => SetValue(ActiveToolProperty, value);
        }

        public string ActiveColorHex
        {
            get => GetValue(ActiveColorHexProperty);
            set => SetValue(ActiveColorHexProperty, value);
        }

        public double ActiveThickness
        {
            get => GetValue(ActiveThicknessProperty);
            set => SetValue(ActiveThicknessProperty, value);
        }

        public double Zoom
        {
            get => GetValue(ZoomProperty);
            set => SetValue(ZoomProperty, Math.Clamp(value, 0.25, 4.0));
        }

        public Point PanOffset { get; private set; }
        public Point? HoverScreenPosition => _hoverScreenPos;
        public Point? HoverCanvasPosition => _hoverScreenPos.HasValue ? ScreenToCanvas(_hoverScreenPos.Value) : null;

        public bool CanUndo => _history.CanUndo;
        public bool CanRedo => _history.CanRedo;
        public int UndoCount => _history.UndoCount;
        public int RedoCount => _history.RedoCount;

        public DrawingDocument Document => _document;

        DrawingStroke? ICanvasToolContext.ActiveStroke
        {
            get => _activeStroke;
            set => _activeStroke = value;
        }

        DrawingDocument? ICanvasToolContext.PreStrokeSnapshot
        {
            get => _preStrokeSnapshot;
            set => _preStrokeSnapshot = value;
        }

        Rect? ICanvasToolContext.DraftSelectionArea
        {
            get => _draftSelectionArea;
            set => _draftSelectionArea = value;
        }

        bool ICanvasToolContext.IsPointerPressedOnCanvas
        {
            get => IsPointerPressedOnCanvas;
            set => IsPointerPressedOnCanvas = value;
        }

        Point ICanvasToolContext.LastPointerScreenPos
        {
            get => _lastPointerScreenPos;
            set => _lastPointerScreenPos = value;
        }

        Control ICanvasToolContext.CaptureTarget => this;

        public event EventHandler? DocumentChanged;
        public event EventHandler? ViewportChanged;
        public event EventHandler? HistoryChanged;
        public event EventHandler<Point>? TextLabelPlacementRequested;
        public event EventHandler<Point>? ContextSubMenuRequested;
        public event EventHandler? CanvasLeftClicked;
        public event EventHandler<string>? ColorPickedByPipette;
        public event EventHandler<Point?>? PasteImageRequested;
        public event EventHandler? CopyCanvasRequested;
        public event EventHandler? SelectionChanged;

        public void SetSelectionArea(Rect? area)
        {
            Rect? normalized = area is { Width: >= 5.0, Height: >= 5.0 } r ? r : null;
            if (SelectionArea == normalized)
                return;
            SelectionArea = normalized;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearSelectionArea() => SetSelectionArea(null);

        public void SetHoverPositionForPreview(Point? screenPos)
        {
            _hoverScreenPos = screenPos;
            InvalidateVisual();
        }

        public void AddStroke(DrawingStroke stroke)
        {
            if (stroke is null) return;
            var beforeDoc = _document.Clone();
            _document.Strokes.Add(stroke.Clone());
            RecordMutationCommand(new AddStrokeCommand(stroke.Tool, beforeDoc, _document, SelectionArea));
        }

        public (List<DrawingDocument> Undo, List<DrawingDocument> Redo) GetHistoryState()
            => _history.ExportSnapshots();

        public void RestoreHistoryState(IReadOnlyList<DrawingDocument> undo, IReadOnlyList<DrawingDocument> redo)
        {
            _history.RestoreSnapshots(undo, redo, _document);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public void LoadDocument(DrawingDocument document)
        {
            CancelActiveToolInteraction();
            _document = document?.Clone() ?? new DrawingDocument();
            _history.Clear();
            _activeStroke = null;
            _preStrokeSnapshot = null;
            _draftSelectionArea = null;
            SelectionArea = null;
            InvalidateVisual();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetBackground(CanvasBackgroundKind background)
        {
            if (_document.Background == background) return;
            var beforeDoc = _document.Clone();
            _document.Background = background;
            RecordMutationCommand(new ChangeBackgroundCommand(background, beforeDoc, _document, SelectionArea));
        }

        public void AddTextLabel(Point canvasPoint, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (SelectionArea is { } activeSel && !activeSel.Contains(canvasPoint))
                return;

            var beforeDoc = _document.Clone();
            string trimmed = text.Trim();
            bool isLatex = (trimmed.StartsWith('$') && trimmed.EndsWith('$') && trimmed.Length > 2) ||
                           trimmed.Contains('\\');

            string cleanLatex = trimmed;
            if (cleanLatex.StartsWith("$$", StringComparison.Ordinal) && cleanLatex.EndsWith("$$", StringComparison.Ordinal) && cleanLatex.Length > 4)
                cleanLatex = cleanLatex[2..^2].Trim();
            else if (cleanLatex.StartsWith('$') && cleanLatex.EndsWith('$') && cleanLatex.Length > 2)
                cleanLatex = cleanLatex[1..^1].Trim();

            _document.Strokes.Add(new DrawingStroke
            {
                Tool = DrawingToolKind.TextLabel,
                ColorHex = ActiveColorHex,
                Thickness = ActiveThickness,
                Opacity = 1.0,
                TextContent = isLatex ? cleanLatex : trimmed,
                IsLatex = isLatex,
                ClipRect = SelectionArea is { } sel ? new DrawingRect(sel.X, sel.Y, sel.Width, sel.Height) : null,
                Points = new List<DrawingPoint> { new(canvasPoint.X, canvasPoint.Y) }
            });

            RecordMutationCommand(new AddTextLabelCommand(beforeDoc, _document, SelectionArea));
        }

        public bool Undo() => _history.Undo(this);

        public bool Redo() => _history.Redo(this);

        internal void ApplyCommandState(DrawingDocument restoredDoc, Rect? restoredSelection)
        {
            CancelActiveToolInteraction();
            _document = restoredDoc.Clone();
            if (HasSelectionArea && restoredSelection is not null)
                SelectionArea = restoredSelection;
            _activeStroke = null;
            _preStrokeSnapshot = null;
            InvalidateVisual();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        private void CancelActiveToolInteraction()
        {
            if (_activeInteractionTool is not null)
            {
                _activeInteractionTool.Cancel(this);
                _activeInteractionTool = null;
            }
        }

        private void RecordMutationCommand(ICanvasCommand command)
        {
            _history.RecordExecuted(command);
            InvalidateVisual();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        public static bool TryMapKeyToTool(Key key, KeyModifiers modifiers, out DrawingToolKind tool)
            => DrawingToolRegistry.Default.TryMapKey(key, modifiers, out tool);

        public bool TryHandleToolShortcut(Key key, KeyModifiers modifiers = KeyModifiers.None)
        {
            if (!TryMapKeyToTool(key, modifiers, out var nextTool))
                return false;

            ActiveTool = nextTool;
            InvalidateVisual();
            return true;
        }

        public string SampleColorHexAtCanvasPoint(Point canvasPos)
        {
            var hit = HitTestStroke(canvasPos, tolerance: 10.0 / Math.Max(0.25, Zoom), includeImages: false);
            Color sampled = hit is not null
                ? (hit.Tool == DrawingToolKind.Eraser ? GetEffectiveCanvasBackgroundColor() : ResolveStrokeColor(hit.ColorHex))
                : GetEffectiveCanvasBackgroundColor();
            return $"#{sampled.R:X2}{sampled.G:X2}{sampled.B:X2}";
        }

        public string PickColorAtCanvasPoint(Point canvasPos)
        {
            string hex = SampleColorHexAtCanvasPoint(canvasPos);
            ActiveColorHex = hex;
            ColorPickedByPipette?.Invoke(this, hex);
            InvalidateVisual();
            return hex;
        }

        public DrawingStroke? PasteBitmap(Bitmap bitmap, Point? canvasPoint = null)
        {
            if (bitmap is null)
                return null;

            using var ms = new MemoryStream();
            bitmap.Save(ms);
            string base64 = Convert.ToBase64String(ms.ToArray());
            return PasteImageBase64(base64, bitmap.PixelSize.Width, bitmap.PixelSize.Height, canvasPoint);
        }

        public DrawingStroke? PasteImageFile(string filePath, Point? canvasPoint = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                using var bmp = new Bitmap(filePath);
                return PasteBitmap(bmp, canvasPoint);
            }
            catch
            {
                return null;
            }
        }

        public DrawingStroke? PasteImageBase64(string base64Png, double naturalWidth, double naturalHeight, Point? canvasPoint = null)
        {
            if (string.IsNullOrWhiteSpace(base64Png))
                return null;

            double rawW = naturalWidth > 0 ? naturalWidth : 240.0;
            double rawH = naturalHeight > 0 ? naturalHeight : 160.0;
            double docW = _document.Width > 0 ? _document.Width : 1200.0;
            double docH = _document.Height > 0 ? _document.Height : 720.0;
            double maxW = Math.Max(200.0, docW * 0.65);
            double maxH = Math.Max(160.0, docH * 0.65);
            double scale = Math.Min(1.0, Math.Min(maxW / rawW, maxH / rawH));
            double w = Math.Max(24.0, Math.Round(rawW * scale));
            double h = Math.Max(24.0, Math.Round(rawH * scale));

            Point placement = canvasPoint ?? HoverCanvasPosition ?? new Point(
                Math.Max(20.0, (docW - w) / 2.0),
                Math.Max(20.0, (docH - h) / 2.0));
            double x = Math.Max(10.0, Math.Round(placement.X));
            double y = Math.Max(10.0, Math.Round(placement.Y));

            var beforeDoc = _document.Clone();
            var stroke = new DrawingStroke
            {
                Tool = DrawingToolKind.Image,
                ColorHex = "#FFFFFF",
                Thickness = 1.0,
                Opacity = 1.0,
                ImageBase64 = base64Png,
                Points = new List<DrawingPoint>
                {
                    new(x, y),
                    new(x + w, y + h)
                }
            };
            _document.Strokes.Add(stroke);
            _ = GetOrDecodeImageBitmap(base64Png);
            RecordMutationCommand(new PasteImageCommand(beforeDoc, _document, SelectionArea));
            return stroke;
        }

        public RenderTargetBitmap RenderToBitmap()
        {
            var used = _document.GetUsedBounds(padding: 16.0);
            int targetW = Math.Clamp((int)Math.Ceiling(used.Width), 64, 2048);
            int targetH = Math.Clamp((int)Math.Ceiling(used.Height), 48, 2048);

            var renderCanvas = new DrawingCanvasControl
            {
                IsDarkTheme = false,
                Width = targetW,
                Height = targetH
            };
            renderCanvas.LoadDocument(_document);
            renderCanvas.SetPanOffset(new Point(-used.X, -used.Y));
            renderCanvas.ApplyPaletteColors("#FFFFFF", "#E5E5E5");
            renderCanvas.Measure(new Size(targetW, targetH));
            renderCanvas.Arrange(new Rect(0, 0, targetW, targetH));

            var bitmap = new RenderTargetBitmap(new PixelSize(targetW, targetH), new Vector(96, 96));
            bitmap.Render(renderCanvas);
            return bitmap;
        }

        private Bitmap? GetOrDecodeImageBitmap(string? base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
                return null;
            if (_imageBitmapCache.TryGetValue(base64, out var cached))
                return cached;

            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                using var ms = new MemoryStream(bytes);
                var bmp = new Bitmap(ms);
                if (_imageBitmapCache.Count > 64)
                    _imageBitmapCache.Clear();
                _imageBitmapCache[base64] = bmp;
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (PlatformShortcuts.HasPrimaryModifier(e.KeyModifiers))
            {
                if (e.Key == Key.Z && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    if (Undo()) e.Handled = true;
                    return;
                }
                if (e.Key == Key.Y || (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
                {
                    if (Redo()) e.Handled = true;
                    return;
                }
                if (e.Key == Key.V && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    PasteImageRequested?.Invoke(this, HoverCanvasPosition);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.C && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    CopyCanvasRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                }
                if (e.Key is Key.D0 or Key.NumPad0)
                {
                    ResetZoomAndPan();
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Escape && HasSelectionArea)
            {
                ClearSelectionArea();
                e.Handled = true;
                return;
            }

            if (e.Key is Key.Add or Key.OemPlus)
            {
                ZoomByFactor(1.15);
                e.Handled = true;
                return;
            }
            else if (e.Key is Key.Subtract or Key.OemMinus)
            {
                ZoomByFactor(1.0 / 1.15);
                e.Handled = true;
                return;
            }

            if (TryHandleToolShortcut(e.Key, e.KeyModifiers))
                e.Handled = true;
        }

        public void ClearCanvas()
        {
            if (_document.Strokes.Count == 0) return;
            var beforeDoc = _document.Clone();
            _document.Strokes.Clear();
            RecordMutationCommand(new ClearCanvasCommand(beforeDoc, _document, SelectionArea));
        }

        public void SetZoom(double newZoom)
        {
            Zoom = Math.Clamp(newZoom, 0.25, 4.0);
            InvalidateVisual();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Zooms the canvas while keeping <paramref name="anchorScreenPos"/> (or the current cursor/center)
        /// pinned to the same logical canvas coordinate so zooming while drawing stays smooth.
        /// </summary>
        public void SetZoomAroundPoint(double newZoom, Point? anchorScreenPos = null)
        {
            double clamped = Math.Clamp(newZoom, 0.25, 4.0);
            if (Math.Abs(clamped - Zoom) < 0.0001)
                return;

            Point anchor = anchorScreenPos
                ?? _hoverScreenPos
                ?? new Point(Bounds.Width > 0 ? Bounds.Width / 2.0 : 300.0, Bounds.Height > 0 ? Bounds.Height / 2.0 : 200.0);

            var beforeCanvas = ScreenToCanvas(anchor);
            Zoom = clamped;
            PanOffset = new Point(
                anchor.X - beforeCanvas.X * Zoom,
                anchor.Y - beforeCanvas.Y * Zoom);

            InvalidateVisual();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ZoomByFactor(double factor, Point? anchorScreenPos = null)
        {
            if (factor <= 0)
                return;
            SetZoomAroundPoint(Zoom * factor, anchorScreenPos);
        }

        private Point GetCanvasLocalPointerPosition(PointerEventArgs e)
        {
            var pos = e.GetPosition(this);
            if (pos == default && TopLevel.GetTopLevel(this) is null)
                return e.GetPosition(null);
            return pos;
        }

        private void OnTouchPadMagnify(object? sender, PointerDeltaEventArgs e)
        {
            if (Math.Abs(e.Delta.Y) > 0.0001 || Math.Abs(e.Delta.X) > 0.0001)
            {
                double delta = Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X) ? e.Delta.Y : e.Delta.X;
                double factor = Math.Clamp(1.0 + delta * 0.5, 0.5, 2.0);
                ZoomByFactor(factor, GetCanvasLocalPointerPosition(e));
            }
            e.Handled = true;
        }

        public void ResetZoomAndPan()
        {
            Zoom = 1.0;
            PanOffset = default;
            InvalidateVisual();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetPanOffset(Point panOffset)
        {
            PanOffset = panOffset;
            InvalidateVisual();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ResizeCanvas(double width, double height)
        {
            double clampedW = Math.Clamp(Math.Round(width), 120.0, 4096.0);
            double clampedH = Math.Clamp(Math.Round(height), 100.0, 4096.0);
            if (Math.Abs(_document.Width - clampedW) < 0.5 && Math.Abs(_document.Height - clampedH) < 0.5)
                return;

            var beforeDoc = _document.Clone();
            _document.Width = clampedW;
            _document.Height = clampedH;
            RecordMutationCommand(new ResizeCanvasCommand("Resize Canvas", beforeDoc, _document, SelectionArea, SelectionArea));
        }

        public void FitCanvasToUsedBounds(double padding = 20.0)
        {
            var used = _document.GetUsedBounds(padding);
            double targetW = Math.Clamp(Math.Ceiling(used.Width), 120.0, 4096.0);
            double targetH = Math.Clamp(Math.Ceiling(used.Height), 100.0, 4096.0);

            var beforeDoc = _document.Clone();
            var beforeSelection = SelectionArea;
            if (Math.Abs(used.X) > 0.5 || Math.Abs(used.Y) > 0.5)
            {
                foreach (var stroke in _document.Strokes)
                {
                    foreach (var pt in stroke.Points)
                    {
                        pt.X -= used.X;
                        pt.Y -= used.Y;
                    }
                    if (stroke.ClipRect is not null)
                    {
                        stroke.ClipRect.X -= used.X;
                        stroke.ClipRect.Y -= used.Y;
                    }
                }
                if (SelectionArea is { } curSel)
                    SelectionArea = new Rect(curSel.X - used.X, curSel.Y - used.Y, curSel.Width, curSel.Height);
            }

            _document.Width = targetW;
            _document.Height = targetH;
            PanOffset = default;
            _history.RecordExecuted(new ResizeCanvasCommand(
                "Fit Canvas to Bounds",
                beforeDoc,
                _document.Clone(),
                beforeSelection,
                SelectionArea));
            InvalidateVisual();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            ViewportChanged?.Invoke(this, EventArgs.Empty);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ExportToPng(string filePath)
        {
            int width = Math.Max(800, (int)Math.Ceiling(Bounds.Width));
            int height = Math.Max(450, (int)Math.Ceiling(Bounds.Height));
            var pixelSize = new PixelSize(width, height);
            using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
            bitmap.Render(this);
            bitmap.Save(filePath);
        }

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            base.OnPointerEntered(e);
            _hoverScreenPos = GetCanvasLocalPointerPosition(e);
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hoverScreenPos = null;
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            e.PreventGestureRecognition();

            var pointInfo = e.GetCurrentPoint(this);
            var screenPos = GetCanvasLocalPointerPosition(e);
            _lastPointerScreenPos = screenPos;
            _hoverScreenPos = screenPos;

            // Defensively commit any unclosed stroke before starting a new interaction.
            if (_activeStroke is not null)
                CommitActiveStroke();

            if (pointInfo.Properties.IsRightButtonPressed)
            {
                ContextSubMenuRequested?.Invoke(this, screenPos);
                e.Handled = true;
                return;
            }

            if (pointInfo.Properties.IsMiddleButtonPressed)
            {
                IsPointerPressedOnCanvas = true;
                _isPanning = true;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            bool isPenOrTouchAction = e.Pointer.Type is PointerType.Pen or PointerType.Touch;
            bool isEraserTip = pointInfo.Properties.IsEraser;
            bool isPrimaryContact = pointInfo.Properties.IsLeftButtonPressed ||
                                    isEraserTip ||
                                    (isPenOrTouchAction && !pointInfo.Properties.IsRightButtonPressed && !pointInfo.Properties.IsMiddleButtonPressed);
            if (!isPrimaryContact)
                return;

            IsPointerPressedOnCanvas = true;
            CanvasLeftClicked?.Invoke(this, EventArgs.Empty);

            var canvasPos = ScreenToCanvas(screenPos);
            double pressure = pointInfo.Properties.Pressure > 0 ? pointInfo.Properties.Pressure : 0.5;
            DrawingToolKind effectiveTool = isEraserTip ? DrawingToolKind.Eraser : ActiveTool;

            _activeInteractionTool = _tools.TryGetValue(effectiveTool, out var tool) ? tool : _tools[DrawingToolKind.Pen];
            var input = new CanvasPointerInput(screenPos, canvasPos, pressure, e.ClickCount, e.Pointer, e);
            _activeInteractionTool.OnPointerPressed(this, input);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var screenPos = GetCanvasLocalPointerPosition(e);
            _hoverScreenPos = screenPos;

            if (IsPointerPressedOnCanvas || _activeStroke is not null || _isPanning ||
                (_activeInteractionTool?.IsActiveInteraction ?? false))
            {
                e.PreventGestureRecognition();
            }

            if (_isPanning)
            {
                var delta = screenPos - _lastPointerScreenPos;
                PanOffset = new Point(PanOffset.X + delta.X, PanOffset.Y + delta.Y);
                _lastPointerScreenPos = screenPos;
                InvalidateVisual();
                ViewportChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            if (_activeInteractionTool is not null && (_activeInteractionTool.IsActiveInteraction || _activeStroke is not null))
            {
                var ptInfo = e.GetCurrentPoint(this);
                double pressure = ptInfo.Properties.Pressure > 0 ? ptInfo.Properties.Pressure : 0.5;
                var canvasPos = ScreenToCanvas(screenPos);
                var input = new CanvasPointerInput(screenPos, canvasPos, pressure, 1, e.Pointer, e);
                _activeInteractionTool.OnPointerMoved(this, input);
                if (e.Handled)
                    return;
            }

            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            IsPointerPressedOnCanvas = false;

            if (_isPanning)
            {
                _isPanning = false;
                e.Pointer.Capture(null);
                e.Handled = true;
                return;
            }

            if (_activeInteractionTool is not null)
            {
                var screenPos = GetCanvasLocalPointerPosition(e);
                var canvasPos = ScreenToCanvas(screenPos);
                var input = new CanvasPointerInput(screenPos, canvasPos, 0.5, 1, e.Pointer, e);
                var tool = _activeInteractionTool;
                _activeInteractionTool = null;
                tool.OnPointerReleased(this, input);
                if (e.Handled)
                    return;
            }

            if (CommitActiveStroke())
            {
                e.Pointer.Capture(null);
                e.Handled = true;
            }
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            IsPointerPressedOnCanvas = false;

            if (_isPanning)
                _isPanning = false;

            if (_activeInteractionTool is not null)
            {
                var tool = _activeInteractionTool;
                _activeInteractionTool = null;
                tool.OnPointerCaptureLost(this);
            }

            // Never discard/undo an active stylus or pointer stroke if capture is interrupted.
            CommitActiveStroke();
        }

        public bool CommitActiveStroke()
        {
            if (_activeStroke is null)
                return false;

            var committedStroke = _activeStroke;
            var beforeSnapshot = _preStrokeSnapshot ?? _document.Clone();
            _document.Strokes.Add(committedStroke);
            _activeStroke = null;
            _preStrokeSnapshot = null;

            RecordMutationCommand(new AddStrokeCommand(
                committedStroke.Tool,
                beforeSnapshot,
                _document.Clone(),
                SelectionArea));
            return true;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            if (Math.Abs(e.Delta.Y) > 0.0001)
            {
                double factor = e.Delta.Y > 0 ? 1.12 : 0.89;
                ZoomByFactor(factor, GetCanvasLocalPointerPosition(e));
            }
            e.Handled = true;
        }


        private Point ScreenToCanvas(Point screen)
            => new((screen.X - PanOffset.X) / Zoom, (screen.Y - PanOffset.Y) / Zoom);

        Point ICanvasToolContext.ScreenToCanvas(Point screenPos) => ScreenToCanvas(screenPos);

        DrawingStroke? ICanvasToolContext.HitTestStroke(Point pt, double tolerance, bool includeImages)
            => HitTestStroke(pt, tolerance, includeImages);

        bool ICanvasToolContext.StrokeIntersectsRect(DrawingStroke stroke, Rect rect)
            => StrokeIntersectsRect(stroke, rect);

        void ICanvasToolContext.TranslateStroke(DrawingStroke stroke, double dx, double dy)
            => TranslateStroke(stroke, dx, dy);

        void ICanvasToolContext.RequestTextLabelPlacement(Point canvasPos)
            => TextLabelPlacementRequested?.Invoke(this, canvasPos);

        void ICanvasToolContext.RecordUndoCommand(ICanvasCommand command)
        {
            _history.RecordExecuted(command);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        void ICanvasToolContext.NotifyDocumentChanged()
            => DocumentChanged?.Invoke(this, EventArgs.Empty);

        void ICanvasToolContext.NotifySelectionChanged()
            => SelectionChanged?.Invoke(this, EventArgs.Empty);

        void ICanvasToolContext.NotifyViewportChanged()
            => ViewportChanged?.Invoke(this, EventArgs.Empty);

    }
}
