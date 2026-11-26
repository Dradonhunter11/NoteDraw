using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using NoteDraw.Drawing.Commands;

namespace NoteDraw.Drawing.Tools
{
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
}
