using System;
using System.Collections.Generic;

namespace NoteDraw.Services
{
    public sealed record TextUndoEntry(string Text, int CaretIndex);

    /// <summary>
    /// Multi-character, burst-coalescing Undo/Redo manager for the Markdown/LaTeX text editor.
    /// Coalesces consecutive character insertions or deletions into multi-character chunks so
    /// Undo/Redo reverts words/typing bursts rather than 1 character at a time.
    /// </summary>
    public sealed class TextUndoManager
    {
        private enum EditOpKind
        {
            None,
            InsertChar,
            DeleteChar,
            Bulk
        }

        private const double CoalescePauseMs = 1200.0;
        private const int MaxUndoSteps = 200;

        private readonly List<TextUndoEntry> _undoStack = new();
        private readonly Stack<TextUndoEntry> _redoStack = new();

        private string _committedText;
        private int _committedCaret;
        private string _currentText;
        private int _currentCaret;
        private EditOpKind _activeOp = EditOpKind.None;
        private int _charsInActiveGroup;
        private DateTime _lastEditUtc = DateTime.MinValue;

        public event EventHandler? HistoryChanged;

        public TextUndoManager(string? initialText = null, int initialCaret = 0)
        {
            _committedText = initialText ?? string.Empty;
            _committedCaret = Math.Clamp(initialCaret, 0, _committedText.Length);
            _currentText = _committedText;
            _currentCaret = _committedCaret;
        }

        public bool CanUndo => _undoStack.Count > 0 || !string.Equals(_committedText, _currentText, StringComparison.Ordinal);
        public bool CanRedo => _redoStack.Count > 0;
        public int UndoCount => _undoStack.Count + (!string.Equals(_committedText, _currentText, StringComparison.Ordinal) ? 1 : 0);
        public int RedoCount => _redoStack.Count;

        /// <summary>
        /// Resets undo/redo history (for example when loading a file from disk).
        /// </summary>
        public void Reset(string? text, int caretIndex = 0)
        {
            _undoStack.Clear();
            _redoStack.Clear();
            _committedText = text ?? string.Empty;
            _committedCaret = Math.Clamp(caretIndex, 0, _committedText.Length);
            _currentText = _committedText;
            _currentCaret = _committedCaret;
            _activeOp = EditOpKind.None;
            _charsInActiveGroup = 0;
            _lastEditUtc = DateTime.MinValue;
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Records a text edit, automatically coalescing sequential character insertions or deletions.
        /// </summary>
        public void RecordEdit(
            string previousText,
            int previousCaret,
            string newText,
            int newCaret,
            DateTime? timestampUtc = null,
            bool isAtomic = false)
        {
            previousText ??= string.Empty;
            newText ??= string.Empty;
            if (string.Equals(previousText, newText, StringComparison.Ordinal))
                return;

            DateTime now = timestampUtc ?? DateTime.UtcNow;
            _redoStack.Clear();

            EditOpKind op = DetermineOp(previousText, newText, isAtomic);
            bool hasUncommitted = !string.Equals(_committedText, _currentText, StringComparison.Ordinal);

            if (!hasUncommitted)
            {
                _committedText = previousText;
                _committedCaret = Math.Clamp(previousCaret, 0, previousText.Length);
                _currentText = newText;
                _currentCaret = Math.Clamp(newCaret, 0, newText.Length);
                _activeOp = op;
                _charsInActiveGroup = 1;
                _lastEditUtc = now;
                HistoryChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            bool shouldSealPrevious = false;
            if (op == EditOpKind.Bulk || _activeOp == EditOpKind.Bulk)
            {
                shouldSealPrevious = true;
            }
            else if (op != _activeOp)
            {
                shouldSealPrevious = true;
            }
            else if (Math.Abs(previousCaret - _currentCaret) > 1)
            {
                shouldSealPrevious = true;
            }
            else if (_charsInActiveGroup >= 2 && (now - _lastEditUtc).TotalMilliseconds >= CoalescePauseMs)
            {
                shouldSealPrevious = true;
            }
            else if (_charsInActiveGroup >= 2 && IsNewlineBoundary(previousText, newText, op))
            {
                shouldSealPrevious = true;
            }

            if (shouldSealPrevious)
            {
                PushUndoEntry(new TextUndoEntry(_committedText, _committedCaret));
                _committedText = previousText;
                _committedCaret = Math.Clamp(previousCaret, 0, previousText.Length);
                _charsInActiveGroup = 1;
            }
            else
            {
                _charsInActiveGroup++;
            }

            _activeOp = op;
            _currentText = newText;
            _currentCaret = Math.Clamp(newCaret, 0, newText.Length);
            _lastEditUtc = now;
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool Undo(string currentText, int currentCaret, out TextUndoEntry restored)
        {
            currentText ??= string.Empty;
            currentCaret = Math.Clamp(currentCaret, 0, currentText.Length);

            if (!string.Equals(_committedText, currentText, StringComparison.Ordinal))
            {
                _redoStack.Push(new TextUndoEntry(currentText, currentCaret));
                restored = new TextUndoEntry(_committedText, _committedCaret);
                _currentText = _committedText;
                _currentCaret = _committedCaret;
                _activeOp = EditOpKind.None;
                _charsInActiveGroup = 0;
                HistoryChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }

            if (_undoStack.Count == 0)
            {
                restored = new TextUndoEntry(currentText, currentCaret);
                return false;
            }

            _redoStack.Push(new TextUndoEntry(currentText, currentCaret));
            restored = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);

            _committedText = restored.Text;
            _committedCaret = restored.CaretIndex;
            _currentText = restored.Text;
            _currentCaret = restored.CaretIndex;
            _activeOp = EditOpKind.None;
            _charsInActiveGroup = 0;
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool Redo(string currentText, int currentCaret, out TextUndoEntry restored)
        {
            currentText ??= string.Empty;
            currentCaret = Math.Clamp(currentCaret, 0, currentText.Length);

            if (_redoStack.Count == 0)
            {
                restored = new TextUndoEntry(currentText, currentCaret);
                return false;
            }

            if (!string.Equals(_committedText, currentText, StringComparison.Ordinal))
            {
                PushUndoEntry(new TextUndoEntry(_committedText, _committedCaret));
            }
            PushUndoEntry(new TextUndoEntry(currentText, currentCaret));

            restored = _redoStack.Pop();
            _committedText = restored.Text;
            _committedCaret = restored.CaretIndex;
            _currentText = restored.Text;
            _currentCaret = restored.CaretIndex;
            _activeOp = EditOpKind.None;
            _charsInActiveGroup = 0;
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private void PushUndoEntry(TextUndoEntry entry)
        {
            if (_undoStack.Count >= MaxUndoSteps)
                _undoStack.RemoveAt(0);
            _undoStack.Add(entry);
        }

        private static EditOpKind DetermineOp(string previousText, string newText, bool isAtomic)
        {
            if (isAtomic)
                return EditOpKind.Bulk;

            if (newText.Length == previousText.Length + 1)
                return EditOpKind.InsertChar;

            if (newText.Length == previousText.Length - 1)
                return EditOpKind.DeleteChar;

            return EditOpKind.Bulk;
        }

        private static bool IsNewlineBoundary(string previousText, string newText, EditOpKind op)
        {
            if (op == EditOpKind.InsertChar)
            {
                for (int i = 0; i < previousText.Length; i++)
                {
                    if (previousText[i] != newText[i])
                        return newText[i] == '\n';
                }
                return newText.Length > 0 && newText[^1] == '\n';
            }

            if (op == EditOpKind.DeleteChar)
            {
                for (int i = 0; i < newText.Length; i++)
                {
                    if (previousText[i] != newText[i])
                        return previousText[i] == '\n';
                }
                return previousText.Length > 0 && previousText[^1] == '\n';
            }

            return false;
        }
    }
}
