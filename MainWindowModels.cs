using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using NoteDraw.Drawing;
using NoteDraw.Rendering;
using NoteDraw.Rendering.Latex;
using NoteDraw.Rendering.Markdown;
using NoteDraw.Services;

namespace NoteDraw
{
    internal sealed class OpenDocumentTab
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title => Path.GetFileName(FilePath);
        public bool IsDrawing => FilePath.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase);
        public bool IsImage => MainWindow.IsSupportedImageExtension(FilePath);
        public string Content { get; set; } = string.Empty;
        public bool IsDirty { get; set; }
        public int CaretIndex { get; set; }
        public string? SourceMarkdownFilePath { get; set; }
        public int? SourceInlineFenceLineZeroBased { get; set; }
        public TextUndoManager TextUndo { get; } = new();
    }

    internal enum RibbonTabKind
    {
        DrawingTools,
        Git
    }

    public enum UnsavedExitDecision
    {
        Save,
        Discard,
        Cancel
    }

    internal static class ControlExtensions
    {
        public static T Also<T>(this T obj, Action<T> action)
        {
            action(obj);
            return obj;
        }
    }
}
