using System.Collections.Generic;
using Avalonia.Input;

namespace NoteDraw.Drawing.Tools
{
    public sealed class TextLabelTool : DrawingToolBase
    {
        public override DrawingToolKind Kind => DrawingToolKind.TextLabel;
        public override string DisplayName => "Text / $LaTeX$";
        public override string Tooltip => "Text / $LaTeX$ (Shortcut: T or 9)";
        public override string CanvasTooltip => "Click anywhere on the canvas to place a Text or $LaTeX$ label";
        public override string IconPathData => "M5 4V7H10.5V19H13.5V7H19V4H5Z";
        public override DrawingToolGroup Group => DrawingToolGroup.Annotation;
        public override IReadOnlyList<Key> ShortcutKeys { get; } = new[] { Key.T, Key.D9, Key.NumPad9 };

        public override void OnPointerPressed(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            if (ctx.SelectionArea is { } sel && !sel.Contains(input.CanvasPosition))
            {
                ctx.IsPointerPressedOnCanvas = false;
                input.RawEvent.Handled = true;
                return;
            }

            ctx.RequestTextLabelPlacement(input.CanvasPosition);
            ctx.IsPointerPressedOnCanvas = false;
            input.RawEvent.Handled = true;
        }
    }
}
