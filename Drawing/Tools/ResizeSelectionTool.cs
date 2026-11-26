using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using NoteDraw.Drawing.Commands;

namespace NoteDraw.Drawing.Tools
{
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
