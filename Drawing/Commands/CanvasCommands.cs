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
}
