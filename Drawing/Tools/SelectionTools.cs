using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using NoteDraw.Drawing.Commands;

namespace NoteDraw.Drawing.Tools
{
    public sealed class SelectAreaTool : DrawingToolBase
    {
        private bool _isSelectingArea;
        private Point _dragAnchor;

        public override DrawingToolKind Kind => DrawingToolKind.SelectArea;
        public override string DisplayName => "Select Area";
        public override string Tooltip => "Select Drawing Area (Shortcut: S • Double-click or <5x5 to cancel)";
        public override string IconPathData => "M3 5H5V3H3V5ZM7 5H9V3H7V5ZM11 5H13V3H11V5ZM15 5H17V3H15V5ZM19 5H21V3H19V5ZM3 9H5V7H3V9ZM19 9H21V7H19V9ZM3 13H5V11H3V13ZM19 13H21V11H19V13ZM3 17H5V15H3V17ZM19 17H21V15H19V17ZM3 21H5V19H3V21ZM7 21H9V19H7V21ZM11 21H13V19H11V21ZM15 21H17V19H15V21ZM19 21H21V19H19V21Z";
        public override DrawingToolGroup Group => DrawingToolGroup.NavigationAndSelection;
        public override IReadOnlyList<Key> ShortcutKeys { get; } = new[] { Key.S };
        public override bool IsActiveInteraction => _isSelectingArea;

        public override void OnPointerPressed(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            if (input.ClickCount >= 2)
            {
                _isSelectingArea = false;
                ctx.DraftSelectionArea = null;
                ctx.IsPointerPressedOnCanvas = false;
                ctx.ClearSelectionArea();
                input.RawEvent.Handled = true;
                return;
            }

            _isSelectingArea = true;
            _dragAnchor = input.CanvasPosition;
            ctx.DraftSelectionArea = new Rect(input.CanvasPosition, input.CanvasPosition);
            input.Pointer.Capture(ctx.CaptureTarget);
            ctx.InvalidateVisual();
            input.RawEvent.Handled = true;
        }

        public override void OnPointerMoved(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            if (!_isSelectingArea)
                return;

            ctx.DraftSelectionArea = NormalizeRect(_dragAnchor, input.CanvasPosition);
            ctx.InvalidateVisual();
            input.RawEvent.Handled = true;
        }

        public override void OnPointerReleased(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            if (!_isSelectingArea)
                return;

            FinalizeAreaSelection(ctx, NormalizeRect(_dragAnchor, input.CanvasPosition));
            input.Pointer.Capture(null);
            input.RawEvent.Handled = true;
        }

        public override void OnPointerCaptureLost(ICanvasToolContext ctx)
        {
            if (_isSelectingArea)
                FinalizeAreaSelection(ctx, ctx.DraftSelectionArea ?? default);
        }

        public override void Cancel(ICanvasToolContext ctx)
        {
            _isSelectingArea = false;
            ctx.DraftSelectionArea = null;
            ctx.InvalidateVisual();
        }

        private void FinalizeAreaSelection(ICanvasToolContext ctx, Rect area)
        {
            _isSelectingArea = false;
            ctx.DraftSelectionArea = null;
            if (area.Width < 5.0 || area.Height < 5.0)
                ctx.ClearSelectionArea();
            else
                ctx.SetSelectionArea(area);
        }

        private static Rect NormalizeRect(Point a, Point b) => new(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Abs(b.X - a.X),
            Math.Abs(b.Y - a.Y));
    }

    public sealed class MoveSelectionTool : DrawingToolBase
    {
        private bool _isMovingSelection;
        private bool _isDraggingStroke;
        private DrawingStroke? _draggedStroke;
        private readonly List<DrawingStroke> _movingSelectionStrokes = new();
        private DrawingDocument? _beforeDoc;
        private Rect? _beforeSelection;

        public override DrawingToolKind Kind => DrawingToolKind.MoveSelection;
        public override string DisplayName => "Move Selection";
        public override string Tooltip => "Move Selection Area & Contents (Shortcut: W)";
        public override string IconPathData => "M3 3H11V5H5V11H3V3ZM13 3H21V9H19V5H13V3ZM3 13H5V19H9V21H3V13ZM16 11L13 14H15V18H11V16L8 19L11 22V20H15V22L18 19L15 16V18H17V14H19L16 11Z";
        public override DrawingToolGroup Group => DrawingToolGroup.NavigationAndSelection;
        public override IReadOnlyList<Key> ShortcutKeys { get; } = new[] { Key.W };
        public override bool IsActiveInteraction => _isMovingSelection || _isDraggingStroke;

        public override void OnPointerPressed(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            if (ctx.SelectionArea is { } sel)
            {
                if (sel.Inflate(10.0 / Math.Max(0.25, ctx.Zoom)).Contains(input.CanvasPosition))
                {
                    _movingSelectionStrokes.Clear();
                    foreach (var s in ctx.Document.Strokes)
                    {
                        if (ctx.StrokeIntersectsRect(s, sel))
                            _movingSelectionStrokes.Add(s);
                    }
                    _isMovingSelection = true;
                    _beforeDoc = null;
                    _beforeSelection = null;
                    input.Pointer.Capture(ctx.CaptureTarget);
                }
                else
                {
                    ctx.IsPointerPressedOnCanvas = false;
                }
                input.RawEvent.Handled = true;
                return;
            }

            _draggedStroke = ctx.HitTestStroke(input.CanvasPosition, tolerance: 12.0 / Math.Max(0.25, ctx.Zoom));
            if (_draggedStroke is not null)
            {
                _isDraggingStroke = true;
                _beforeDoc = null;
                _beforeSelection = null;
                input.Pointer.Capture(ctx.CaptureTarget);
            }
            else
            {
                ctx.IsPointerPressedOnCanvas = false;
            }
            input.RawEvent.Handled = true;
        }

        public override void OnPointerMoved(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            if (_isMovingSelection && ctx.SelectionArea is { } curMoveSel)
            {
                var prevCanvas = ctx.ScreenToCanvas(ctx.LastPointerScreenPos);
                double dx = input.CanvasPosition.X - prevCanvas.X;
                double dy = input.CanvasPosition.Y - prevCanvas.Y;
                if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                {
                    if (_beforeDoc is null)
                    {
                        _beforeDoc = ctx.Document.Clone();
                        _beforeSelection = curMoveSel;
                    }
                    ctx.SetSelectionArea(new Rect(curMoveSel.X + dx, curMoveSel.Y + dy, curMoveSel.Width, curMoveSel.Height));
                    foreach (var s in _movingSelectionStrokes)
                        ctx.TranslateStroke(s, dx, dy);
                    ctx.InvalidateVisual();
                }
                ctx.LastPointerScreenPos = input.ScreenPosition;
                input.RawEvent.Handled = true;
                return;
            }

            if (_isDraggingStroke && _draggedStroke is not null)
            {
                var prevCanvas = ctx.ScreenToCanvas(ctx.LastPointerScreenPos);
                double dx = input.CanvasPosition.X - prevCanvas.X;
                double dy = input.CanvasPosition.Y - prevCanvas.Y;
                if (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01)
                {
                    if (_beforeDoc is null)
                    {
                        _beforeDoc = ctx.Document.Clone();
                        _beforeSelection = ctx.SelectionArea;
                    }
                    ctx.TranslateStroke(_draggedStroke, dx, dy);
                    ctx.InvalidateVisual();
                }
                ctx.LastPointerScreenPos = input.ScreenPosition;
                input.RawEvent.Handled = true;
            }
        }

        public override void OnPointerReleased(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            FinishInteraction(ctx, emitEvents: true);
            input.Pointer.Capture(null);
            input.RawEvent.Handled = true;
        }

        public override void OnPointerCaptureLost(ICanvasToolContext ctx)
            => FinishInteraction(ctx, emitEvents: true);

        public override void Cancel(ICanvasToolContext ctx)
            => FinishInteraction(ctx, emitEvents: false);

        private void FinishInteraction(ICanvasToolContext ctx, bool emitEvents)
        {
            bool changed = _beforeDoc is not null;
            if (changed && emitEvents)
            {
                ctx.RecordUndoCommand(new TransformStrokesCommand(
                    _isMovingSelection ? "Move Selection" : "Move Stroke",
                    _beforeDoc!,
                    ctx.Document.Clone(),
                    _beforeSelection,
                    ctx.SelectionArea));
            }
            _isMovingSelection = false;
            _isDraggingStroke = false;
            _draggedStroke = null;
            _movingSelectionStrokes.Clear();
            _beforeDoc = null;
            _beforeSelection = null;
            if (changed && emitEvents)
            {
                ctx.NotifyDocumentChanged();
                ctx.NotifySelectionChanged();
            }
        }
    }

    public sealed class ResizeSelectionTool : DrawingToolBase
    {
        internal enum SelectionResizeHandle
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

        private bool _isResizingSelection;
        private SelectionResizeHandle _activeResizeHandle;
        private Rect _resizeOriginRect;
        private readonly List<(DrawingStroke Stroke, List<Point> OrigPoints, DrawingRect? OrigClip)> _resizeStrokeSnapshots = new();
        private DrawingDocument? _beforeDoc;
        private Rect? _beforeSelection;

        public override DrawingToolKind Kind => DrawingToolKind.ResizeSelection;
        public override string DisplayName => "Resize Selection";
        public override string Tooltip => "Resize Selection Area & Contents (Shortcut: Q)";
        public override string IconPathData => "M21 11V3H13L16.29 6.29L6.29 16.29L3 13V21H11L7.71 17.71L17.71 7.71L21 11ZM3 5V9H5V5H9V3H5C3.9 3 3 3.9 3 5ZM19 19H15V21H19C20.1 21 21 20.1 21 19V15H19V19Z";
        public override DrawingToolGroup Group => DrawingToolGroup.NavigationAndSelection;
        public override IReadOnlyList<Key> ShortcutKeys { get; } = new[] { Key.Q };
        public override bool IsActiveInteraction => _isResizingSelection;

        public static Point[] GetSelectionHandleCenters(Rect sel) => new[]
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

        public override void OnPointerPressed(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            if (ctx.SelectionArea is { } sel)
            {
                var handle = HitTestResizeHandle(input.CanvasPosition, sel, tolerance: 14.0 / Math.Max(0.25, ctx.Zoom));
                if (handle == SelectionResizeHandle.None && sel.Inflate(10.0 / Math.Max(0.25, ctx.Zoom)).Contains(input.CanvasPosition))
                    handle = SelectionResizeHandle.BottomRight;

                if (handle != SelectionResizeHandle.None)
                {
                    _isResizingSelection = true;
                    _activeResizeHandle = handle;
                    _resizeOriginRect = sel;
                    _beforeDoc = null;
                    _beforeSelection = null;
                    _resizeStrokeSnapshots.Clear();
                    foreach (var s in ctx.Document.Strokes)
                    {
                        if (ctx.StrokeIntersectsRect(s, sel))
                        {
                            var pts = new List<Point>(s.Points.Count);
                            foreach (var pt in s.Points)
                                pts.Add(new Point(pt.X, pt.Y));
                            _resizeStrokeSnapshots.Add((s, pts, s.ClipRect?.Clone()));
                        }
                    }
                    input.Pointer.Capture(ctx.CaptureTarget);
                }
                else
                {
                    ctx.IsPointerPressedOnCanvas = false;
                }
            }
            else
            {
                ctx.IsPointerPressedOnCanvas = false;
            }
            input.RawEvent.Handled = true;
        }

        public override void OnPointerMoved(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            if (!_isResizingSelection)
                return;

            Rect nextRect = ComputeResizedRect(_resizeOriginRect, _activeResizeHandle, input.CanvasPosition, minSize: 5.0);
            if (Math.Abs(nextRect.X - _resizeOriginRect.X) > 0.05 ||
                Math.Abs(nextRect.Y - _resizeOriginRect.Y) > 0.05 ||
                Math.Abs(nextRect.Width - _resizeOriginRect.Width) > 0.05 ||
                Math.Abs(nextRect.Height - _resizeOriginRect.Height) > 0.05)
            {
                if (_beforeDoc is null)
                {
                    _beforeDoc = ctx.Document.Clone();
                    _beforeSelection = _resizeOriginRect;
                }

                ctx.SetSelectionArea(nextRect);
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

                ctx.InvalidateVisual();
            }
            ctx.LastPointerScreenPos = input.ScreenPosition;
            input.RawEvent.Handled = true;
        }

        public override void OnPointerReleased(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            FinishInteraction(ctx, emitEvents: true);
            input.Pointer.Capture(null);
            input.RawEvent.Handled = true;
        }

        public override void OnPointerCaptureLost(ICanvasToolContext ctx)
            => FinishInteraction(ctx, emitEvents: true);

        public override void Cancel(ICanvasToolContext ctx)
            => FinishInteraction(ctx, emitEvents: false);

        private void FinishInteraction(ICanvasToolContext ctx, bool emitEvents)
        {
            bool changed = _beforeDoc is not null;
            if (changed && emitEvents)
            {
                ctx.RecordUndoCommand(new TransformStrokesCommand(
                    "Resize Selection",
                    _beforeDoc!,
                    ctx.Document.Clone(),
                    _beforeSelection,
                    ctx.SelectionArea));
            }
            _isResizingSelection = false;
            _resizeStrokeSnapshots.Clear();
            _activeResizeHandle = SelectionResizeHandle.None;
            _beforeDoc = null;
            _beforeSelection = null;
            if (changed && emitEvents)
            {
                ctx.NotifyDocumentChanged();
                ctx.NotifySelectionChanged();
            }
        }

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
    }
}
