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
}
