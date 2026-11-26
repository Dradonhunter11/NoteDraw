using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdInline = Markdig.Syntax.Inlines.Inline;
using NoteDraw.Drawing;
using NoteDraw.Rendering.Latex;
using NoteDraw.Services;

namespace NoteDraw.Rendering.Markdown
{
    public sealed record InlineDrawingUpdateEventArgs(int OpenFenceLineZeroBased, string UpdatedJson);
    public sealed record ExternalDrawingUpdateEventArgs(string TargetName, string UpdatedJson);
    public sealed record TaskToggleEventArgs(int LineZeroBased, bool IsChecked);

    public sealed class OpenCanvasInTabEventArgs : EventArgs
    {
        public string? TargetName { get; }
        public int? InlineOpenFenceLineZeroBased { get; }
        public DrawingDocument Document { get; }

        public OpenCanvasInTabEventArgs(string? targetName, int? inlineOpenFenceLineZeroBased, DrawingDocument document)
        {
            TargetName = targetName;
            InlineOpenFenceLineZeroBased = inlineOpenFenceLineZeroBased;
            Document = document;
        }
    }

    /// <summary>
}
