using System;
using System.Collections.Generic;

namespace NoteDraw.Drawing.Commands
{
    /// <summary>
    /// Undo/Redo command stack backing <see cref="DrawingCanvasControl"/>.
    /// </summary>
    public sealed class CanvasCommandHistory
    {
        private readonly Stack<ICanvasCommand> _undoStack = new();
        private readonly Stack<ICanvasCommand> _redoStack = new();

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;

        public void RecordExecuted(ICanvasCommand command)
        {
            if (command is null) throw new ArgumentNullException(nameof(command));
            _undoStack.Push(command);
            _redoStack.Clear();
        }

        public void Execute(ICanvasCommand command, DrawingCanvasControl canvas)
        {
            if (command is null) throw new ArgumentNullException(nameof(command));
            if (canvas is null) throw new ArgumentNullException(nameof(canvas));
            command.Execute(canvas);
            _undoStack.Push(command);
            _redoStack.Clear();
        }

        public bool Undo(DrawingCanvasControl canvas)
        {
            if (_undoStack.Count == 0)
                return false;

            var cmd = _undoStack.Pop();
            cmd.Undo(canvas);
            _redoStack.Push(cmd);
            return true;
        }

        public bool Redo(DrawingCanvasControl canvas)
        {
            if (_redoStack.Count == 0)
                return false;

            var cmd = _redoStack.Pop();
            cmd.Execute(canvas);
            _undoStack.Push(cmd);
            return true;
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }

        public (List<DrawingDocument> Undo, List<DrawingDocument> Redo) ExportSnapshots()
        {
            var undoList = new List<DrawingDocument>(_undoStack.Count);
            foreach (var cmd in _undoStack)
                undoList.Add(cmd.BeforeDocument.Clone());

            var redoList = new List<DrawingDocument>(_redoStack.Count);
            foreach (var cmd in _redoStack)
                redoList.Add(cmd.AfterDocument.Clone());

            return (undoList, redoList);
        }

        public void RestoreSnapshots(
            IReadOnlyList<DrawingDocument> undo,
            IReadOnlyList<DrawingDocument> redo,
            DrawingDocument currentDocument)
        {
            _undoStack.Clear();
            _redoStack.Clear();

            var undoCommands = new List<ICanvasCommand>(undo.Count);
            DrawingDocument forwardTarget = currentDocument.Clone();
            for (int i = 0; i < undo.Count; i++)
            {
                var beforeSnap = undo[i].Clone();
                undoCommands.Add(new RestoredSnapshotCommand(beforeSnap, forwardTarget));
                forwardTarget = beforeSnap;
            }
            for (int i = undoCommands.Count - 1; i >= 0; i--)
                _undoStack.Push(undoCommands[i]);

            var redoCommands = new List<ICanvasCommand>(redo.Count);
            DrawingDocument backSource = currentDocument.Clone();
            for (int i = 0; i < redo.Count; i++)
            {
                var afterSnap = redo[i].Clone();
                redoCommands.Add(new RestoredSnapshotCommand(backSource, afterSnap));
                backSource = afterSnap;
            }
            for (int i = redoCommands.Count - 1; i >= 0; i--)
                _redoStack.Push(redoCommands[i]);
        }
    }
}
