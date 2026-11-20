using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        private readonly Stack<DrawingDocument> _undoStack = new();
        private readonly Stack<DrawingDocument> _redoStack = new();

        private DrawingStroke? _activeStroke;
        private DrawingDocument? _preStrokeSnapshot;
        private DrawingStroke? _draggedStroke;
        private Point _lastPointerScreenPos;
        private Point? _hoverScreenPos;
        private bool _isPanning;
        private bool _isDraggingStroke;
        private bool _pushedMoveUndo;

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
            return (
                new List<DrawingDocument>(_undoStack),
                new List<DrawingDocument>(_redoStack));
        }

        public void RestoreHistoryState(IReadOnlyList<DrawingDocument> undo, IReadOnlyList<DrawingDocument> redo)
        {
            _undoStack.Clear();
            for (int i = undo.Count - 1; i >= 0; i--)
                _undoStack.Push(undo[i].Clone());

            _redoStack.Clear();
            for (int i = redo.Count - 1; i >= 0; i--)
                _redoStack.Push(redo[i].Clone());

            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public void LoadDocument(DrawingDocument document)
        {
            _document = document?.Clone() ?? new DrawingDocument();
            _undoStack.Clear();
            _redoStack.Clear();
            _activeStroke = null;
            _preStrokeSnapshot = null;
            InvalidateVisual();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
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
                Points = new List<DrawingPoint> { new(canvasPoint.X, canvasPoint.Y) }
            });

            InvalidateVisual();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool Undo()
        {
            if (_undoStack.Count == 0) return false;
            _redoStack.Push(_document.Clone());
            _document = _undoStack.Pop();
            _activeStroke = null;
            _preStrokeSnapshot = null;
            InvalidateVisual();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool Redo()
        {
            if (_redoStack.Count == 0) return false;
            _undoStack.Push(_document.Clone());
            _document = _redoStack.Pop();
            _activeStroke = null;
            _preStrokeSnapshot = null;
            InvalidateVisual();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (e.Key == Key.Z && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    if (Undo()) e.Handled = true;
                }
                else if (e.Key == Key.Y || (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
                {
                    if (Redo()) e.Handled = true;
                }
            }
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

        public void ResetZoomAndPan()
        {
            Zoom = 1.0;
            PanOffset = default;
            InvalidateVisual();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
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
            _hoverScreenPos = e.GetPosition(this);
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

            var pointInfo = e.GetCurrentPoint(this);
            var screenPos = pointInfo.Position;
            _lastPointerScreenPos = screenPos;
            _hoverScreenPos = screenPos;

            if (pointInfo.Properties.IsRightButtonPressed)
            {
                ContextSubMenuRequested?.Invoke(this, screenPos);
                e.Handled = true;
                return;
            }

            if (pointInfo.Properties.IsMiddleButtonPressed)
            {
                _isPanning = true;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            if (!pointInfo.Properties.IsLeftButtonPressed)
                return;

            CanvasLeftClicked?.Invoke(this, EventArgs.Empty);

            var canvasPos = ScreenToCanvas(screenPos);
            double pressure = pointInfo.Properties.Pressure > 0 ? pointInfo.Properties.Pressure : 0.5;

            switch (ActiveTool)
            {
                case DrawingToolKind.Pen:
                case DrawingToolKind.Highlighter:
                case DrawingToolKind.Eraser:
                {
                    _preStrokeSnapshot = _document.Clone();
                    bool isHigh = ActiveTool == DrawingToolKind.Highlighter;
                    bool isEraser = ActiveTool == DrawingToolKind.Eraser;
                    _activeStroke = new DrawingStroke
                    {
                        Tool = ActiveTool,
                        ColorHex = isEraser
                            ? "#FFFFFF"
                            : (isHigh && ActiveColorHex == "AUTO" ? "#FACC15" : ActiveColorHex),
                        Thickness = isEraser
                            ? Math.Max(6.0, ActiveThickness * 2.0)
                            : (isHigh ? Math.Max(12.0, ActiveThickness * 3.0) : ActiveThickness),
                        Opacity = isHigh ? 0.36 : 1.0,
                        Points = new List<DrawingPoint> { new(canvasPos.X, canvasPos.Y, pressure) }
                    };
                    e.Pointer.Capture(this);
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                }

                case DrawingToolKind.Line:
                case DrawingToolKind.Arrow:
                case DrawingToolKind.Rectangle:
                case DrawingToolKind.Ellipse:
                {
                    _preStrokeSnapshot = _document.Clone();
                    _activeStroke = new DrawingStroke
                    {
                        Tool = ActiveTool,
                        ColorHex = ActiveColorHex,
                        Thickness = ActiveThickness,
                        Opacity = 1.0,
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
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var screenPos = e.GetPosition(this);
            _hoverScreenPos = screenPos;

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
                    foreach (var pt in _draggedStroke.Points)
                    {
                        pt.X += dx;
                        pt.Y += dy;
                    }
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
            double pressure = ptInfo.Properties.Pressure > 0 ? ptInfo.Properties.Pressure : 0.5;

            if (_activeStroke.Tool is DrawingToolKind.Pen or DrawingToolKind.Highlighter or DrawingToolKind.Eraser)
            {
                var last = _activeStroke.Points[^1];
                double distSq = Math.Pow(canvasPos.X - last.X, 2) + Math.Pow(canvasPos.Y - last.Y, 2);
                if (distSq >= 2.25)
                {
                    _activeStroke.Points.Add(new DrawingPoint(canvasPos.X, canvasPos.Y, pressure));
                    InvalidateVisual();
                }
            }
            else if (_activeStroke.Points.Count >= 2)
            {
                _activeStroke.Points[1] = new DrawingPoint(canvasPos.X, canvasPos.Y, pressure);
                InvalidateVisual();
            }

            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (_isPanning)
            {
                _isPanning = false;
                e.Pointer.Capture(null);
                e.Handled = true;
                return;
            }

            if (_isDraggingStroke)
            {
                _isDraggingStroke = false;
                _draggedStroke = null;
                e.Pointer.Capture(null);
                if (_pushedMoveUndo)
                    DocumentChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            if (_activeStroke is not null)
            {
                if (_preStrokeSnapshot is not null)
                {
                    _undoStack.Push(_preStrokeSnapshot);
                    _redoStack.Clear();
                    _preStrokeSnapshot = null;
                }
                _document.Strokes.Add(_activeStroke);
                _activeStroke = null;
                e.Pointer.Capture(null);
                InvalidateVisual();
                HistoryChanged?.Invoke(this, EventArgs.Empty);
                DocumentChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                var cursorScreen = e.GetPosition(this);
                var beforeCanvas = ScreenToCanvas(cursorScreen);
                double factor = e.Delta.Y > 0 ? 1.12 : 0.89;
                Zoom = Math.Clamp(Zoom * factor, 0.25, 4.0);
                PanOffset = new Point(
                    cursorScreen.X - beforeCanvas.X * Zoom,
                    cursorScreen.Y - beforeCanvas.Y * Zoom);
                InvalidateVisual();
                ViewportChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
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
            }

            if (_document.Background != CanvasBackgroundKind.Blank && HasEraserStrokes())
                DrawBackgroundPattern(context, bounds, patternOpacity: 0.45);

            if (_hoverScreenPos.HasValue)
                DrawMousePreview(context, bounds, _hoverScreenPos.Value);
        }

        private void DrawMousePreview(DrawingContext context, Rect bounds, Point sp)
        {
            if (!bounds.Contains(sp))
                return;

            Point cp = ScreenToCanvas(sp);
            if (ActiveTool is DrawingToolKind.Pen or DrawingToolKind.Highlighter or DrawingToolKind.Eraser)
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
            }
            else
            {
                var ringBrush = ResolveStrokeBrush(ActiveColorHex, 0.9);
                var crossPen = new Pen(new SolidColorBrush(Color.Parse("#475569")), 1.1);
                context.DrawLine(crossPen, new Point(sp.X - 8, sp.Y), new Point(sp.X + 8, sp.Y));
                context.DrawLine(crossPen, new Point(sp.X, sp.Y - 8), new Point(sp.X, sp.Y + 8));
                context.DrawEllipse(null, new Pen(ringBrush, 1.4), sp, 3.0, 3.0);
            }

            string readoutText = $"{ActiveTool} • ({cp.X:0}, {cp.Y:0}) • {ActiveThickness:0.#}px";
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

        private DrawingStroke? HitTestStroke(Point pt, double tolerance)
        {
            for (int i = _document.Strokes.Count - 1; i >= 0; i--)
            {
                var s = _document.Strokes[i];
                if (s.Points.Count == 0) continue;

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
            _undoStack.Push(_document.Clone());
            _redoStack.Clear();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
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
