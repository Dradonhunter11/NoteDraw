using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using NoteDraw.Drawing.Commands;

namespace NoteDraw.Drawing.Tools
{
    public enum DrawingToolGroup
    {
        DrawAndColor,
        Shapes,
        Annotation,
        NavigationAndSelection
    }

    public readonly record struct CanvasPointerInput(
        Point ScreenPosition,
        Point CanvasPosition,
        double Pressure,
        int ClickCount,
        IPointer Pointer,
        PointerEventArgs RawEvent);

    /// <summary>
    /// Host contract passed to modular <see cref="IDrawingTool"/> implementations by <see cref="DrawingCanvasControl"/>.
    /// </summary>
    public interface ICanvasToolContext
    {
        DrawingDocument Document { get; }
        string ActiveColorHex { get; }
        double ActiveThickness { get; }
        double Zoom { get; }
        Point PanOffset { get; }
        Rect? SelectionArea { get; }

        DrawingStroke? ActiveStroke { get; set; }
        DrawingDocument? PreStrokeSnapshot { get; set; }
        Rect? DraftSelectionArea { get; set; }
        bool IsPointerPressedOnCanvas { get; set; }
        Point LastPointerScreenPos { get; set; }
        Control CaptureTarget { get; }

        Point ScreenToCanvas(Point screenPos);
        DrawingStroke? HitTestStroke(Point pt, double tolerance, bool includeImages = true);
        bool StrokeIntersectsRect(DrawingStroke stroke, Rect rect);
        void TranslateStroke(DrawingStroke stroke, double dx, double dy);

        string PickColorAtCanvasPoint(Point canvasPos);
        void RequestTextLabelPlacement(Point canvasPos);

        void SetSelectionArea(Rect? area);
        void ClearSelectionArea();
        void SetPanOffset(Point offset);
        void RecordUndoCommand(ICanvasCommand command);
        bool CommitActiveStroke();

        void InvalidateVisual();
        void NotifyDocumentChanged();
        void NotifySelectionChanged();
        void NotifyViewportChanged();
    }

    /// <summary>
    /// Strategy + metadata contract for a single modular vector canvas tool.
    /// </summary>
    public interface IDrawingTool
    {
        DrawingToolKind Kind { get; }
        string DisplayName { get; }
        string Tooltip { get; }
        string? CanvasTooltip { get; }
        string IconPathData { get; }
        DrawingToolGroup Group { get; }
        IReadOnlyList<Key> ShortcutKeys { get; }
        bool IsActiveInteraction { get; }

        void OnPointerPressed(ICanvasToolContext ctx, CanvasPointerInput input);
        void OnPointerMoved(ICanvasToolContext ctx, CanvasPointerInput input);
        void OnPointerReleased(ICanvasToolContext ctx, CanvasPointerInput input);
        void OnPointerCaptureLost(ICanvasToolContext ctx);
        void Cancel(ICanvasToolContext ctx);
    }

    public abstract class DrawingToolBase : IDrawingTool
    {
        public abstract DrawingToolKind Kind { get; }
        public abstract string DisplayName { get; }
        public abstract string Tooltip { get; }
        public virtual string? CanvasTooltip => null;
        public abstract string IconPathData { get; }
        public abstract DrawingToolGroup Group { get; }
        public abstract IReadOnlyList<Key> ShortcutKeys { get; }
        public virtual bool IsActiveInteraction => false;

        public abstract void OnPointerPressed(ICanvasToolContext ctx, CanvasPointerInput input);

        public virtual void OnPointerMoved(ICanvasToolContext ctx, CanvasPointerInput input) { }

        public virtual void OnPointerReleased(ICanvasToolContext ctx, CanvasPointerInput input) { }

        public virtual void OnPointerCaptureLost(ICanvasToolContext ctx) { }

        public virtual void Cancel(ICanvasToolContext ctx) { }

        protected static DrawingRect? ActiveClipRect(ICanvasToolContext ctx)
            => ctx.SelectionArea is { Width: >= 5.0, Height: >= 5.0 } selBounds
                ? new DrawingRect(selBounds.X, selBounds.Y, selBounds.Width, selBounds.Height)
                : null;
    }
}
