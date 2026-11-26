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
    public sealed class DrawingCanvasControl : Control, ICanvasToolContext
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

        public void ApplyPaletteColors(string? canvasBackgroundHex, string? gridColorHex)
        {
            _customCanvasBgHex = canvasBackgroundHex;
            _customGridColorHex = gridColorHex;
            InvalidateVisual();
        }

        private Color GetEffectiveCanvasBackgroundColor()
            => Color.TryParse(_customCanvasBgHex, out var customBg) ? customBg : Colors.White;

        private bool IsCanvasBackgroundLight()
        {
            var c = GetEffectiveCanvasBackgroundColor();
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 128.0;
        }

        private bool HasEraserStrokes()
        {
            if (_activeStroke?.Tool == DrawingToolKind.Eraser)
                return true;
            foreach (var s in _document.Strokes)
            {
                if (s.Tool == DrawingToolKind.Eraser)
                    return true;
            }
            return false;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var bounds = new Rect(Bounds.Size);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            var bgColor = GetEffectiveCanvasBackgroundColor();
            context.FillRectangle(new SolidColorBrush(bgColor), bounds);

            DrawBackgroundPattern(context, bounds, patternOpacity: 1.0);

            var transform = Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(PanOffset.X, PanOffset.Y);
            using (context.PushTransform(transform))
            {
                foreach (var stroke in _document.Strokes)
                    DrawStroke(context, stroke);

                if (_activeStroke is not null)
                    DrawStroke(context, _activeStroke);

                DrawSelectionOverlay(context);
            }

            if (_document.Background != CanvasBackgroundKind.Blank && HasEraserStrokes())
                DrawBackgroundPattern(context, bounds, patternOpacity: 0.45);

            if (_hoverScreenPos.HasValue)
                DrawMousePreview(context, bounds, _hoverScreenPos.Value);
        }

        private void DrawSelectionOverlay(DrawingContext context)
        {
            Rect? overlayRect = _draftSelectionArea ?? SelectionArea;
            if (overlayRect is not { } sel)
                return;

            double invScale = 1.0 / Math.Max(0.25, Zoom);
            if (_draftSelectionArea is null && sel.Width >= 5.0 && sel.Height >= 5.0)
            {
                double docW = Math.Max(_document.Width > 0 ? _document.Width : 1200, sel.Right + 200);
                double docH = Math.Max(_document.Height > 0 ? _document.Height : 720, sel.Bottom + 200);
                var outsideTint = new SolidColorBrush(Color.FromArgb(34, 15, 23, 42));
                if (sel.Top > 0)
                    context.FillRectangle(outsideTint, new Rect(0, 0, docW, sel.Top));
                if (sel.Bottom < docH)
                    context.FillRectangle(outsideTint, new Rect(0, sel.Bottom, docW, docH - sel.Bottom));
                if (sel.Left > 0)
                    context.FillRectangle(outsideTint, new Rect(0, sel.Top, sel.Left, sel.Height));
                if (sel.Right < docW)
                    context.FillRectangle(outsideTint, new Rect(sel.Right, sel.Top, docW - sel.Right, sel.Height));
            }

            var haloPen = new Pen(Brushes.White, 2.0 * invScale);
            var dashPen = new Pen(new SolidColorBrush(Color.Parse("#0078D4")), 1.6 * invScale, new DashStyle(new[] { 5.0, 4.0 }, 0));
            context.DrawRectangle(null, haloPen, sel);
            context.DrawRectangle(null, dashPen, sel);

            if (_draftSelectionArea is null && ActiveTool == DrawingToolKind.ResizeSelection)
            {
                double hs = 8.0 * invScale;
                double half = hs / 2.0;
                var handleFill = Brushes.White;
                var handlePen = new Pen(new SolidColorBrush(Color.Parse("#0078D4")), 1.5 * invScale);
                foreach (var pt in ResizeSelectionTool.GetSelectionHandleCenters(sel))
                {
                    context.DrawRectangle(handleFill, handlePen, new Rect(pt.X - half, pt.Y - half, hs, hs));
                }
            }
        }

        private void DrawMousePreview(DrawingContext context, Rect bounds, Point sp)
        {
            if (!bounds.Contains(sp))
                return;

            Point cp = ScreenToCanvas(sp);
            string readoutText;
            if (ActiveTool == DrawingToolKind.Pipette)
            {
                string sampledHex = SampleColorHexAtCanvasPoint(cp);
                var sampledColor = Color.TryParse(sampledHex, out var sc) ? sc : Colors.White;
                var outerRing = new Pen(new SolidColorBrush(Color.Parse("#334155")), 1.6);
                var innerRing = new Pen(Brushes.White, 1.2);
                context.DrawEllipse(new SolidColorBrush(sampledColor), outerRing, sp, 8.0, 8.0);
                context.DrawEllipse(null, innerRing, sp, 6.4, 6.4);
                var crossPen = new Pen(new SolidColorBrush(Color.Parse("#0F172A")), 1.0);
                context.DrawLine(crossPen, new Point(sp.X - 4, sp.Y), new Point(sp.X + 4, sp.Y));
                context.DrawLine(crossPen, new Point(sp.X, sp.Y - 4), new Point(sp.X, sp.Y + 4));
                readoutText = $"Pipette ({sampledHex}) • ({cp.X:0}, {cp.Y:0}) • {Math.Round(Zoom * 100):0}%";
            }
            else if (ActiveTool is DrawingToolKind.Pen or DrawingToolKind.Highlighter or DrawingToolKind.Eraser)
            {
                double diameter = ActiveTool switch
                {
                    DrawingToolKind.Highlighter => Math.Max(12.0, ActiveThickness * 3.0),
                    DrawingToolKind.Eraser => Math.Max(6.0, ActiveThickness * 2.0),
                    _ => ActiveThickness
                };
                double r = Math.Max(3.0, (diameter * Zoom) / 2.0);

                if (ActiveTool == DrawingToolKind.Eraser)
                {
                    var eraserFill = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
                    var outerRing = new Pen(new SolidColorBrush(Color.Parse("#EF4444")), 1.4);
                    var innerRing = new Pen(new SolidColorBrush(Color.Parse("#334155")), 1.0);
                    context.DrawEllipse(eraserFill, outerRing, sp, r, r);
                    if (r > 2.5)
                        context.DrawEllipse(null, innerRing, sp, r - 1.2, r - 1.2);
                }
                else
                {
                    var resolved = ResolveStrokeColor(ActiveColorHex);
                    var fill = ResolveStrokeBrush(ActiveColorHex, 0.28);
                    var ring = new Pen(ResolveStrokeBrush(ActiveColorHex, 0.95), 1.4);
                    context.DrawEllipse(fill, ring, sp, r, r);
                    if (IsCanvasBackgroundLight() && (resolved.R > 220 && resolved.G > 220 && resolved.B > 220))
                        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#475569")), 1.0), sp, r + 1.0, r + 1.0);
                    context.DrawEllipse(ResolveStrokeBrush(ActiveColorHex, 0.95), null, sp, 1.5, 1.5);
                }

                readoutText = $"{ActiveTool} • ({cp.X:0}, {cp.Y:0}) • {ActiveThickness:0.#}px • {Math.Round(Zoom * 100):0}%";
            }
            else
            {
                var ringBrush = ResolveStrokeBrush(ActiveColorHex, 0.9);
                var crossPen = new Pen(new SolidColorBrush(Color.Parse("#475569")), 1.1);
                context.DrawLine(crossPen, new Point(sp.X - 8, sp.Y), new Point(sp.X + 8, sp.Y));
                context.DrawLine(crossPen, new Point(sp.X, sp.Y - 8), new Point(sp.X, sp.Y + 8));
                context.DrawEllipse(null, new Pen(ringBrush, 1.4), sp, 3.0, 3.0);
                readoutText = $"{ActiveTool} • ({cp.X:0}, {cp.Y:0}) • {ActiveThickness:0.#}px • {Math.Round(Zoom * 100):0}%";
            }
            var ft = new FormattedText(
                readoutText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Medium),
                11.0,
                new SolidColorBrush(Color.Parse("#0F172A")));
            double pillW = ft.Width + 14;
            double pillH = ft.Height + 6;
            if (bounds.Width > pillW + 16 && bounds.Height > pillH + 16)
            {
                var pillRect = new Rect(bounds.Right - pillW - 10, bounds.Bottom - pillH - 10, pillW, pillH);
                context.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb(220, 241, 245, 249)),
                    new Pen(new SolidColorBrush(Color.Parse("#CBD5E1")), 1.0),
                    pillRect, 4, 4);
                context.DrawText(ft, new Point(pillRect.X + 7, pillRect.Y + 3));
            }
        }

        private void DrawBackgroundPattern(DrawingContext context, Rect bounds, double patternOpacity = 1.0)
        {
            if (_document.Background == CanvasBackgroundKind.Blank)
                return;

            double step = Math.Max(10.0, 24.0 * Zoom);
            double startX = ((PanOffset.X % step) + step) % step;
            double startY = ((PanOffset.Y % step) + step) % step;

            bool lightCanvas = IsCanvasBackgroundLight();
            var fallbackGrid = lightCanvas ? "#E5E5E5" : "#2E2E2E";
            var gridColor = Color.TryParse(_customGridColorHex, out var customGrid)
                ? customGrid
                : Color.Parse(fallbackGrid);
            byte lineAlpha = (byte)Math.Clamp((int)Math.Round(gridColor.A * patternOpacity), 0, 255);
            var lineBrush = new SolidColorBrush(Color.FromArgb(lineAlpha, gridColor.R, gridColor.G, gridColor.B));
            var pen = new Pen(lineBrush, 1.0);

            switch (_document.Background)
            {
                case CanvasBackgroundKind.Grid:
                    for (double x = startX; x < bounds.Width; x += step)
                        context.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Height));
                    for (double y = startY; y < bounds.Height; y += step)
                        context.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));
                    break;

                case CanvasBackgroundKind.Dotted:
                {
                    var dotBase = Color.Parse(lightCanvas ? "#C8C8C8" : "#444444");
                    byte dotAlpha = (byte)Math.Clamp((int)Math.Round(dotBase.A * patternOpacity), 0, 255);
                    var dotBrush = new SolidColorBrush(Color.FromArgb(dotAlpha, dotBase.R, dotBase.G, dotBase.B));
                    double r = Math.Max(1.0, 1.3 * Zoom);
                    for (double x = startX; x < bounds.Width; x += step)
                    {
                        for (double y = startY; y < bounds.Height; y += step)
                            context.DrawEllipse(dotBrush, null, new Point(x, y), r, r);
                    }
                    break;
                }

                case CanvasBackgroundKind.Ruled:
                {
                    for (double y = startY; y < bounds.Height; y += step)
                        context.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));
                    double marginX = PanOffset.X + 64.0 * Zoom;
                    if (marginX >= 0 && marginX <= bounds.Width)
                    {
                        var marginBase = Color.Parse(lightCanvas ? "#FCA5A5" : "#4C1D2A");
                        byte marginAlpha = (byte)Math.Clamp((int)Math.Round(marginBase.A * patternOpacity), 0, 255);
                        var marginPen = new Pen(new SolidColorBrush(Color.FromArgb(marginAlpha, marginBase.R, marginBase.G, marginBase.B)), 1.2);
                        context.DrawLine(marginPen, new Point(marginX, 0), new Point(marginX, bounds.Height));
                    }
                    break;
                }
            }
        }

        private void DrawStroke(DrawingContext context, DrawingStroke stroke)
        {
            if (stroke.Points.Count == 0)
                return;

            if (stroke.ClipRect is { Width: > 0, Height: > 0 } clip)
            {
                using (context.PushClip(clip.ToAvaloniaRect()))
                    DrawStrokeCore(context, stroke);
                return;
            }

            DrawStrokeCore(context, stroke);
        }

        private void DrawStrokeCore(DrawingContext context, DrawingStroke stroke)
        {
            var brush = stroke.Tool == DrawingToolKind.Eraser
                ? new SolidColorBrush(GetEffectiveCanvasBackgroundColor())
                : ResolveStrokeBrush(stroke.ColorHex, stroke.Opacity);
            var pen = new Pen(brush, stroke.Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

            switch (stroke.Tool)
            {
                case DrawingToolKind.Pen:
                case DrawingToolKind.Highlighter:
                case DrawingToolKind.Eraser:
                {
                    if (stroke.Points.Count == 1)
                    {
                        var p = stroke.Points[0];
                        double r = stroke.Thickness / 2.0;
                        context.DrawEllipse(brush, null, new Point(p.X, p.Y), r, r);
                        return;
                    }

                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(new Point(stroke.Points[0].X, stroke.Points[0].Y), false);
                        for (int i = 1; i < stroke.Points.Count - 1; i++)
                        {
                            var p1 = stroke.Points[i];
                            var p2 = stroke.Points[i + 1];
                            var mid = new Point((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0);
                            g.QuadraticBezierTo(new Point(p1.X, p1.Y), mid);
                        }
                        var last = stroke.Points[^1];
                        g.LineTo(new Point(last.X, last.Y));
                        g.EndFigure(false);
                    }
                    context.DrawGeometry(null, pen, geo);
                    break;
                }

                case DrawingToolKind.Line:
                case DrawingToolKind.Arrow:
                {
                    var p0 = new Point(stroke.Points[0].X, stroke.Points[0].Y);
                    var p1 = new Point(stroke.Points[^1].X, stroke.Points[^1].Y);
                    context.DrawLine(pen, p0, p1);
                    if (stroke.Tool == DrawingToolKind.Arrow && (Math.Abs(p1.X - p0.X) > 2 || Math.Abs(p1.Y - p0.Y) > 2))
                    {
                        double angle = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X);
                        double headLen = Math.Max(11.0, stroke.Thickness * 3.4);
                        double wing = Math.PI / 6.0;
                        var w1 = new Point(p1.X - headLen * Math.Cos(angle - wing), p1.Y - headLen * Math.Sin(angle - wing));
                        var w2 = new Point(p1.X - headLen * Math.Cos(angle + wing), p1.Y - headLen * Math.Sin(angle + wing));
                        context.DrawLine(pen, w1, p1);
                        context.DrawLine(pen, w2, p1);
                    }
                    break;
                }

                case DrawingToolKind.Rectangle:
                {
                    var p0 = stroke.Points[0];
                    var p1 = stroke.Points[^1];
                    var rect = new Rect(
                        Math.Min(p0.X, p1.X),
                        Math.Min(p0.Y, p1.Y),
                        Math.Abs(p1.X - p0.X),
                        Math.Abs(p1.Y - p0.Y));
                    context.DrawRectangle(null, pen, rect, 4.0, 4.0);
                    break;
                }

                case DrawingToolKind.Ellipse:
                {
                    var p0 = stroke.Points[0];
                    var p1 = stroke.Points[^1];
                    var center = new Point((p0.X + p1.X) / 2.0, (p0.Y + p1.Y) / 2.0);
                    double rx = Math.Abs(p1.X - p0.X) / 2.0;
                    double ry = Math.Abs(p1.Y - p0.Y) / 2.0;
                    context.DrawEllipse(null, pen, center, rx, ry);
                    break;
                }

                case DrawingToolKind.TextLabel:
                {
                    var p = new Point(stroke.Points[0].X, stroke.Points[0].Y);
                    string text = stroke.TextContent ?? string.Empty;
                    if (stroke.IsLatex)
                    {
                        var parsed = LatexParser.Parse(text, isDisplayMode: false);
                        var box = LatexMathRenderer.Measure(parsed.Root, 18.0, brush, isDisplayMode: false);
                        var badgeRect = new Rect(p.X - 6, p.Y - 4, box.Width + 12, box.Height + 8);
                        bool darkCanvas = !IsCanvasBackgroundLight();
                        var badgeFill = new SolidColorBrush(darkCanvas ? Color.FromArgb(200, 30, 41, 59) : Color.FromArgb(220, 241, 245, 249));
                        var badgeBorder = new Pen(new SolidColorBrush(Color.Parse(darkCanvas ? "#475569" : "#94A3B8")), 1.0);
                        context.DrawRectangle(badgeFill, badgeBorder, badgeRect, 5, 5);
                        box.Draw(context, p);
                    }
                    else
                    {
                        var ft = new FormattedText(
                            text,
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Medium),
                            15.0,
                            brush);
                        context.DrawText(ft, p);
                    }
                    break;
                }

                case DrawingToolKind.Image:
                {
                    if (stroke.Points.Count >= 2)
                    {
                        var bmp = GetOrDecodeImageBitmap(stroke.ImageBase64);
                        if (bmp is not null)
                        {
                            var p0 = stroke.Points[0];
                            var p1 = stroke.Points[^1];
                            var rect = new Rect(
                                Math.Min(p0.X, p1.X),
                                Math.Min(p0.Y, p1.Y),
                                Math.Max(1.0, Math.Abs(p1.X - p0.X)),
                                Math.Max(1.0, Math.Abs(p1.Y - p0.Y)));
                            context.DrawImage(bmp, rect);
                        }
                    }
                    break;
                }
            }
        }

        private Color ResolveStrokeColor(string colorHex)
        {
            bool darkCanvas = !IsCanvasBackgroundLight();
            if (string.Equals(colorHex, "AUTO", StringComparison.OrdinalIgnoreCase))
                return darkCanvas ? Color.Parse("#F1F5F9") : Color.Parse("#0F172A");

            if (Color.TryParse(colorHex, out var parsed))
                return parsed;

            return darkCanvas ? Colors.White : Color.Parse("#0F172A");
        }

        private IBrush ResolveStrokeBrush(string colorHex, double opacity)
        {
            Color color = ResolveStrokeColor(colorHex);
            byte alpha = (byte)Math.Clamp((int)Math.Round(color.A * opacity), 0, 255);
            return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        private DrawingStroke? HitTestStroke(Point pt, double tolerance, bool includeImages = true)
        {
            for (int i = _document.Strokes.Count - 1; i >= 0; i--)
            {
                var s = _document.Strokes[i];
                if (s.Points.Count == 0) continue;

                if (s.Tool == DrawingToolKind.Image)
                {
                    if (includeImages && s.Points.Count >= 2)
                    {
                        var p0 = s.Points[0];
                        var p1 = s.Points[^1];
                        var imgRect = new Rect(
                            Math.Min(p0.X, p1.X),
                            Math.Min(p0.Y, p1.Y),
                            Math.Abs(p1.X - p0.X),
                            Math.Abs(p1.Y - p0.Y)).Inflate(tolerance);
                        if (imgRect.Contains(pt))
                            return s;
                    }
                    continue;
                }

                double r = tolerance + s.Thickness / 2.0;

                if (s.Tool == DrawingToolKind.TextLabel)
                {
                    var p = s.Points[0];
                    var rect = new Rect(p.X - 10, p.Y - 10, 140, 40);
                    if (rect.Contains(pt)) return s;
                    continue;
                }

                if (s.Tool is DrawingToolKind.Rectangle or DrawingToolKind.Ellipse)
                {
                    var p0 = s.Points[0];
                    var p1 = s.Points[^1];
                    double minX = Math.Min(p0.X, p1.X) - r;
                    double maxX = Math.Max(p0.X, p1.X) + r;
                    double minY = Math.Min(p0.Y, p1.Y) - r;
                    double maxY = Math.Max(p0.Y, p1.Y) + r;
                    if (pt.X >= minX && pt.X <= maxX && pt.Y >= minY && pt.Y <= maxY)
                    {
                        bool nearEdge = Math.Abs(pt.X - Math.Min(p0.X, p1.X)) <= r ||
                                        Math.Abs(pt.X - Math.Max(p0.X, p1.X)) <= r ||
                                        Math.Abs(pt.Y - Math.Min(p0.Y, p1.Y)) <= r ||
                                        Math.Abs(pt.Y - Math.Max(p0.Y, p1.Y)) <= r;
                        if (nearEdge) return s;
                    }
                    continue;
                }

                if (s.Points.Count == 1)
                {
                    if (Distance(pt, new Point(s.Points[0].X, s.Points[0].Y)) <= r)
                        return s;
                    continue;
                }

                for (int j = 0; j < s.Points.Count - 1; j++)
                {
                    var a = new Point(s.Points[j].X, s.Points[j].Y);
                    var b = new Point(s.Points[j + 1].X, s.Points[j + 1].Y);
                    if (DistanceToSegment(pt, a, b) <= r)
                        return s;
                }
            }
            return null;
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

        private static void TranslateStroke(DrawingStroke stroke, double dx, double dy)
        {
            foreach (var pt in stroke.Points)
            {
                pt.X += dx;
                pt.Y += dy;
            }
            if (stroke.ClipRect is not null)
            {
                stroke.ClipRect.X += dx;
                stroke.ClipRect.Y += dy;
            }
        }

        private static bool StrokeIntersectsRect(DrawingStroke s, Rect sel)
        {
            if (s.Points.Count == 0)
                return false;

            double minX = double.PositiveInfinity;
            double minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;
            double pad = s.Tool == DrawingToolKind.Image ? 0.0 : Math.Max(4.0, s.Thickness / 2.0);
            foreach (var pt in s.Points)
            {
                minX = Math.Min(minX, pt.X - pad);
                minY = Math.Min(minY, pt.Y - pad);
                maxX = Math.Max(maxX, pt.X + pad);
                maxY = Math.Max(maxY, pt.Y + pad);
            }

            var bounds = new Rect(minX, minY, Math.Max(1.0, maxX - minX), Math.Max(1.0, maxY - minY));
            if (s.ClipRect is { Width: > 0, Height: > 0 } clip)
                bounds = bounds.Intersect(clip.ToAvaloniaRect());
            return bounds.Width > 0 && bounds.Height > 0 && bounds.Intersects(sel);
        }

        private static double Distance(Point a, Point b)
            => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

        private static double DistanceToSegment(Point p, Point a, Point b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 0.0001) return Distance(p, a);

            double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0.0, 1.0);
            var proj = new Point(a.X + t * dx, a.Y + t * dy);
            return Distance(p, proj);
        }
    }
}
