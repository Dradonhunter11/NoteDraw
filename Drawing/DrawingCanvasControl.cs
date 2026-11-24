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
using NoteDraw.Rendering.Latex;

namespace NoteDraw.Drawing
{
    /// <summary>
    /// Interactive vector hand-drawing canvas supporting smooth freehand ink,
    /// highlighter, eraser, geometric shapes, canvas LaTeX/text labels, pan/zoom, and undo/redo.
    /// </summary>
    public sealed class DrawingCanvasControl : Control
    {
        private DrawingDocument _document = new();
        private string? _customCanvasBgHex;
        private string? _customGridColorHex;
        private readonly Stack<(DrawingDocument Doc, Rect? Selection)> _undoStack = new();
        private readonly Stack<(DrawingDocument Doc, Rect? Selection)> _redoStack = new();
        private readonly Dictionary<string, Bitmap> _imageBitmapCache = new(StringComparer.Ordinal);

        private enum SelectionResizeHandle
        {
            None,
            TopLeft,
            Top,
            TopRight,
            Right,
            BottomRight,
            Bottom,
            BottomLeft,
            Left
        }

        private DrawingStroke? _activeStroke;
        private DrawingDocument? _preStrokeSnapshot;
        private DrawingStroke? _draggedStroke;
        private Point _lastPointerScreenPos;
        private Point? _hoverScreenPos;
        private bool _isPanning;
        private bool _isDraggingStroke;
        private bool _isSelectingArea;
        private Point _selectionDragAnchor;
        private Rect? _draftSelectionArea;
        private bool _isMovingSelection;
        private readonly List<DrawingStroke> _movingSelectionStrokes = new();
        private bool _isResizingSelection;
        private SelectionResizeHandle _activeResizeHandle;
        private Rect _resizeOriginRect;
        private readonly List<(DrawingStroke Stroke, List<Point> OrigPoints, DrawingRect? OrigClip)> _resizeStrokeSnapshots = new();
        private bool _pushedMoveUndo;

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

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;

        public DrawingDocument Document => _document;

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
            PushUndoSnapshot();
            _document.Strokes.Add(stroke.Clone());
            InvalidateVisual();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        public (List<DrawingDocument> Undo, List<DrawingDocument> Redo) GetHistoryState()
        {
            var undoList = new List<DrawingDocument>(_undoStack.Count);
            foreach (var entry in _undoStack)
                undoList.Add(entry.Doc.Clone());
            var redoList = new List<DrawingDocument>(_redoStack.Count);
            foreach (var entry in _redoStack)
                redoList.Add(entry.Doc.Clone());
            return (undoList, redoList);
        }

        public void RestoreHistoryState(IReadOnlyList<DrawingDocument> undo, IReadOnlyList<DrawingDocument> redo)
        {
            _undoStack.Clear();
            for (int i = undo.Count - 1; i >= 0; i--)
                _undoStack.Push((undo[i].Clone(), null));

            _redoStack.Clear();
            for (int i = redo.Count - 1; i >= 0; i--)
                _redoStack.Push((redo[i].Clone(), null));

            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public void LoadDocument(DrawingDocument document)
        {
            _document = document?.Clone() ?? new DrawingDocument();
            _undoStack.Clear();
            _redoStack.Clear();
            _activeStroke = null;
            _preStrokeSnapshot = null;
            _isSelectingArea = false;
            _draftSelectionArea = null;
            _isMovingSelection = false;
            _movingSelectionStrokes.Clear();
            _isResizingSelection = false;
            _resizeStrokeSnapshots.Clear();
            SelectionArea = null;
            InvalidateVisual();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetBackground(CanvasBackgroundKind background)
        {
            if (_document.Background == background) return;
            PushUndoSnapshot();
            _document.Background = background;
            InvalidateVisual();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        public void AddTextLabel(Point canvasPoint, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (SelectionArea is { } activeSel && !activeSel.Contains(canvasPoint))
                return;

            PushUndoSnapshot();
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

            InvalidateVisual();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool Undo()
        {
            if (_undoStack.Count == 0) return false;
            _redoStack.Push((_document.Clone(), SelectionArea));
            var restored = _undoStack.Pop();
            _document = restored.Doc;
            if (HasSelectionArea && restored.Selection is not null)
                SelectionArea = restored.Selection;
            _activeStroke = null;
            _preStrokeSnapshot = null;
            InvalidateVisual();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool Redo()
        {
            if (_redoStack.Count == 0) return false;
            _undoStack.Push((_document.Clone(), SelectionArea));
            var restored = _redoStack.Pop();
            _document = restored.Doc;
            if (HasSelectionArea && restored.Selection is not null)
                SelectionArea = restored.Selection;
            _activeStroke = null;
            _preStrokeSnapshot = null;
            InvalidateVisual();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public static bool TryMapKeyToTool(Key key, KeyModifiers modifiers, out DrawingToolKind tool)
        {
            tool = default;
            if (modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Alt))
                return false;

            switch (key)
            {
                case Key.P:
                case Key.D1:
                case Key.NumPad1:
                    tool = DrawingToolKind.Pen;
                    return true;
                case Key.H:
                case Key.D2:
                case Key.NumPad2:
                    tool = DrawingToolKind.Highlighter;
                    return true;
                case Key.E:
                case Key.D3:
                case Key.NumPad3:
                    tool = DrawingToolKind.Eraser;
                    return true;
                case Key.I:
                case Key.K:
                case Key.D4:
                case Key.NumPad4:
                    tool = DrawingToolKind.Pipette;
                    return true;
                case Key.L:
                case Key.D5:
                case Key.NumPad5:
                    tool = DrawingToolKind.Line;
                    return true;
                case Key.A:
                case Key.D6:
                case Key.NumPad6:
                    tool = DrawingToolKind.Arrow;
                    return true;
                case Key.R:
                case Key.D7:
                case Key.NumPad7:
                    tool = DrawingToolKind.Rectangle;
                    return true;
                case Key.O:
                case Key.C:
                case Key.D8:
                case Key.NumPad8:
                    tool = DrawingToolKind.Ellipse;
                    return true;
                case Key.T:
                case Key.D9:
                case Key.NumPad9:
                    tool = DrawingToolKind.TextLabel;
                    return true;
                case Key.V:
                case Key.M:
                    tool = DrawingToolKind.SelectPan;
                    return true;
                case Key.G:
                    tool = DrawingToolKind.MovePosition;
                    return true;
                case Key.S:
                    tool = DrawingToolKind.SelectArea;
                    return true;
                case Key.W:
                    tool = DrawingToolKind.MoveSelection;
                    return true;
                case Key.Q:
                    tool = DrawingToolKind.ResizeSelection;
                    return true;
                default:
                    return false;
            }
        }

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

            PushUndoSnapshot();
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
            InvalidateVisual();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
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
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
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
            PushUndoSnapshot();
            _document.Strokes.Clear();
            InvalidateVisual();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
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

            PushUndoSnapshot();
            _document.Width = clampedW;
            _document.Height = clampedH;
            InvalidateVisual();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        public void FitCanvasToUsedBounds(double padding = 20.0)
        {
            var used = _document.GetUsedBounds(padding);
            double targetW = Math.Clamp(Math.Ceiling(used.Width), 120.0, 4096.0);
            double targetH = Math.Clamp(Math.Ceiling(used.Height), 100.0, 4096.0);

            PushUndoSnapshot();
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
            InvalidateVisual();
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

            DrawingRect? activeClip = SelectionArea is { Width: >= 5.0, Height: >= 5.0 } selBounds
                ? new DrawingRect(selBounds.X, selBounds.Y, selBounds.Width, selBounds.Height)
                : null;

            switch (effectiveTool)
            {
                case DrawingToolKind.Pen:
                case DrawingToolKind.Highlighter:
                case DrawingToolKind.Eraser:
                {
                    if (SelectionArea is { } sel && !sel.Contains(canvasPos))
                    {
                        IsPointerPressedOnCanvas = false;
                        e.Handled = true;
                        break;
                    }
                    _preStrokeSnapshot = _document.Clone();
                    bool isHigh = effectiveTool == DrawingToolKind.Highlighter;
                    bool isEraser = effectiveTool == DrawingToolKind.Eraser;
                    _activeStroke = new DrawingStroke
                    {
                        Tool = effectiveTool,
                        ColorHex = isEraser
                            ? "#FFFFFF"
                            : (isHigh && ActiveColorHex == "AUTO" ? "#FACC15" : ActiveColorHex),
                        Thickness = isEraser
                            ? Math.Max(6.0, ActiveThickness * 2.0)
                            : (isHigh ? Math.Max(12.0, ActiveThickness * 3.0) : ActiveThickness),
                        Opacity = isHigh ? 0.36 : 1.0,
                        ClipRect = activeClip,
                        Points = new List<DrawingPoint> { new(canvasPos.X, canvasPos.Y, pressure) }
                    };
                    e.Pointer.Capture(this);
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                }

                case DrawingToolKind.Pipette:
                {
                    PickColorAtCanvasPoint(canvasPos);
                    IsPointerPressedOnCanvas = false;
                    e.Handled = true;
                    break;
                }

                case DrawingToolKind.Line:
                case DrawingToolKind.Arrow:
                case DrawingToolKind.Rectangle:
                case DrawingToolKind.Ellipse:
                {
                    if (SelectionArea is { } sel && !sel.Contains(canvasPos))
                    {
                        IsPointerPressedOnCanvas = false;
                        e.Handled = true;
                        break;
                    }
                    _preStrokeSnapshot = _document.Clone();
                    _activeStroke = new DrawingStroke
                    {
                        Tool = effectiveTool,
                        ColorHex = ActiveColorHex,
                        Thickness = ActiveThickness,
                        Opacity = 1.0,
                        ClipRect = activeClip,
                        Points = new List<DrawingPoint>
                        {
                            new(canvasPos.X, canvasPos.Y, pressure),
                            new(canvasPos.X, canvasPos.Y, pressure)
                        }
                    };
                    e.Pointer.Capture(this);
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                }

                case DrawingToolKind.TextLabel:
                {
                    if (SelectionArea is { } sel && !sel.Contains(canvasPos))
                    {
                        IsPointerPressedOnCanvas = false;
                        e.Handled = true;
                        break;
                    }
                    TextLabelPlacementRequested?.Invoke(this, canvasPos);
                    e.Handled = true;
                    break;
                }

                case DrawingToolKind.SelectPan:
                {
                    _draggedStroke = HitTestStroke(canvasPos, tolerance: 12.0 / Zoom);
                    if (_draggedStroke is not null)
                    {
                        _isDraggingStroke = true;
                        _pushedMoveUndo = false;
                    }
                    else
                    {
                        _isPanning = true;
                    }
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    break;
                }

                case DrawingToolKind.MovePosition:
                {
                    _isPanning = true;
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    break;
                }

                case DrawingToolKind.SelectArea:
                {
                    if (e.ClickCount >= 2)
                    {
                        _isSelectingArea = false;
                        _draftSelectionArea = null;
                        IsPointerPressedOnCanvas = false;
                        ClearSelectionArea();
                        e.Handled = true;
                        break;
                    }

                    _isSelectingArea = true;
                    _selectionDragAnchor = canvasPos;
                    _draftSelectionArea = new Rect(canvasPos, canvasPos);
                    e.Pointer.Capture(this);
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                }

                case DrawingToolKind.MoveSelection:
                {
                    if (SelectionArea is { } sel)
                    {
                        if (sel.Inflate(10.0 / Math.Max(0.25, Zoom)).Contains(canvasPos))
                        {
                            _movingSelectionStrokes.Clear();
                            foreach (var s in _document.Strokes)
                            {
                                if (StrokeIntersectsRect(s, sel))
                                    _movingSelectionStrokes.Add(s);
                            }
                            _isMovingSelection = true;
                            _pushedMoveUndo = false;
                            e.Pointer.Capture(this);
                        }
                        else
                        {
                            IsPointerPressedOnCanvas = false;
                        }
                        e.Handled = true;
                        break;
                    }

                    _draggedStroke = HitTestStroke(canvasPos, tolerance: 12.0 / Zoom);
                    if (_draggedStroke is not null)
                    {
                        _isDraggingStroke = true;
                        _pushedMoveUndo = false;
                        e.Pointer.Capture(this);
                    }
                    else
                    {
                        IsPointerPressedOnCanvas = false;
                    }
                    e.Handled = true;
                    break;
                }

                case DrawingToolKind.ResizeSelection:
                {
                    if (SelectionArea is { } sel)
                    {
                        var handle = HitTestResizeHandle(canvasPos, sel, tolerance: 14.0 / Math.Max(0.25, Zoom));
                        if (handle == SelectionResizeHandle.None && sel.Inflate(10.0 / Math.Max(0.25, Zoom)).Contains(canvasPos))
                            handle = SelectionResizeHandle.BottomRight;

                        if (handle != SelectionResizeHandle.None)
                        {
                            _isResizingSelection = true;
                            _activeResizeHandle = handle;
                            _resizeOriginRect = sel;
                            _pushedMoveUndo = false;
                            _resizeStrokeSnapshots.Clear();
                            foreach (var s in _document.Strokes)
                            {
                                if (StrokeIntersectsRect(s, sel))
                                {
                                    var pts = new List<Point>(s.Points.Count);
                                    foreach (var pt in s.Points)
                                        pts.Add(new Point(pt.X, pt.Y));
                                    _resizeStrokeSnapshots.Add((s, pts, s.ClipRect?.Clone()));
                                }
                            }
                            e.Pointer.Capture(this);
                        }
                        else
                        {
                            IsPointerPressedOnCanvas = false;
                        }
                    }
                    else
                    {
                        IsPointerPressedOnCanvas = false;
                    }
                    e.Handled = true;
                    break;
                }
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var screenPos = GetCanvasLocalPointerPosition(e);
            _hoverScreenPos = screenPos;

            if (IsPointerPressedOnCanvas || _activeStroke is not null || _isPanning || _isDraggingStroke ||
                _isSelectingArea || _isMovingSelection || _isResizingSelection)
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

            var canvasPos = ScreenToCanvas(screenPos);

            if (_isSelectingArea)
            {
                _draftSelectionArea = NormalizeRect(_selectionDragAnchor, canvasPos);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_isMovingSelection && SelectionArea is { } curMoveSel)
            {
                var prevCanvas = ScreenToCanvas(_lastPointerScreenPos);
                double dx = canvasPos.X - prevCanvas.X;
                double dy = canvasPos.Y - prevCanvas.Y;
                if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                {
                    if (!_pushedMoveUndo)
                    {
                        PushUndoSnapshot();
                        _pushedMoveUndo = true;
                    }
                    SelectionArea = new Rect(curMoveSel.X + dx, curMoveSel.Y + dy, curMoveSel.Width, curMoveSel.Height);
                    foreach (var s in _movingSelectionStrokes)
                        TranslateStroke(s, dx, dy);
                    InvalidateVisual();
                }
                _lastPointerScreenPos = screenPos;
                e.Handled = true;
                return;
            }

            if (_isResizingSelection)
            {
                Rect nextRect = ComputeResizedRect(_resizeOriginRect, _activeResizeHandle, canvasPos, minSize: 5.0);
                if (Math.Abs(nextRect.X - _resizeOriginRect.X) > 0.05 ||
                    Math.Abs(nextRect.Y - _resizeOriginRect.Y) > 0.05 ||
                    Math.Abs(nextRect.Width - _resizeOriginRect.Width) > 0.05 ||
                    Math.Abs(nextRect.Height - _resizeOriginRect.Height) > 0.05)
                {
                    if (!_pushedMoveUndo)
                    {
                        PushUndoSnapshot();
                        _pushedMoveUndo = true;
                    }

                    SelectionArea = nextRect;
                    double sx = nextRect.Width / Math.Max(1.0, _resizeOriginRect.Width);
                    double sy = nextRect.Height / Math.Max(1.0, _resizeOriginRect.Height);

                    foreach (var (stroke, origPts, origClip) in _resizeStrokeSnapshots)
                    {
                        for (int i = 0; i < stroke.Points.Count && i < origPts.Count; i++)
                        {
                            stroke.Points[i].X = nextRect.X + (origPts[i].X - _resizeOriginRect.X) * sx;
                            stroke.Points[i].Y = nextRect.Y + (origPts[i].Y - _resizeOriginRect.Y) * sy;
                        }

                        if (origClip is not null && stroke.ClipRect is not null)
                        {
                            stroke.ClipRect.X = nextRect.X + (origClip.X - _resizeOriginRect.X) * sx;
                            stroke.ClipRect.Y = nextRect.Y + (origClip.Y - _resizeOriginRect.Y) * sy;
                            stroke.ClipRect.Width = Math.Max(5.0, origClip.Width * sx);
                            stroke.ClipRect.Height = Math.Max(5.0, origClip.Height * sy);
                        }
                    }

                    InvalidateVisual();
                }
                _lastPointerScreenPos = screenPos;
                e.Handled = true;
                return;
            }

            if (_isDraggingStroke && _draggedStroke is not null)
            {
                var prevCanvas = ScreenToCanvas(_lastPointerScreenPos);
                double dx = canvasPos.X - prevCanvas.X;
                double dy = canvasPos.Y - prevCanvas.Y;
                if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                {
                    if (!_pushedMoveUndo)
                    {
                        PushUndoSnapshot();
                        _pushedMoveUndo = true;
                    }
                    TranslateStroke(_draggedStroke, dx, dy);
                    InvalidateVisual();
                }
                _lastPointerScreenPos = screenPos;
                e.Handled = true;
                return;
            }

            if (_activeStroke is null)
            {
                InvalidateVisual();
                return;
            }

            var ptInfo = e.GetCurrentPoint(this);
            if (e.Pointer.Type == PointerType.Mouse &&
                !ptInfo.Properties.IsLeftButtonPressed &&
                !ptInfo.Properties.IsEraser)
            {
                IsPointerPressedOnCanvas = false;
                CommitActiveStroke();
                e.Pointer.Capture(null);
                e.Handled = true;
                return;
            }

            double pressure = ptInfo.Properties.Pressure > 0 ? ptInfo.Properties.Pressure : 0.5;

            if (_activeStroke.Tool is DrawingToolKind.Pen or DrawingToolKind.Highlighter or DrawingToolKind.Eraser)
            {
                bool addedAny = false;
                var intermediate = e.GetIntermediatePoints(this);
                if (intermediate is { Count: > 0 })
                {
                    foreach (var sample in intermediate)
                    {
                        var sampleCanvas = ScreenToCanvas(sample.Position);
                        double samplePressure = sample.Properties.Pressure > 0 ? sample.Properties.Pressure : pressure;
                        var last = _activeStroke.Points[^1];
                        double distSq = Math.Pow(sampleCanvas.X - last.X, 2) + Math.Pow(sampleCanvas.Y - last.Y, 2);
                        if (distSq >= 2.25)
                        {
                            _activeStroke.Points.Add(new DrawingPoint(sampleCanvas.X, sampleCanvas.Y, samplePressure));
                            addedAny = true;
                        }
                    }
                }

                var finalLast = _activeStroke.Points[^1];
                double finalDistSq = Math.Pow(canvasPos.X - finalLast.X, 2) + Math.Pow(canvasPos.Y - finalLast.Y, 2);
                if (finalDistSq >= 2.25)
                {
                    _activeStroke.Points.Add(new DrawingPoint(canvasPos.X, canvasPos.Y, pressure));
                    addedAny = true;
                }

                if (addedAny)
                    InvalidateVisual();
            }
            else if (_activeStroke.Points.Count >= 2)
            {
                _activeStroke.Points[1] = new DrawingPoint(canvasPos.X, canvasPos.Y, pressure);
                InvalidateVisual();
            }

            e.Handled = true;
        }

        private void FinalizeAreaSelection(Rect area)
        {
            _isSelectingArea = false;
            _draftSelectionArea = null;
            if (area.Width < 5.0 || area.Height < 5.0)
                ClearSelectionArea();
            else
                SetSelectionArea(area);
        }

        private void ClearMoveOrResizeSelectionFlags(bool emitEvents)
        {
            bool changed = _pushedMoveUndo;
            if (_isMovingSelection)
            {
                _isMovingSelection = false;
                _movingSelectionStrokes.Clear();
            }
            if (_isResizingSelection)
            {
                _isResizingSelection = false;
                _resizeStrokeSnapshots.Clear();
                _activeResizeHandle = SelectionResizeHandle.None;
            }
            if (_isDraggingStroke)
            {
                _isDraggingStroke = false;
                _draggedStroke = null;
            }
            if (changed)
            {
                _pushedMoveUndo = false;
                if (emitEvents)
                {
                    DocumentChanged?.Invoke(this, EventArgs.Empty);
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
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

            if (_isSelectingArea)
            {
                var releaseCanvas = ScreenToCanvas(GetCanvasLocalPointerPosition(e));
                FinalizeAreaSelection(NormalizeRect(_selectionDragAnchor, releaseCanvas));
                e.Pointer.Capture(null);
                e.Handled = true;
                return;
            }

            if (_isMovingSelection || _isResizingSelection || _isDraggingStroke)
            {
                ClearMoveOrResizeSelectionFlags(emitEvents: true);
                e.Pointer.Capture(null);
                e.Handled = true;
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

            if (_isSelectingArea)
                FinalizeAreaSelection(_draftSelectionArea ?? default);

            if (_isMovingSelection || _isResizingSelection || _isDraggingStroke)
                ClearMoveOrResizeSelectionFlags(emitEvents: true);

            // Never discard/undo an active stylus or pointer stroke if capture is interrupted.
            CommitActiveStroke();
        }

        private bool CommitActiveStroke()
        {
            if (_activeStroke is null)
                return false;

            if (_preStrokeSnapshot is not null)
            {
                _undoStack.Push((_preStrokeSnapshot, SelectionArea));
                _redoStack.Clear();
                _preStrokeSnapshot = null;
            }

            _document.Strokes.Add(_activeStroke);
            _activeStroke = null;
            InvalidateVisual();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
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
                foreach (var pt in GetSelectionHandleCenters(sel))
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

        private void PushUndoSnapshot()
        {
            _undoStack.Push((_document.Clone(), SelectionArea));
            _redoStack.Clear();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        private static Rect NormalizeRect(Point a, Point b) => new(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Abs(b.X - a.X),
            Math.Abs(b.Y - a.Y));

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

        private static Point[] GetSelectionHandleCenters(Rect sel) => new[]
        {
            new Point(sel.Left, sel.Top),
            new Point(sel.Center.X, sel.Top),
            new Point(sel.Right, sel.Top),
            new Point(sel.Right, sel.Center.Y),
            new Point(sel.Right, sel.Bottom),
            new Point(sel.Center.X, sel.Bottom),
            new Point(sel.Left, sel.Bottom),
            new Point(sel.Left, sel.Center.Y)
        };

        private static SelectionResizeHandle HitTestResizeHandle(Point pt, Rect sel, double tolerance)
        {
            var handles = new (SelectionResizeHandle Kind, Point Center)[]
            {
                (SelectionResizeHandle.TopLeft, new Point(sel.Left, sel.Top)),
                (SelectionResizeHandle.Top, new Point(sel.Center.X, sel.Top)),
                (SelectionResizeHandle.TopRight, new Point(sel.Right, sel.Top)),
                (SelectionResizeHandle.Right, new Point(sel.Right, sel.Center.Y)),
                (SelectionResizeHandle.BottomRight, new Point(sel.Right, sel.Bottom)),
                (SelectionResizeHandle.Bottom, new Point(sel.Center.X, sel.Bottom)),
                (SelectionResizeHandle.BottomLeft, new Point(sel.Left, sel.Bottom)),
                (SelectionResizeHandle.Left, new Point(sel.Left, sel.Center.Y))
            };

            SelectionResizeHandle best = SelectionResizeHandle.None;
            double bestDist = tolerance;
            foreach (var (kind, center) in handles)
            {
                double dist = Distance(pt, center);
                if (dist <= bestDist)
                {
                    bestDist = dist;
                    best = kind;
                }
            }
            if (best != SelectionResizeHandle.None)
                return best;

            if (pt.Y >= sel.Top - tolerance && pt.Y <= sel.Bottom + tolerance)
            {
                if (Math.Abs(pt.X - sel.Left) <= tolerance) return SelectionResizeHandle.Left;
                if (Math.Abs(pt.X - sel.Right) <= tolerance) return SelectionResizeHandle.Right;
            }
            if (pt.X >= sel.Left - tolerance && pt.X <= sel.Right + tolerance)
            {
                if (Math.Abs(pt.Y - sel.Top) <= tolerance) return SelectionResizeHandle.Top;
                if (Math.Abs(pt.Y - sel.Bottom) <= tolerance) return SelectionResizeHandle.Bottom;
            }
            return SelectionResizeHandle.None;
        }

        private static Rect ComputeResizedRect(Rect origin, SelectionResizeHandle handle, Point pt, double minSize)
        {
            double left = origin.Left;
            double top = origin.Top;
            double right = origin.Right;
            double bottom = origin.Bottom;

            if (handle is SelectionResizeHandle.TopLeft or SelectionResizeHandle.Left or SelectionResizeHandle.BottomLeft)
                left = Math.Min(pt.X, right - minSize);
            if (handle is SelectionResizeHandle.TopRight or SelectionResizeHandle.Right or SelectionResizeHandle.BottomRight)
                right = Math.Max(pt.X, left + minSize);
            if (handle is SelectionResizeHandle.TopLeft or SelectionResizeHandle.Top or SelectionResizeHandle.TopRight)
                top = Math.Min(pt.Y, bottom - minSize);
            if (handle is SelectionResizeHandle.BottomLeft or SelectionResizeHandle.Bottom or SelectionResizeHandle.BottomRight)
                bottom = Math.Max(pt.Y, top + minSize);

            return new Rect(left, top, Math.Max(minSize, right - left), Math.Max(minSize, bottom - top));
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
