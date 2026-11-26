using System;
using System.Windows.Input;
using NoteDraw.Drawing.Tools;

namespace NoteDraw.Drawing.Commands
{
    /// <summary>
    /// Command Pattern action that activates an <see cref="IDrawingTool"/> on a <see cref="DrawingCanvasControl"/>
    /// and updates associated UI state and canvas tooltips.
    /// </summary>
    public sealed class SelectDrawingToolCommand : ICommand
    {
        private readonly DrawingCanvasControl _canvas;
        private readonly Action<IDrawingTool>? _afterExecute;

        public SelectDrawingToolCommand(
            DrawingCanvasControl canvas,
            IDrawingTool tool,
            Action<IDrawingTool>? afterExecute = null)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            Tool = tool ?? throw new ArgumentNullException(nameof(tool));
            _afterExecute = afterExecute;
        }

        public IDrawingTool Tool { get; }

        public DrawingToolKind ToolKind => Tool.Kind;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            _canvas.ActiveTool = Tool.Kind;
            Avalonia.Controls.ToolTip.SetTip(_canvas, Tool.CanvasTooltip);
            _afterExecute?.Invoke(Tool);
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
