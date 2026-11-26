using System.Collections.Generic;
using Avalonia.Input;

namespace NoteDraw.Drawing.Tools
{
    public sealed class PipetteTool : DrawingToolBase
    {
        public override DrawingToolKind Kind => DrawingToolKind.Pipette;
        public override string DisplayName => "Pipette";
        public override string Tooltip => "Pipette Color Picker (Shortcut: I, K, or 4)";
        public override string IconPathData => "M20.71 5.63L18.37 3.29C17.98 2.9 17.35 2.9 16.96 3.29L13.84 6.41L11.91 4.48L10.5 5.89L11.92 7.31L3 16.23V21H7.77L16.69 12.08L18.11 13.5L19.52 12.09L17.59 10.16L20.71 7.04C21.1 6.65 21.1 6.02 20.71 5.63ZM6.94 19H5V17.06L13.33 8.73L15.27 10.67L6.94 19Z";
        public override DrawingToolGroup Group => DrawingToolGroup.DrawAndColor;
        public override IReadOnlyList<Key> ShortcutKeys { get; } = new[] { Key.I, Key.K, Key.D4, Key.NumPad4 };

        public override void OnPointerPressed(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            ctx.PickColorAtCanvasPoint(input.CanvasPosition);
            ctx.IsPointerPressedOnCanvas = false;
            input.RawEvent.Handled = true;
        }
    }
}
