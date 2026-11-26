using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using NoteDraw.Drawing.Commands;

namespace NoteDraw.Drawing.Tools
{
    public sealed class SelectPanTool : DrawingToolBase
    {
        private DrawingStroke? _draggedStroke;
        private DrawingDocument? _moveBeforeDoc;
        private bool _isDraggingStroke;
        private bool _isPanning;

        public override DrawingToolKind Kind => DrawingToolKind.SelectPan;
        public override string DisplayName => "Move / Pan";
        public override string Tooltip => "Move Object / Pan (Shortcut: V or M)";
        public override string IconPathData => "M12 2L9 5H11V11H5V9L2 12L5 15V13H11V19H9L12 22L15 19H13V13H19V15L22 12L19 9V11H13V5H15L12 2Z";
        public override DrawingToolGroup Group => DrawingToolGroup.NavigationAndSelection;
        public override IReadOnlyList<Key> ShortcutKeys { get; } = new[] { Key.V, Key.M };
        public override bool IsActiveInteraction => _isDraggingStroke || _isPanning;

        public override void OnPointerPressed(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            _draggedStroke = ctx.HitTestStroke(input.CanvasPosition, tolerance: 12.0 / Math.Max(0.25, ctx.Zoom));
            if (_draggedStroke is not null)
            {
                _isDraggingStroke = true;
                _isPanning = false;
                _moveBeforeDoc = null;
            }
            else
            {
                _isDraggingStroke = false;
                _isPanning = true;
            }
            input.Pointer.Capture(ctx.CaptureTarget);
            input.RawEvent.Handled = true;
        }

        public override void OnPointerMoved(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            if (_isPanning)
            {
                var delta = input.ScreenPosition - ctx.LastPointerScreenPos;
                ctx.SetPanOffset(new Point(ctx.PanOffset.X + delta.X, ctx.PanOffset.Y + delta.Y));
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
                    _moveBeforeDoc ??= ctx.Document.Clone();
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
            bool moved = _moveBeforeDoc is not null;
            if (moved && emitEvents)
            {
                ctx.RecordUndoCommand(new TransformStrokesCommand(
                    "Move Stroke",
                    _moveBeforeDoc!,
                    ctx.Document.Clone(),
                    ctx.SelectionArea,
                    ctx.SelectionArea));
            }
            _moveBeforeDoc = null;
            _draggedStroke = null;
            _isDraggingStroke = false;
            _isPanning = false;
            if (moved && emitEvents)
                ctx.NotifyDocumentChanged();
        }
    }

    public sealed class MovePositionTool : DrawingToolBase
    {
        private bool _isPanning;

        public override DrawingToolKind Kind => DrawingToolKind.MovePosition;
        public override string DisplayName => "Move Position";
        public override string Tooltip => "Move Position / Pan Canvas (Shortcut: G)";
        public override string IconPathData => "M10 3C10.55 3 11 3.45 11 4V11H12V3.5C12 2.95 12.45 2.5 13 2.5C13.55 2.5 14 2.95 14 3.5V11H15V5C15 4.45 15.45 4 16 4C16.55 4 17 4.45 17 5V12.5H18V8C18 7.45 18.45 7 19 7C19.55 7 20 7.45 20 8V15.5C20 19.09 17.09 22 13.5 22H12C9.5 22 7.26 20.52 6.26 18.23L3.77 12.71C3.52 12.16 3.79 11.51 4.35 11.29C4.83 11.1 5.39 11.29 5.65 11.74L7.5 14.5V5.5C7.5 4.95 7.95 4.5 8.5 4.5C9.05 4.5 9.5 4.95 9.5 5.5V11H10V4C10 3.45 10 3 10 3Z";
        public override DrawingToolGroup Group => DrawingToolGroup.NavigationAndSelection;
        public override IReadOnlyList<Key> ShortcutKeys { get; } = new[] { Key.G };
        public override bool IsActiveInteraction => _isPanning;

        public override void OnPointerPressed(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            _isPanning = true;
            input.Pointer.Capture(ctx.CaptureTarget);
            input.RawEvent.Handled = true;
        }

        public override void OnPointerMoved(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            if (!_isPanning)
                return;

            var delta = input.ScreenPosition - ctx.LastPointerScreenPos;
            ctx.SetPanOffset(new Point(ctx.PanOffset.X + delta.X, ctx.PanOffset.Y + delta.Y));
            ctx.LastPointerScreenPos = input.ScreenPosition;
            input.RawEvent.Handled = true;
        }

        public override void OnPointerReleased(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            _isPanning = false;
            input.Pointer.Capture(null);
            input.RawEvent.Handled = true;
        }

        public override void OnPointerCaptureLost(ICanvasToolContext ctx) => _isPanning = false;

        public override void Cancel(ICanvasToolContext ctx) => _isPanning = false;
    }
}
