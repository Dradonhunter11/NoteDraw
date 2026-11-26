using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace NoteDraw.Drawing.Tools
{
    public abstract class FreehandToolBase : DrawingToolBase
    {
        public sealed override DrawingToolGroup Group => DrawingToolGroup.DrawAndColor;

        public override bool IsActiveInteraction => true;

        protected abstract DrawingStroke CreateInitialStroke(ICanvasToolContext ctx, CanvasPointerInput input, DrawingRect? clip);

        public override void OnPointerPressed(ICanvasToolContext ctx, CanvasPointerInput input)
        {
            if (ctx.SelectionArea is { } sel && !sel.Contains(input.CanvasPosition))
            {
                ctx.IsPointerPressedOnCanvas = false;
                input.RawEvent.Handled = true;
                return;
            }

            ctx.PreStrokeSnapshot = ctx.Document.Clone();
            ctx.ActiveStroke = CreateInitialStroke(ctx, input, ActiveClipRect(ctx));
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

            bool addedAny = false;
            var intermediate = input.RawEvent.GetIntermediatePoints(ctx.CaptureTarget);
            if (intermediate is { Count: > 0 })
            {
                foreach (var sample in intermediate)
                {
                    var sampleCanvas = ctx.ScreenToCanvas(sample.Position);
                    double samplePressure = sample.Properties.Pressure > 0 ? sample.Properties.Pressure : input.Pressure;
                    var last = ctx.ActiveStroke.Points[^1];
                    double distSq = Math.Pow(sampleCanvas.X - last.X, 2) + Math.Pow(sampleCanvas.Y - last.Y, 2);
                    if (distSq >= 2.25)
                    {
                        ctx.ActiveStroke.Points.Add(new DrawingPoint(sampleCanvas.X, sampleCanvas.Y, samplePressure));
                        addedAny = true;
                    }
                }
            }

            var finalLast = ctx.ActiveStroke.Points[^1];
            double finalDistSq = Math.Pow(input.CanvasPosition.X - finalLast.X, 2) + Math.Pow(input.CanvasPosition.Y - finalLast.Y, 2);
            if (finalDistSq >= 2.25)
            {
                ctx.ActiveStroke.Points.Add(new DrawingPoint(input.CanvasPosition.X, input.CanvasPosition.Y, input.Pressure));
                addedAny = true;
            }

            if (addedAny)
                ctx.InvalidateVisual();

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

    public sealed class PenTool : FreehandToolBase
    {
        public override DrawingToolKind Kind => DrawingToolKind.Pen;
        public override string DisplayName => "Pen";
        public override string Tooltip => "Pen (Shortcut: P or 1)";
        public override string IconPathData => "M3 17.25V21H6.75L17.81 9.94L14.06 6.19L3 17.25ZM20.71 7.04C21.1 6.65 21.1 6.02 20.71 5.63L18.37 3.29C17.98 2.9 17.35 2.9 16.96 3.29L15.13 5.12L18.88 8.87L20.71 7.04Z";
        public override IReadOnlyList<Key> ShortcutKeys { get; } = new[] { Key.P, Key.D1, Key.NumPad1 };

        protected override DrawingStroke CreateInitialStroke(ICanvasToolContext ctx, CanvasPointerInput input, DrawingRect? clip) => new()
        {
            Tool = DrawingToolKind.Pen,
            ColorHex = ctx.ActiveColorHex,
            Thickness = ctx.ActiveThickness,
            Opacity = 1.0,
            ClipRect = clip,
            Points = new List<DrawingPoint> { new(input.CanvasPosition.X, input.CanvasPosition.Y, input.Pressure) }
        };
    }

    public sealed class HighlighterTool : FreehandToolBase
    {
        public override DrawingToolKind Kind => DrawingToolKind.Highlighter;
        public override string DisplayName => "Highlight";
        public override string Tooltip => "Highlight (Shortcut: H or 2)";
        public override string IconPathData => "M6 14L15 5L19 9L10 18H6V14ZM16.4 3.6L18.3 1.7C19.1 0.9 20.4 0.9 21.2 1.7L22.3 2.8C23.1 3.6 23.1 4.9 22.3 5.7L20.4 7.6L16.4 3.6ZM2 20H14V22H2V20Z";
        public override IReadOnlyList<Key> ShortcutKeys { get; } = new[] { Key.H, Key.D2, Key.NumPad2 };

        protected override DrawingStroke CreateInitialStroke(ICanvasToolContext ctx, CanvasPointerInput input, DrawingRect? clip) => new()
        {
            Tool = DrawingToolKind.Highlighter,
            ColorHex = ctx.ActiveColorHex == "AUTO" ? "#FACC15" : ctx.ActiveColorHex,
            Thickness = Math.Max(12.0, ctx.ActiveThickness * 3.0),
            Opacity = 0.36,
            ClipRect = clip,
            Points = new List<DrawingPoint> { new(input.CanvasPosition.X, input.CanvasPosition.Y, input.Pressure) }
        };
    }

    public sealed class EraserTool : FreehandToolBase
    {
        public override DrawingToolKind Kind => DrawingToolKind.Eraser;
        public override string DisplayName => "Eraser";
        public override string Tooltip => "Eraser (Shortcut: E or 3)";
        public override string IconPathData => "M16.24 3.56L21.19 8.51C21.97 9.29 21.97 10.56 21.19 11.34L12.71 19.82C12.33 20.2 11.82 20.41 11.29 20.41H6.17C5.64 20.41 5.13 20.2 4.76 19.82L2.81 17.87C2.03 17.09 2.03 15.82 2.81 15.04L13.41 4.44C14.19 3.66 15.46 3.27 16.24 3.56ZM4.22 16.46L6.17 18.41H11.29L14.12 15.58L9.88 11.34L4.22 16.46ZM12.71 20.41H21V22H11.5L12.71 20.41Z";
        public override IReadOnlyList<Key> ShortcutKeys { get; } = new[] { Key.E, Key.D3, Key.NumPad3 };

        protected override DrawingStroke CreateInitialStroke(ICanvasToolContext ctx, CanvasPointerInput input, DrawingRect? clip) => new()
        {
            Tool = DrawingToolKind.Eraser,
            ColorHex = "#FFFFFF",
            Thickness = Math.Max(6.0, ctx.ActiveThickness * 2.0),
            Opacity = 1.0,
            ClipRect = clip,
            Points = new List<DrawingPoint> { new(input.CanvasPosition.X, input.CanvasPosition.Y, input.Pressure) }
        };
    }
}
