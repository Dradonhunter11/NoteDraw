using System;
using System.Collections.Generic;
using Avalonia;

namespace NoteDraw.Drawing.Commands
{
    /// <summary>
    /// Undoable command representing an atomic mutation of a <see cref="DrawingCanvasControl"/> document and optional selection.
    /// </summary>
    public interface ICanvasCommand
    {
        string Description { get; }
        DrawingDocument BeforeDocument { get; }
        DrawingDocument AfterDocument { get; }
        Rect? BeforeSelection { get; }
        Rect? AfterSelection { get; }

        void Execute(DrawingCanvasControl canvas);
        void Undo(DrawingCanvasControl canvas);
    }

    public abstract class CanvasSnapshotCommandBase : ICanvasCommand
    {
        protected CanvasSnapshotCommandBase(
            string description,
            DrawingDocument beforeDocument,
            DrawingDocument afterDocument,
            Rect? beforeSelection = null,
            Rect? afterSelection = null)
        {
            Description = description;
            BeforeDocument = beforeDocument.Clone();
            AfterDocument = afterDocument.Clone();
            BeforeSelection = beforeSelection;
            AfterSelection = afterSelection;
        }

        public string Description { get; }
        public DrawingDocument BeforeDocument { get; }
        public DrawingDocument AfterDocument { get; }
        public Rect? BeforeSelection { get; }
        public Rect? AfterSelection { get; }

        public virtual void Execute(DrawingCanvasControl canvas)
            => canvas.ApplyCommandState(AfterDocument.Clone(), AfterSelection);

        public virtual void Undo(DrawingCanvasControl canvas)
            => canvas.ApplyCommandState(BeforeDocument.Clone(), BeforeSelection);
    }

    public sealed class AddStrokeCommand : CanvasSnapshotCommandBase
    {
        public AddStrokeCommand(
            DrawingToolKind tool,
            DrawingDocument beforeDocument,
            DrawingDocument afterDocument,
            Rect? selection = null)
            : base($"Draw {tool}", beforeDocument, afterDocument, selection, selection)
        {
        }
    }

    public sealed class TransformStrokesCommand : CanvasSnapshotCommandBase
    {
        public TransformStrokesCommand(
            string actionName,
            DrawingDocument beforeDocument,
            DrawingDocument afterDocument,
            Rect? beforeSelection = null,
            Rect? afterSelection = null)
            : base(actionName, beforeDocument, afterDocument, beforeSelection, afterSelection)
        {
        }
    }

    public sealed class ClearCanvasCommand : CanvasSnapshotCommandBase
    {
        public ClearCanvasCommand(
            DrawingDocument beforeDocument,
            DrawingDocument afterDocument,
            Rect? selection = null)
            : base("Clear Canvas", beforeDocument, afterDocument, selection, selection)
        {
        }
    }

    public sealed class ChangeBackgroundCommand : CanvasSnapshotCommandBase
    {
        public ChangeBackgroundCommand(
            CanvasBackgroundKind background,
            DrawingDocument beforeDocument,
            DrawingDocument afterDocument,
            Rect? selection = null)
            : base($"Change Background to {background}", beforeDocument, afterDocument, selection, selection)
        {
        }
    }

    public sealed class ResizeCanvasCommand : CanvasSnapshotCommandBase
    {
        public ResizeCanvasCommand(
            string description,
            DrawingDocument beforeDocument,
            DrawingDocument afterDocument,
            Rect? beforeSelection = null,
            Rect? afterSelection = null)
            : base(description, beforeDocument, afterDocument, beforeSelection, afterSelection)
        {
        }
    }

    public sealed class AddTextLabelCommand : CanvasSnapshotCommandBase
    {
        public AddTextLabelCommand(
            DrawingDocument beforeDocument,
            DrawingDocument afterDocument,
            Rect? selection = null)
            : base("Add Text Label", beforeDocument, afterDocument, selection, selection)
        {
        }
    }

    public sealed class PasteImageCommand : CanvasSnapshotCommandBase
    {
        public PasteImageCommand(
            DrawingDocument beforeDocument,
            DrawingDocument afterDocument,
            Rect? selection = null)
            : base("Paste Image", beforeDocument, afterDocument, selection, selection)
        {
        }
    }

    public sealed class RestoredSnapshotCommand : CanvasSnapshotCommandBase
    {
        public RestoredSnapshotCommand(
            DrawingDocument beforeDocument,
            DrawingDocument afterDocument,
            Rect? beforeSelection = null,
            Rect? afterSelection = null)
            : base("Restored Snapshot", beforeDocument, afterDocument, beforeSelection, afterSelection)
        {
        }
    }

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
            var nextAfter = currentDocument.Clone();
            for (int i = 0; i < undo.Count; i++)
            {
                // undo[0] is the most recent undo snapshot (top of stack).
                // Push bottom-to-top below, so collect pairs first.
            }

            // undo is exported top-to-bottom from Stack enumeration:
            // undo[0] restores to immediate previous state; its Execute state is currentDocument,
            // undo[1]'s Execute state is undo[0], etc.
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

            // redo is exported top-to-bottom: redo[0] reapplies the next undone state.
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
