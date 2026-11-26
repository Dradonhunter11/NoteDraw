using System.Collections.Generic;
using Avalonia.Input;

namespace NoteDraw.Drawing.Tools
{
    public abstract class ShapeToolBase : DrawingToolBase
    {
        public sealed override DrawingToolGroup Group => DrawingToolGroup.Shapes;
        public override bool IsActiveInteraction => true;

        public override void OnPointerPressed(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            if (ctx.SelectionArea is { } sel && !sel.Contains(input.CanvasPosition))
            {
                ctx.IsPointerPressedOnCanvas = false;
                input.RawEvent.Handled = true;
                return;
            }

            ctx.PreStrokeSnapshot = ctx.Document.Clone();
            ctx.ActiveStroke = new DrawingStroke
            {
                Tool = Kind,
                ColorHex = ctx.ActiveColorHex,
                Thickness = ctx.ActiveThickness,
                Opacity = 1.0,
                ClipRect = ActiveClipRect(ctx),
                Points = new List<DrawingPoint>
                {
                    new(input.CanvasPosition.X, input.CanvasPosition.Y, input.Pressure),
                    new(input.CanvasPosition.X, input.CanvasPosition.Y, input.Pressure)
                }
            };
            input.Pointer.Capture(ctx.CaptureTarget);
            ctx.InvalidateVisual();
            input.RawEvent.Handled = true;
        }

        public override void OnPointerMoved(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            if (ctx.ActiveStroke is null)
                return;

            var ptInfo = input.RawEvent.GetCurrentPoint(ctx.CaptureTarget);
            if (input.Pointer.Type == PointerType.Mouse &&
                !ptInfo.Properties.IsLeftButtonPressed &&
                !ptInfo.Properties.IsEraser)
            {
                ctx.IsPointerPressedOnCanvas = false;
                ctx.CommitActiveStroke();
                input.Pointer.Capture(null);
                input.RawEvent.Handled = true;
                return;
            }

            if (ctx.ActiveStroke.Points.Count >= 2)
            {
                ctx.ActiveStroke.Points[1] = new DrawingPoint(input.CanvasPosition.X, input.CanvasPosition.Y, input.Pressure);
                ctx.InvalidateVisual();
            }
            input.RawEvent.Handled = true;
        }

        public override void OnPointerReleased(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            if (ctx.CommitActiveStroke())
            {
                input.Pointer.Capture(null);
                input.RawEvent.Handled = true;
            }
        }

        public override void OnPointerCaptureLost(ICanvasToolContext ctx)
        {
            ctx.CommitActiveStroke();
        }
    }

    public sealed class LineTool : ShapeToolBase
    {
        public override DrawingToolKind Kind => DrawingToolKind.Line;
        public override string DisplayName => "Line";
        public override string Tooltip => "Line (Shortcut: L or 5)";
        public override string IconPathData => "M4 20L20 4M4 20H4.01M20 4H20.01M3.29 19.29L19.29 3.29L20.71 4.71L4.71 20.71Z";
        public override IReadOnlyList<Key> ShortcutKeys { get; } = new[] { Key.L, Key.D5, Key.NumPad5 };
    }

    public sealed class ArrowTool : ShapeToolBase
    {
        public override DrawingToolKind Kind => DrawingToolKind.Arrow;
        public override string DisplayName => "Arrow";
        public override string Tooltip => "Arrow (Shortcut: A or 6)";
        public override string IconPathData => "M5 17.59L15.59 7H9V5H19V15H17V8.41L6.41 19L5 17.59Z";
        public override IReadOnlyList<Key> ShortcutKeys { get; } = new[] { Key.A, Key.D6, Key.NumPad6 };
    }

    public sealed class RectangleTool : ShapeToolBase
    {
        public override DrawingToolKind Kind => DrawingToolKind.Rectangle;
        public override string DisplayName => "Rect";
        public override string Tooltip => "Rectangle (Shortcut: R or 7)";
        public override string IconPathData => "M3 5C3 3.9 3.9 3 5 3H19C20.1 3 21 3.9 21 5V19C21 20.1 20.1 21 19 21H5C3.9 21 3 20.1 3 19V5ZM5 5V19H19V5H5Z";
        public override IReadOnlyList<Key> ShortcutKeys { get; } = new[] { Key.R, Key.D7, Key.NumPad7 };
    }

    public sealed class EllipseTool : ShapeToolBase
    {
        public override DrawingToolKind Kind => DrawingToolKind.Ellipse;
        public override string DisplayName => "Ellipse";
        public override string Tooltip => "Ellipse (Shortcut: O, C, or 8)";
        public override string IconPathData => "M12 4C6.48 4 2 7.58 2 12C2 16.42 6.48 20 12 20C17.52 20 22 16.42 22 12C22 7.58 17.52 4 12 4ZM12 18C7.58 18 4 15.31 4 12C4 8.69 7.58 6 12 6C16.42 6 20 8.69 20 12C20 15.31 16.42 18 12 18Z";
        public override IReadOnlyList<Key> ShortcutKeys { get; } = new[] { Key.O, Key.C, Key.D8, Key.NumPad8 };
    }
}
