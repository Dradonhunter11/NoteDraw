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

    /// <summary>
    /// Obsidian-style unified Markdown + LaTeX + Drawing live-preview text editor.
    /// Non-cursor lines render formatted Markdown/LaTeX; clicking once or navigating with arrow keys
    /// reveals raw Markdown syntax directly on the cursor line while clicking images opens the Image Editor.
    /// Maintainers: Add segment classification rules in <see cref="BuildEditorSegments"/> and block renderers in <see cref="RenderParsedBlock"/>.
    /// </summary>
    public sealed class MarkdownPreviewControl : Border
    {
        private enum EditorSegmentKind
        {
            BlankLine,
            Heading,
            SingleLineMarkdown,
            FencedLatex,
            DisplayMath,
            FencedCode,
            FencedDrawing,
            EmbeddedDrawing,
            MarkdownImage,
            Table
        }

        private sealed class EditorSegment
        {
            public int StartLine { get; set; }
            public int EndLine { get; set; }
            public EditorSegmentKind Kind { get; init; }
            public int HeadingLevel { get; init; }
            public string RawText { get; set; } = string.Empty;
        }

        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        private static readonly Regex InlineTokenRegex = new(
            @"(!?\[\[([^\]\|]+?)(?:\|([^\]]+?))?\]\]|(?<![A-Za-z0-9_/&#])#([A-Za-z][A-Za-z0-9_\-/]*))",
            RegexOptions.Compiled);

        private static readonly Regex HeadingLineRegex = new(
            @"^(#{1,6})(?:\s.*)?$",
            RegexOptions.Compiled);

        private static readonly Regex MarkdownImageLineRegex = new(
            @"^!\[([^\]]*)\]\(([^\)]*)\)$",
            RegexOptions.Compiled);

        private readonly StackPanel _rootStack;
        private readonly ScrollViewer _scrollViewer;
        private readonly Dictionary<int, (List<DrawingDocument> Undo, List<DrawingDocument> Redo)> _inlineDrawingHistories = new();
        private readonly Dictionary<string, DrawingDocument> _embeddedDrawingCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<int> _collapsedPreviewHeadingLines = new();
        private readonly HashSet<int> _openInlineImageEditors = new();
        private readonly HashSet<string> _openEmbeddedImageEditors = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _openMarkdownImageEditors = new(StringComparer.OrdinalIgnoreCase);

        private List<EditorSegment> _lastSegments = new();
        private List<EditorSegment> _visibleEditableSegments = new();
        private readonly List<(Control Visual, int StartLine, int EndLine)> _renderedSegmentVisuals = new();
        private string _markdown = string.Empty;
        private bool _isDarkTheme = true;
        private ThemePalette _palette = ThemePalette.CreateClassicDark();
        private double _baseFontSize = 14.5;
        private int _activeSegmentStartLine = -1;
        private int _pendingCaretOffset = -1;
        private TextBox? _activeSegmentTextBox;

        public StackPanel RootPanel => _rootStack;
        public DrawingCanvasControl? LastActiveInlineCanvas { get; private set; }
        public IReadOnlySet<int> CollapsedHeadingLines => _collapsedPreviewHeadingLines;
        public IReadOnlySet<int> OpenInlineImageEditors => _openInlineImageEditors;
        public int ActiveCursorLineZeroBased => _activeSegmentStartLine;
        public TextBox? ActiveEditorTextBox => _activeSegmentTextBox;

        public Func<string, DrawingDocument?>? ResolveEmbeddedDrawing { get; set; }
        public Func<string, string?>? ResolveVaultImagePath { get; set; }
        public Func<int, DrawingDocument, string>? SaveInlineCanvasToFolder { get; set; }
        public Func<DrawingDocument, (string FullPath, string FileName, string EmbedMarkdown)>? CreateSavedDocumentCanvas { get; set; }
        public Func<int, bool>? TryHandleImagePasteCallback { get; set; }

        public event EventHandler<int>? LineFocusRequested;
        public event EventHandler<string>? WikiLinkClicked;
        public event EventHandler<string>? TagClicked;
        public event EventHandler<InlineDrawingUpdateEventArgs>? InlineDrawingUpdated;
        public event EventHandler<ExternalDrawingUpdateEventArgs>? ExternalDrawingUpdated;
        public event EventHandler<TaskToggleEventArgs>? TaskItemToggled;
        public event EventHandler<DrawingCanvasControl>? InlineCanvasActivated;
        public event EventHandler<string>? DocumentEditedFromPreview;
        public event EventHandler<(int LineOneBased, int ColumnOneBased)>? CursorPositionChanged;
        public event EventHandler<Bitmap>? CopyImageRequested;
        public event EventHandler<(string ImageFilePath, int TargetLineZeroBased)>? ImageFileDroppedAtLine;

        public MarkdownPreviewControl()
        {
            _rootStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 3,
                Margin = new Thickness(24, 16, 24, 48)
            };

            _scrollViewer = new ScrollViewer
            {
                Content = _rootStack,
                Background = Brushes.Transparent
            };

            _scrollViewer.PointerPressed += OnBackgroundPointerPressed;
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnPreviewDragOver, RoutingStrategies.Tunnel);
            AddHandler(DragDrop.DropEvent, OnPreviewDrop, RoutingStrategies.Tunnel);
            Child = _scrollViewer;
        }

        public void ClearInlineDrawingHistories()
        {
            _inlineDrawingHistories.Clear();
            _embeddedDrawingCache.Clear();
            _collapsedPreviewHeadingLines.Clear();
            _openInlineImageEditors.Clear();
            _openEmbeddedImageEditors.Clear();
            _openMarkdownImageEditors.Clear();
            _activeSegmentStartLine = -1;
            _pendingCaretOffset = -1;
            _activeSegmentTextBox = null;
            LastActiveInlineCanvas = null;
        }

        public void TogglePreviewHeadingFold(int headingStartLineZeroBased)
        {
            if (!_collapsedPreviewHeadingLines.Remove(headingStartLineZeroBased))
            {
                _collapsedPreviewHeadingLines.Add(headingStartLineZeroBased);
                if (_activeSegmentStartLine > headingStartLineZeroBased)
                {
                    // Ensure cursor does not stay hidden inside newly folded section
                    int sectionEnd = FindSectionEndLine(_lastSegments, headingStartLineZeroBased);
                    if (_activeSegmentStartLine <= sectionEnd)
                        _activeSegmentStartLine = headingStartLineZeroBased;
                }
            }
            RenderMarkdown(_markdown);
        }

        public void OpenInlineImageEditor(int openFenceLineZeroBased)
        {
            _openInlineImageEditors.Add(openFenceLineZeroBased);
            RenderMarkdown(_markdown);
        }

        public void CloseInlineImageEditor(int openFenceLineZeroBased)
        {
            if (_openInlineImageEditors.Remove(openFenceLineZeroBased))
                RenderMarkdown(_markdown);
        }

        public void OpenEmbeddedImageEditor(string targetName)
        {
            if (string.IsNullOrWhiteSpace(targetName)) return;
            _openEmbeddedImageEditors.Add(targetName);
            RenderMarkdown(_markdown);
        }

        public void ActivateSegmentAtLine(
            int startLineZeroBased,
            int? caretOffset = null,
            int? targetColumn = null,
            bool fromTop = true,
            bool caretAtAbsoluteStart = false,
            bool caretAtAbsoluteEnd = false)
        {
            var segments = _lastSegments.Count > 0
                ? _lastSegments
                : BuildEditorSegments(_markdown.Replace("\r\n", "\n").Split('\n'), ResolveEmbeddedDrawing);

            var target = segments.FirstOrDefault(s => startLineZeroBased >= s.StartLine && startLineZeroBased <= s.EndLine)
                         ?? segments.FirstOrDefault(s => s.StartLine >= startLineZeroBased)
                         ?? segments.LastOrDefault();

            if (target is null)
            {
                _activeSegmentStartLine = 0;
                _pendingCaretOffset = 0;
                RenderMarkdown(_markdown);
                return;
            }

            _activeSegmentStartLine = target.StartLine;
            if (caretAtAbsoluteStart)
            {
                _pendingCaretOffset = 0;
            }
            else if (caretAtAbsoluteEnd)
            {
                _pendingCaretOffset = target.RawText.Length;
            }
            else if (caretOffset.HasValue)
            {
                _pendingCaretOffset = Math.Clamp(caretOffset.Value, 0, target.RawText.Length);
            }
            else if (targetColumn.HasValue)
            {
                _pendingCaretOffset = ResolveOffsetFromColumn(target.RawText, targetColumn.Value, fromTop);
            }
            else
            {
                _pendingCaretOffset = target.RawText.Length;
            }

            RenderMarkdown(_markdown);
        }

        public void DeactivateActiveSegment()
        {
            if (_activeSegmentStartLine < 0)
                return;
            _activeSegmentStartLine = -1;
            _pendingCaretOffset = -1;
            _activeSegmentTextBox = null;
            RenderMarkdown(_markdown);
        }

        public bool MoveCursorToAdjacentSegment(int direction)
        {
            if (_visibleEditableSegments.Count == 0)
                return false;

            int currentIdx = _visibleEditableSegments.FindIndex(s => s.StartLine == _activeSegmentStartLine);
            int targetIdx;
            if (currentIdx < 0)
                targetIdx = direction >= 0 ? 0 : _visibleEditableSegments.Count - 1;
            else
                targetIdx = currentIdx + (direction >= 0 ? 1 : -1);

            if (targetIdx < 0 || targetIdx >= _visibleEditableSegments.Count)
                return false;

            var target = _visibleEditableSegments[targetIdx];
            ActivateSegmentAtLine(
                target.StartLine,
                caretAtAbsoluteStart: direction >= 0,
                caretAtAbsoluteEnd: direction < 0);
            return true;
        }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (_isDarkTheme == value) return;
                _isDarkTheme = value;
                _palette = value ? ThemePalette.CreateClassicDark() : ThemePalette.CreateClassicLight();
                RenderMarkdown(_markdown);
            }
        }

        public void ApplyThemePalette(ThemePalette palette, bool isDark, double previewFontSize = 14.5)
        {
            _palette = palette ?? (isDark ? ThemePalette.CreateClassicDark() : ThemePalette.CreateClassicLight());
            _isDarkTheme = isDark;
            _baseFontSize = Math.Clamp(previewFontSize, 10.0, 32.0);
            RenderMarkdown(_markdown);
        }

        public void RenderMarkdown(string? markdown)
        {
            _markdown = markdown ?? string.Empty;
            _rootStack.Children.Clear();
            _renderedSegmentVisuals.Clear();
            _activeSegmentTextBox = null;

            string[] origLines = string.IsNullOrEmpty(_markdown)
                ? new[] { string.Empty }
                : _markdown.Replace("\r\n", "\n").Split('\n');

            _lastSegments = BuildEditorSegments(origLines, ResolveEmbeddedDrawing);
            _visibleEditableSegments = new List<EditorSegment>();

            void AddSegmentVisual(Control ctrl, EditorSegment seg)
            {
                _rootStack.Children.Add(ctrl);
                _renderedSegmentVisuals.Add((ctrl, seg.StartLine, seg.EndLine));
            }

            if (string.IsNullOrWhiteSpace(_markdown) && _activeSegmentStartLine < 0)
            {
                var emptyHint = new TextBlock
                {
                    Text = "Click anywhere to start typing Markdown, # Headings, $LaTeX$, or press '?' / F1 for the Syntax Guide...",
                    Foreground = MutedBrush(),
                    FontStyle = FontStyle.Italic,
                    FontSize = _baseFontSize,
                    Margin = new Thickness(0, 6)
                };
                if (_lastSegments.Count > 0)
                {
                    _visibleEditableSegments.Add(_lastSegments[0]);
                    AddSegmentVisual(WrapSingleClickActivatableSegment(emptyHint, _lastSegments[0]), _lastSegments[0]);
                }
                else
                {
                    _rootStack.Children.Add(emptyHint);
                }
                return;
            }

            int? collapsedHeadingLevel = null;

            foreach (var segment in _lastSegments)
            {
                try
                {
                    if (segment.Kind == EditorSegmentKind.Heading)
                    {
                        if (collapsedHeadingLevel.HasValue && segment.HeadingLevel <= collapsedHeadingLevel.Value)
                            collapsedHeadingLevel = null;

                        if (collapsedHeadingLevel.HasValue)
                            continue;

                        _visibleEditableSegments.Add(segment);
                        bool isCollapsed = segment.HeadingLevel <= 5 && _collapsedPreviewHeadingLines.Contains(segment.StartLine);
                        Control headingVisual = segment.StartLine == _activeSegmentStartLine
                            ? RenderActiveTextEditorSegment(segment, isCollapsed)
                            : WrapSingleClickActivatableSegment(RenderSegmentPreview(segment, isCollapsed), segment);
                        AddSegmentVisual(headingVisual, segment);

                        if (isCollapsed)
                            collapsedHeadingLevel = segment.HeadingLevel;
                        continue;
                    }

                    if (collapsedHeadingLevel.HasValue)
                        continue;

                    if (segment.Kind == EditorSegmentKind.FencedDrawing)
                    {
                        string[] linesInBlock = origLines[segment.StartLine..(segment.EndLine + 1)];
                        string jsonBody = linesInBlock.Length > 2
                            ? string.Join("\n", linesInBlock[1..^1])
                            : string.Empty;
                        AddSegmentVisual(RenderInlineDrawingCard(jsonBody, segment.StartLine, segment.EndLine), segment);
                        continue;
                    }

                    if (segment.Kind == EditorSegmentKind.EmbeddedDrawing)
                    {
                        string cleanTarget = ExtractCleanEmbedTarget(segment.RawText.Trim()[3..^2].Trim(), out _, out _);
                        AddSegmentVisual(RenderEmbeddedNdrawCard(cleanTarget, segment.StartLine, segment.EndLine), segment);
                        continue;
                    }

                    if (segment.Kind == EditorSegmentKind.MarkdownImage)
                    {
                        string trimmed = segment.RawText.Trim();
                        string alt = trimmed;
                        string url = trimmed;
                        int? customWidth = null;
                        if (trimmed.StartsWith("![[", StringComparison.Ordinal) && trimmed.EndsWith("]]", StringComparison.Ordinal))
                        {
                            string inner = trimmed[3..^2].Trim();
                            url = ExtractCleanEmbedTarget(inner, out string? pipeParam, out customWidth);
                            alt = !string.IsNullOrWhiteSpace(pipeParam) && !customWidth.HasValue ? pipeParam : url;
                        }
                        else if (TryParseMarkdownImageLine(trimmed, out string parsedAlt, out string parsedUrl, out int? altWidth))
                        {
                            alt = parsedAlt;
                            url = parsedUrl;
                            customWidth = altWidth;
                        }
                        AddSegmentVisual(RenderMarkdownImageCard(url, alt, segment.StartLine, segment.EndLine, customWidth), segment);
                        continue;
                    }

                    _visibleEditableSegments.Add(segment);
                    Control bodyVisual = segment.StartLine == _activeSegmentStartLine
                        ? RenderActiveTextEditorSegment(segment, isCollapsed: false)
                        : WrapSingleClickActivatableSegment(RenderSegmentPreview(segment, isCollapsed: false), segment);
                    AddSegmentVisual(bodyVisual, segment);
                }
                catch
                {
                    var fallback = CreateInlineTextBlock(_baseFontSize, FontWeight.Normal, TextBrush());
                    fallback.Text = segment.RawText;
                    AddSegmentVisual(WrapSingleClickActivatableSegment(fallback, segment), segment);
                }
            }
        }

        public int GetDocumentLineAtPoint(Point pointInPreview)
        {
            Point pt = this.TranslatePoint(pointInPreview, _rootStack)
                ?? new Point(
                    pointInPreview.X - _rootStack.Margin.Left,
                    pointInPreview.Y - _rootStack.Margin.Top + _scrollViewer.Offset.Y);

            if (_renderedSegmentVisuals.Count == 0 || _lastSegments.Count == 0)
                return 0;

            bool anyMeasured = false;
            foreach (var (visual, startLine, endLine) in _renderedSegmentVisuals)
            {
                Rect b = visual.Bounds;
                if (b.Height <= 0)
                    continue;
                anyMeasured = true;

                if (pt.Y < b.Top)
                    return Math.Max(0, startLine);

                if (pt.Y <= b.Bottom)
                {
                    if (endLine > startLine)
                    {
                        double ratio = Math.Clamp((pt.Y - b.Top) / Math.Max(1.0, b.Height), 0.0, 1.0);
                        return startLine + (int)Math.Round(ratio * (endLine - startLine));
                    }
                    return pt.Y > (b.Top + b.Height * 0.65) ? startLine + 1 : startLine;
                }
            }

            if (!anyMeasured)
            {
                int estimated = (int)Math.Floor(Math.Max(0, pt.Y) / 24.0);
                return Math.Clamp(estimated, 0, Math.Max(0, _lastSegments[^1].EndLine));
            }

            return _lastSegments[^1].EndLine + 1;
        }

        public static string InsertImageMarkdownAtLine(
            string markdown,
            string imageMarkdownLink,
            int targetLineZeroBased,
            out int insertedLineZeroBased)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                insertedLineZeroBased = 0;
                return $"{imageMarkdownLink}\n";
            }

            string lineEnding = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = new List<string>(markdown.Replace("\r\n", "\n").Split('\n'));
            int idx = Math.Clamp(targetLineZeroBased, 0, lines.Count);

            if (idx < lines.Count && string.IsNullOrWhiteSpace(lines[idx]))
            {
                lines[idx] = imageMarkdownLink;
                insertedLineZeroBased = idx;
            }
            else if (idx < lines.Count)
            {
                lines.Insert(idx, imageMarkdownLink);
                insertedLineZeroBased = idx;
            }
            else
            {
                lines.Add(imageMarkdownLink);
                insertedLineZeroBased = lines.Count - 1;
            }

            if (insertedLineZeroBased == lines.Count - 1)
                lines.Add(string.Empty);

            return string.Join(lineEnding, lines);
        }

        internal static string? TryExtractDroppedImageFilePath(IDataTransfer dataTransfer)
        {
            var files = dataTransfer.TryGetFiles();
            if (files is not null)
            {
                foreach (var item in files)
                {
                    string? path = item.TryGetLocalPath();
                    if (!string.IsNullOrWhiteSpace(path) && IsImageExtension(path) && File.Exists(path))
                        return path;
                }
            }

            string? text = dataTransfer.TryGetText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                string trimmed = text.Trim().Trim('"');
                if (IsImageExtension(trimmed) && File.Exists(trimmed))
                    return Path.GetFullPath(trimmed);
            }

            return null;
        }

        private void OnPreviewDragOver(object? sender, DragEventArgs e)
        {
            if (TryExtractDroppedImageFilePath(e.DataTransfer) is not null ||
                e.DataTransfer.Contains(DataFormat.File))
            {
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void OnPreviewDrop(object? sender, DragEventArgs e)
        {
            string? imagePath = TryExtractDroppedImageFilePath(e.DataTransfer);
            if (!string.IsNullOrWhiteSpace(imagePath))
            {
                int dropLine = GetDocumentLineAtPoint(e.GetPosition(this));
                ImageFileDroppedAtLine?.Invoke(this, (imagePath, dropLine));
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void OnBackgroundPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var pointInfo = e.GetCurrentPoint(_scrollViewer);
            if (!pointInfo.Properties.IsLeftButtonPressed)
                return;

            // Clicking empty canvas space below blocks starts/activates an editable tail line
            if (_visibleEditableSegments.Count == 0 ||
                !IsLineSegmentEditable(_lastSegments.LastOrDefault()?.Kind ?? EditorSegmentKind.BlankLine))
            {
                string lineEnding = _markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                string next = string.IsNullOrEmpty(_markdown) ? string.Empty : $"{_markdown.TrimEnd()}{lineEnding}";
                int tailLine = string.IsNullOrEmpty(next) ? 0 : next.Replace("\r\n", "\n").Split('\n').Length - 1;
                _activeSegmentStartLine = tailLine;
                _pendingCaretOffset = 0;
                RenderMarkdown(next);
                DocumentEditedFromPreview?.Invoke(this, _markdown);
                e.Handled = true;
                return;
            }

            var lastTextSeg = _visibleEditableSegments[^1];
            ActivateSegmentAtLine(lastTextSeg.StartLine, caretAtAbsoluteEnd: true);
            e.Handled = true;
        }

        private static bool IsLineSegmentEditable(EditorSegmentKind kind) => kind is not
            (EditorSegmentKind.FencedDrawing or EditorSegmentKind.EmbeddedDrawing or EditorSegmentKind.MarkdownImage);

        private static bool IsPictureSegment(EditorSegmentKind kind) =>
            kind is EditorSegmentKind.MarkdownImage
                 or EditorSegmentKind.EmbeddedDrawing
                 or EditorSegmentKind.FencedDrawing;

        private EditorSegment? FindImmediatelyPrecedingSegment(int startLineZeroBased) =>
            _lastSegments.LastOrDefault(s => s.EndLine == startLineZeroBased - 1);

        private EditorSegment? FindImmediatelyFollowingSegment(int endLineZeroBased) =>
            _lastSegments.FirstOrDefault(s => s.StartLine == endLineZeroBased + 1);

        public static string RemoveLineRange(string markdown, int startLineZeroBased, int endLineZeroBased)
        {
            if (string.IsNullOrEmpty(markdown))
                return string.Empty;

            string lineEnding = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = new List<string>(markdown.Replace("\r\n", "\n").Split('\n'));
            if (lines.Count == 0)
                return string.Empty;

            int start = Math.Clamp(startLineZeroBased, 0, lines.Count - 1);
            int end = Math.Clamp(endLineZeroBased, start, lines.Count - 1);
            int removeCount = (end - start) + 1;
            lines.RemoveRange(start, removeCount);
            return lines.Count == 0 ? string.Empty : string.Join(lineEnding, lines);
        }

        public void DeleteImageBlockAtLineRange(
            int startLineZeroBased,
            int endLineZeroBased,
            int? nextActiveLine = null,
            int? nextCaretOffset = null)
        {
            _openInlineImageEditors.Remove(startLineZeroBased);
            string keyPrefix = $"{startLineZeroBased}:";
            foreach (var key in _openMarkdownImageEditors.Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList())
                _openMarkdownImageEditors.Remove(key);

            string updatedMd = RemoveLineRange(_markdown, startLineZeroBased, endLineZeroBased);
            int removedCount = (endLineZeroBased - startLineZeroBased) + 1;
            int remainingLineCount = string.IsNullOrEmpty(updatedMd)
                ? 0
                : updatedMd.Replace("\r\n", "\n").Split('\n').Length;

            if (nextActiveLine.HasValue)
            {
                if (remainingLineCount == 0)
                {
                    _activeSegmentStartLine = 0;
                    _pendingCaretOffset = 0;
                }
                else
                {
                    _activeSegmentStartLine = Math.Clamp(nextActiveLine.Value, 0, remainingLineCount - 1);
                    _pendingCaretOffset = Math.Max(0, nextCaretOffset ?? 0);
                }
            }
            else if (_activeSegmentStartLine > endLineZeroBased)
            {
                _activeSegmentStartLine = Math.Max(0, _activeSegmentStartLine - removedCount);
            }
            else if (_activeSegmentStartLine >= startLineZeroBased && _activeSegmentStartLine <= endLineZeroBased)
            {
                _activeSegmentStartLine = -1;
            }

            RenderMarkdown(updatedMd);
            DocumentEditedFromPreview?.Invoke(this, _markdown);
        }

        private static int FindSectionEndLine(IReadOnlyList<EditorSegment> segments, int headingStartLine)
        {
            int idx = -1;
            int level = 6;
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].StartLine == headingStartLine)
                {
                    idx = i;
                    level = segments[i].HeadingLevel;
                    break;
                }
            }
            if (idx < 0) return headingStartLine;

            int end = segments[idx].EndLine;
            for (int i = idx + 1; i < segments.Count; i++)
            {
                if (segments[i].Kind == EditorSegmentKind.Heading && segments[i].HeadingLevel <= level)
                    break;
                end = segments[i].EndLine;
            }
            return end;
        }

        private static List<EditorSegment> BuildEditorSegments(string[] lines, Func<string, DrawingDocument?>? resolveDrawing)
        {
            var segments = new List<EditorSegment>();
            int i = 0;
            while (i < lines.Length)
            {
                string raw = lines[i];
                string trimmed = raw.Trim();

                // 1. Fenced code / latex / drawing block
                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    int closeIdx = lines.Length - 1;
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        if (lines[j].Trim().StartsWith("```", StringComparison.Ordinal))
                        {
                            closeIdx = j;
                            break;
                        }
                    }

                    string info = trimmed.Length > 3 ? trimmed[3..].Trim().ToLowerInvariant() : string.Empty;
                    var kind = info switch
                    {
                        "drawing" or "ndraw" => EditorSegmentKind.FencedDrawing,
                        "latex" or "math" or "tex" => EditorSegmentKind.FencedLatex,
                        _ => EditorSegmentKind.FencedCode
                    };
                    segments.Add(new EditorSegment
                    {
                        StartLine = i,
                        EndLine = closeIdx,
                        Kind = kind,
                        RawText = string.Join("\n", lines[i..(closeIdx + 1)])
                    });
                    i = closeIdx + 1;
                    continue;
                }

                // 2. Display math $$ ... $$ or \[ ... \]
                if (trimmed.StartsWith("$$", StringComparison.Ordinal))
                {
                    int second = raw.IndexOf("$$", raw.IndexOf("$$", StringComparison.Ordinal) + 2, StringComparison.Ordinal);
                    int closeIdx = i;
                    if (second < 0)
                    {
                        closeIdx = lines.Length - 1;
                        for (int j = i + 1; j < lines.Length; j++)
                        {
                            if (lines[j].Trim().EndsWith("$$", StringComparison.Ordinal))
                            {
                                closeIdx = j;
                                break;
                            }
                        }
                    }
                    segments.Add(new EditorSegment
                    {
                        StartLine = i,
                        EndLine = closeIdx,
                        Kind = EditorSegmentKind.DisplayMath,
                        RawText = string.Join("\n", lines[i..(closeIdx + 1)])
                    });
                    i = closeIdx + 1;
                    continue;
                }

                if (trimmed.StartsWith(@"\[", StringComparison.Ordinal))
                {
                    int closeIdx = i;
                    if (!(trimmed.EndsWith(@"\]", StringComparison.Ordinal) && trimmed.Length >= 4))
                    {
                        closeIdx = lines.Length - 1;
                        for (int j = i + 1; j < lines.Length; j++)
                        {
                            if (lines[j].Trim().EndsWith(@"\]", StringComparison.Ordinal))
                            {
                                closeIdx = j;
                                break;
                            }
                        }
                    }
                    segments.Add(new EditorSegment
                    {
                        StartLine = i,
                        EndLine = closeIdx,
                        Kind = EditorSegmentKind.DisplayMath,
                        RawText = string.Join("\n", lines[i..(closeIdx + 1)])
                    });
                    i = closeIdx + 1;
                    continue;
                }

                // 3. Pipe table
                if (trimmed.StartsWith('|') && i + 1 < lines.Length && lines[i + 1].Trim().StartsWith('|'))
                {
                    int endTable = i + 1;
                    while (endTable + 1 < lines.Length && lines[endTable + 1].Trim().StartsWith('|'))
                        endTable++;

                    segments.Add(new EditorSegment
                    {
                        StartLine = i,
                        EndLine = endTable,
                        Kind = EditorSegmentKind.Table,
                        RawText = string.Join("\n", lines[i..(endTable + 1)])
                    });
                    i = endTable + 1;
                    continue;
                }

                // 4. Blank line
                if (string.IsNullOrWhiteSpace(raw))
                {
                    segments.Add(new EditorSegment
                    {
                        StartLine = i,
                        EndLine = i,
                        Kind = EditorSegmentKind.BlankLine,
                        RawText = raw
                    });
                    i++;
                    continue;
                }

                // 5. Embedded .ndraw or Markdown/Vault image
                if (trimmed.StartsWith("![[", StringComparison.Ordinal) && trimmed.EndsWith("]]", StringComparison.Ordinal))
                {
                    string cleanTarget = ExtractCleanEmbedTarget(trimmed[3..^2].Trim(), out _, out _);
                    if (IsImageExtension(cleanTarget))
                    {
                        segments.Add(new EditorSegment
                        {
                            StartLine = i,
                            EndLine = i,
                            Kind = EditorSegmentKind.MarkdownImage,
                            RawText = raw
                        });
                        i++;
                        continue;
                    }
                    if (cleanTarget.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase))
                    {
                        segments.Add(new EditorSegment
                        {
                            StartLine = i,
                            EndLine = i,
                            Kind = EditorSegmentKind.EmbeddedDrawing,
                            RawText = raw
                        });
                        i++;
                        continue;
                    }
                }

                if (TryParseMarkdownImageLine(trimmed, out _, out _, out _))
                {
                    segments.Add(new EditorSegment
                    {
                        StartLine = i,
                        EndLine = i,
                        Kind = EditorSegmentKind.MarkdownImage,
                        RawText = raw
                    });
                    i++;
                    continue;
                }

                // 6. Heading H1..H6
                var hMatch = HeadingLineRegex.Match(raw);
                if (hMatch.Success)
                {
                    segments.Add(new EditorSegment
                    {
                        StartLine = i,
                        EndLine = i,
                        Kind = EditorSegmentKind.Heading,
                        HeadingLevel = hMatch.Groups[1].Value.Length,
                        RawText = raw
                    });
                    i++;
                    continue;
                }

                // 7. Standard Markdown text / list / task / quote / divider line
                segments.Add(new EditorSegment
                {
                    StartLine = i,
                    EndLine = i,
                    Kind = EditorSegmentKind.SingleLineMarkdown,
                    RawText = raw
                });
                i++;
            }

            return segments;
        }

        private Control RenderActiveTextEditorSegment(EditorSegment segment, bool isCollapsed)
        {
            bool isMultiLine = segment.Kind is EditorSegmentKind.FencedLatex
                or EditorSegmentKind.DisplayMath
                or EditorSegmentKind.FencedCode
                or EditorSegmentKind.Table;

            double fontSize = segment.Kind == EditorSegmentKind.Heading
                ? segment.HeadingLevel switch
                {
                    1 => _baseFontSize + 9.5,
                    2 => _baseFontSize + 5.5,
                    3 => _baseFontSize + 3.0,
                    4 => _baseFontSize + 1.5,
                    5 => _baseFontSize,
                    _ => _baseFontSize - 0.5
                }
                : _baseFontSize;

            var fontWeight = segment.Kind == EditorSegmentKind.Heading ? FontWeight.Bold : FontWeight.Normal;
            var fontFamily = isMultiLine
                ? new FontFamily("Cascadia Code, Consolas, Courier New, monospace")
                : FontFamily.Default;

            int resolvedCaret = _pendingCaretOffset >= 0
                ? Math.Clamp(_pendingCaretOffset, 0, segment.RawText.Length)
                : segment.RawText.Length;
            _pendingCaretOffset = -1;

            var editBox = new TextBox
            {
                Text = segment.RawText,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                IsUndoEnabled = false,
                FontSize = fontSize,
                FontWeight = fontWeight,
                FontFamily = fontFamily,
                Foreground = TextBrush(),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 1),
                MinHeight = 0,
                CaretIndex = resolvedCaret,
                SelectionStart = resolvedCaret,
                SelectionEnd = resolvedCaret
            };

            _activeSegmentTextBox = editBox;

            var liveLatexCtrl = new LatexMathControl
            {
                FontSize = 18,
                IsDisplayMode = true,
                Foreground = TextBrush(),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var liveDiagText = new TextBlock
            {
                FontSize = 11,
                Foreground = WarningBrush(),
                IsVisible = false
            };
            var liveMathCard = new Border
            {
                IsVisible = false,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = BorderBrushColor(),
                Background = CardBrush(),
                Padding = new Thickness(10, 6),
                Margin = new Thickness(0, 4, 0, 2),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children = { liveLatexCtrl, liveDiagText }
                }
            };

            void RefreshLiveLatexPopup()
            {
                string currentText = editBox.Text ?? string.Empty;
                int cIdx = Math.Clamp(editBox.CaretIndex, 0, currentText.Length);
                var ctx = LatexLiveDetector.DetectAtCursor(currentText, cIdx);
                if (ctx.IsFound)
                {
                    liveLatexCtrl.LatexSource = ctx.LatexCode;
                    liveLatexCtrl.IsDisplayMode = ctx.IsDisplayMode;
                    liveDiagText.Text = liveLatexCtrl.DiagnosticsText;
                    liveDiagText.IsVisible = !string.IsNullOrEmpty(liveDiagText.Text);
                    liveMathCard.IsVisible = true;
                }
                else if (segment.Kind is EditorSegmentKind.FencedLatex or EditorSegmentKind.DisplayMath)
                {
                    string formula = ExtractMathFormula(currentText);
                    if (!string.IsNullOrWhiteSpace(formula))
                    {
                        liveLatexCtrl.LatexSource = formula;
                        liveLatexCtrl.IsDisplayMode = true;
                        liveDiagText.Text = liveLatexCtrl.DiagnosticsText;
                        liveDiagText.IsVisible = !string.IsNullOrEmpty(liveDiagText.Text);
                        liveMathCard.IsVisible = true;
                    }
                    else
                    {
                        liveMathCard.IsVisible = false;
                    }
                }
                else
                {
                    liveMathCard.IsVisible = false;
                }
            }

            void EmitCursorPos()
            {
                string currentText = editBox.Text ?? string.Empty;
                int caret = Math.Clamp(editBox.CaretIndex, 0, currentText.Length);
                var (relLn, col) = GetLineAndColumn(currentText, caret);
                CursorPositionChanged?.Invoke(this, (segment.StartLine + relLn, col));
            }

            editBox.AttachedToVisualTree += (_, _) =>
            {
                if (ReferenceEquals(_activeSegmentTextBox, editBox))
                {
                    editBox.Focus();
                    int c = Math.Clamp(resolvedCaret, 0, (editBox.Text ?? string.Empty).Length);
                    editBox.CaretIndex = c;
                    editBox.SelectionStart = c;
                    editBox.SelectionEnd = c;
                    EmitCursorPos();
                }
            };

            bool suppressSelfTextChanged = false;
            editBox.TextChanged += (_, _) =>
            {
                if (suppressSelfTextChanged)
                    return;

                string nextRaw = (editBox.Text ?? string.Empty).Replace("\r\n", "\n");
                segment.RawText = nextRaw;

                string updatedMd = ReplaceLineRange(_markdown, segment.StartLine, segment.EndLine, nextRaw);
                int newLineCount = nextRaw.Split('\n').Length;
                int delta = newLineCount - ((segment.EndLine - segment.StartLine) + 1);
                segment.EndLine = segment.StartLine + newLineCount - 1;
                if (delta != 0)
                    ShiftSegmentsAfter(segment, delta);

                _markdown = updatedMd;
                RefreshLiveLatexPopup();
                EmitCursorPos();
                DocumentEditedFromPreview?.Invoke(this, updatedMd);
            };

            editBox.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBox.CaretIndexProperty)
                {
                    RefreshLiveLatexPopup();
                    EmitCursorPos();
                }
            };

            editBox.AddHandler(InputElement.KeyDownEvent, (_, ke) =>
            {
                if (ke.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    if (ke.Key == Key.V && TryHandleImagePasteCallback is not null && TryHandleImagePasteCallback(segment.StartLine))
                        ke.Handled = true;
                    return;
                }

                string curText = editBox.Text ?? string.Empty;
                int caret = Math.Clamp(editBox.CaretIndex, 0, curText.Length);
                int curLineIdx = GetCaretLineIndex(curText, caret, out int colInLine, out int totalLines);

                if (ke.Key == Key.Up && curLineIdx == 0)
                {
                    if (TryActivateRelativeSegment(segment.StartLine, -1, targetColumn: colInLine, fromTop: false))
                        ke.Handled = true;
                    return;
                }

                if (ke.Key == Key.Down && curLineIdx == totalLines - 1)
                {
                    if (TryActivateRelativeSegment(segment.StartLine, +1, targetColumn: colInLine, fromTop: true))
                        ke.Handled = true;
                    return;
                }

                if (ke.Key == Key.Left && caret == 0 && editBox.SelectionStart == editBox.SelectionEnd)
                {
                    if (TryActivateRelativeSegment(segment.StartLine, -1, caretAtAbsoluteEnd: true))
                        ke.Handled = true;
                    return;
                }

                if (ke.Key == Key.Right && caret == curText.Length && editBox.SelectionStart == editBox.SelectionEnd)
                {
                    if (TryActivateRelativeSegment(segment.StartLine, +1, caretAtAbsoluteStart: true))
                        ke.Handled = true;
                    return;
                }

                if (ke.Key == Key.Escape)
                {
                    DeactivateActiveSegment();
                    ke.Handled = true;
                    return;
                }

                if (ke.Key == Key.Enter && !ke.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    string trimStart = curText.TrimStart();
                    bool keepInsideMultiLine = isMultiLine ||
                        trimStart.StartsWith("```", StringComparison.Ordinal) ||
                        trimStart.StartsWith("$$", StringComparison.Ordinal) ||
                        trimStart.StartsWith(@"\[", StringComparison.Ordinal);

                    if (!keepInsideMultiLine)
                    {
                        string before = curText[..caret];
                        string after = curText[caret..];
                        string splitSlice = $"{before}\n{after}";
                        suppressSelfTextChanged = true;
                        string updatedMd = ReplaceLineRange(_markdown, segment.StartLine, segment.EndLine, splitSlice);
                        _activeSegmentStartLine = segment.StartLine + 1;
                        _pendingCaretOffset = 0;
                        RenderMarkdown(updatedMd);
                        DocumentEditedFromPreview?.Invoke(this, _markdown);
                        ke.Handled = true;
                    }
                    return;
                }

                if (ke.Key == Key.Back && caret == 0 && editBox.SelectionStart == editBox.SelectionEnd && segment.StartLine > 0)
                {
                    var prevAdj = FindImmediatelyPrecedingSegment(segment.StartLine);
                    if (prevAdj is not null && IsPictureSegment(prevAdj.Kind))
                    {
                        suppressSelfTextChanged = true;
                        DeleteImageBlockAtLineRange(
                            prevAdj.StartLine,
                            prevAdj.EndLine,
                            nextActiveLine: prevAdj.StartLine,
                            nextCaretOffset: 0);
                        ke.Handled = true;
                        return;
                    }

                    if (prevAdj is not null &&
                        prevAdj.Kind is EditorSegmentKind.BlankLine or EditorSegmentKind.Heading or EditorSegmentKind.SingleLineMarkdown)
                    {
                        int joinOffset = prevAdj.RawText.Length;
                        string joined = prevAdj.RawText + curText;
                        suppressSelfTextChanged = true;
                        string updatedMd = ReplaceLineRange(_markdown, prevAdj.StartLine, segment.EndLine, joined);
                        _activeSegmentStartLine = prevAdj.StartLine;
                        _pendingCaretOffset = joinOffset;
                        RenderMarkdown(updatedMd);
                        DocumentEditedFromPreview?.Invoke(this, _markdown);
                        ke.Handled = true;
                    }
                    return;
                }

                if (ke.Key == Key.Delete && caret == curText.Length && editBox.SelectionStart == editBox.SelectionEnd)
                {
                    var nextAdj = FindImmediatelyFollowingSegment(segment.EndLine);
                    if (nextAdj is not null && IsPictureSegment(nextAdj.Kind))
                    {
                        suppressSelfTextChanged = true;
                        DeleteImageBlockAtLineRange(
                            nextAdj.StartLine,
                            nextAdj.EndLine,
                            nextActiveLine: segment.StartLine,
                            nextCaretOffset: caret);
                        ke.Handled = true;
                        return;
                    }

                    if (nextAdj is not null &&
                        nextAdj.Kind is EditorSegmentKind.BlankLine or EditorSegmentKind.Heading or EditorSegmentKind.SingleLineMarkdown)
                    {
                        string joined = curText + nextAdj.RawText;
                        suppressSelfTextChanged = true;
                        string updatedMd = ReplaceLineRange(_markdown, segment.StartLine, nextAdj.EndLine, joined);
                        _activeSegmentStartLine = segment.StartLine;
                        _pendingCaretOffset = caret;
                        RenderMarkdown(updatedMd);
                        DocumentEditedFromPreview?.Invoke(this, _markdown);
                        ke.Handled = true;
                    }
                }
            }, RoutingStrategies.Tunnel);

            RefreshLiveLatexPopup();
            EmitCursorPos();

            Control mainRow = editBox;
            if (segment.Kind == EditorSegmentKind.Heading && segment.HeadingLevel <= 5)
            {
                var foldBtn = CreateFoldToggleButton(segment.StartLine, segment.HeadingLevel, isCollapsed);
                var hGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                Grid.SetColumn(foldBtn, 0);
                Grid.SetColumn(editBox, 1);
                hGrid.Children.Add(foldBtn);
                hGrid.Children.Add(editBox);
                mainRow = hGrid;
            }

            var activeLineFrame = new Border
            {
                BorderThickness = new Thickness(2, 0, 0, 0),
                BorderBrush = ThemeConfigService.ParseBrush(_palette.AccentColor, "#0078D4"),
                Padding = new Thickness(6, 1, 2, 1),
                Child = mainRow
            };

            return new StackPanel
            {
                Spacing = 2,
                Children = { activeLineFrame, liveMathCard }
            };
        }

        private void ShiftSegmentsAfter(EditorSegment changedSegment, int lineDelta)
        {
            bool past = false;
            foreach (var seg in _lastSegments)
            {
                if (ReferenceEquals(seg, changedSegment))
                {
                    past = true;
                    continue;
                }
                if (past)
                {
                    seg.StartLine += lineDelta;
                    seg.EndLine += lineDelta;
                }
            }
        }

        private bool TryActivateRelativeSegment(
            int currentStartLine,
            int direction,
            int? targetColumn = null,
            bool fromTop = true,
            bool caretAtAbsoluteStart = false,
            bool caretAtAbsoluteEnd = false)
        {
            int idx = _visibleEditableSegments.FindIndex(s => s.StartLine == currentStartLine);
            int targetIdx = idx + (direction >= 0 ? 1 : -1);
            if (idx < 0 || targetIdx < 0 || targetIdx >= _visibleEditableSegments.Count)
            {
                if (direction > 0 &&
                    _lastSegments.Count > 0 &&
                    IsPictureSegment(_lastSegments[^1].Kind) &&
                    _lastSegments[^1].StartLine > currentStartLine)
                {
                    string lineEnding = _markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                    string next = _markdown + lineEnding;
                    int newTailLine = next.Replace("\r\n", "\n").Split('\n').Length - 1;
                    _activeSegmentStartLine = newTailLine;
                    _pendingCaretOffset = 0;
                    RenderMarkdown(next);
                    DocumentEditedFromPreview?.Invoke(this, _markdown);
                    return true;
                }
                return false;
            }

            var target = _visibleEditableSegments[targetIdx];
            ActivateSegmentAtLine(
                target.StartLine,
                targetColumn: targetColumn,
                fromTop: fromTop,
                caretAtAbsoluteStart: caretAtAbsoluteStart,
                caretAtAbsoluteEnd: caretAtAbsoluteEnd);
            return true;
        }

        private Control RenderSegmentPreview(EditorSegment segment, bool isCollapsed)
        {
            if (segment.Kind == EditorSegmentKind.BlankLine)
            {
                return new Border
                {
                    MinHeight = 14,
                    Background = Brushes.Transparent
                };
            }

            if (segment.Kind == EditorSegmentKind.DisplayMath)
            {
                return RenderLatexCard(ExtractMathFormula(segment.RawText), "$$ Display Math", segment.StartLine);
            }

            if (segment.Kind == EditorSegmentKind.FencedLatex)
            {
                var lines = segment.RawText.Replace("\r\n", "\n").Split('\n');
                string firstTrim = lines[0].Trim();
                string info = firstTrim.Length > 3 ? firstTrim[3..].Trim().ToLowerInvariant() : "latex";
                string body = lines.Length > 2 ? string.Join("\n", lines[1..^1]) : (lines.Length == 2 ? lines[1] : string.Empty);
                return RenderLatexCard(body.Trim(), $"```{info} block", segment.StartLine);
            }

            try
            {
                string normalized = NormalizeMathBlocks(segment.RawText);
                MarkdownDocument doc = Markdig.Markdown.Parse(normalized, Pipeline);
                var blocks = doc.Where(b => b is not LinkReferenceDefinitionGroup).ToList();
                if (blocks.Count == 0)
                {
                    return CreateInlineTextBlock(_baseFontSize, FontWeight.Normal, TextBrush()).Also(tb => tb.Text = segment.RawText);
                }

                if (blocks.Count == 1)
                    return RenderParsedBlock(blocks[0], segment.StartLine, isCollapsed) ?? new Border { MinHeight = 12 };

                var stack = new StackPanel { Spacing = 4 };
                foreach (var b in blocks)
                {
                    var ctrl = RenderParsedBlock(b, segment.StartLine, isCollapsed);
                    if (ctrl is not null)
                        stack.Children.Add(ctrl);
                }
                return stack;
            }
            catch
            {
                return CreateInlineTextBlock(_baseFontSize, FontWeight.Normal, TextBrush()).Also(tb => tb.Text = segment.RawText);
            }
        }

        private Control WrapSingleClickActivatableSegment(Control renderedVisual, EditorSegment segment)
        {
            var host = new Border
            {
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Ibeam),
                Child = renderedVisual
            };

            host.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                var pt = e.GetCurrentPoint(host);
                if (!pt.Properties.IsLeftButtonPressed)
                    return;

                if (IsInteractiveChildControl(e.Source as Visual, host))
                    return;

                double approxFont = segment.Kind == EditorSegmentKind.Heading ? _baseFontSize + 4 : _baseFontSize;
                int approxOffset = segment.Kind is EditorSegmentKind.BlankLine
                    ? 0
                    : Math.Clamp((int)Math.Round(e.GetPosition(host).X / Math.Max(5.5, approxFont * 0.52)), 0, segment.RawText.Length);

                ActivateSegmentAtLine(segment.StartLine, caretOffset: approxOffset);
                e.Handled = true;
            }, RoutingStrategies.Tunnel);

            return host;
        }

        private static bool IsInteractiveChildControl(Visual? source, Visual stopAt)
        {
            Visual? current = source;
            while (current is not null && !ReferenceEquals(current, stopAt))
            {
                if (current is Button or CheckBox or TextBox)
                    return true;
                current = current.GetVisualParent();
            }
            return false;
        }

        private static string ExtractMathFormula(string rawBlock)
        {
            string trimmed = rawBlock.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var lines = trimmed.Replace("\r\n", "\n").Split('\n');
                if (lines.Length >= 3)
                    return string.Join("\n", lines[1..^1]).Trim();
                if (lines.Length == 2)
                    return lines[1].Trim();
                return string.Empty;
            }
            if (trimmed.StartsWith("$$", StringComparison.Ordinal))
            {
                string body = trimmed[2..];
                if (body.EndsWith("$$", StringComparison.Ordinal))
                    body = body[..^2];
                return body.Trim();
            }
            if (trimmed.StartsWith(@"\[", StringComparison.Ordinal))
            {
                string body = trimmed[2..];
                if (body.EndsWith(@"\]", StringComparison.Ordinal))
                    body = body[..^2];
                return body.Trim();
            }
            return trimmed;
        }

        private static int GetCaretLineIndex(string text, int caret, out int columnInLine, out int totalLines)
        {
            int lineIdx = 0;
            int col = 0;
            int linesCount = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                    linesCount++;
            }

            for (int i = 0; i < caret && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lineIdx++;
                    col = 0;
                }
                else if (text[i] != '\r')
                {
                    col++;
                }
            }

            columnInLine = col;
            totalLines = linesCount;
            return lineIdx;
        }

        private static (int LineOneBased, int ColumnOneBased) GetLineAndColumn(string text, int caret)
        {
            int lineIdx = GetCaretLineIndex(text, caret, out int col, out _);
            return (lineIdx + 1, col + 1);
        }

        private static int ResolveOffsetFromColumn(string text, int targetColumn, bool fromTop)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var lines = text.Replace("\r\n", "\n").Split('\n');
            int lineIdx = fromTop ? 0 : lines.Length - 1;
            int start = 0;
            for (int i = 0; i < lineIdx; i++)
                start += lines[i].Length + 1;

            int col = Math.Clamp(targetColumn, 0, lines[lineIdx].Length);
            return Math.Clamp(start + col, 0, text.Length);
        }

        /// <summary>
        /// Normalizes Obsidian-style $$...$$ and \[...\] display math blocks so Markdig parses every block as MathBlock.
        /// </summary>
        public static string NormalizeMathBlocks(string markdown)
            => NormalizeMathBlocksWithMap(markdown, out _);

        public static string NormalizeMathBlocksWithMap(string markdown, out int[] lineMap)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                lineMap = Array.Empty<int>();
                return string.Empty;
            }

            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            var output = new List<string>(lines.Length + 8);
            var map = new List<int>(lines.Length + 8);
            bool inFence = false;
            bool inDollarBlock = false;
            bool inBracketBlock = false;

            void AddLine(string text, int origLine)
            {
                output.Add(text);
                map.Add(origLine);
            }

            for (int origIdx = 0; origIdx < lines.Length; origIdx++)
            {
                string rawLine = lines[origIdx];
                string trimmed = rawLine.Trim();

                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    AddLine(rawLine, origIdx);
                    continue;
                }

                if (inFence)
                {
                    AddLine(rawLine, origIdx);
                    continue;
                }

                if (!inDollarBlock && !inBracketBlock &&
                    trimmed.StartsWith(@"\[", StringComparison.Ordinal) &&
                    trimmed.EndsWith(@"\]", StringComparison.Ordinal) &&
                    trimmed.Length >= 4)
                {
                    string inner = trimmed[2..^2].Trim();
                    AddLine("$$", origIdx);
                    AddLine(inner, origIdx);
                    AddLine("$$", origIdx);
                    continue;
                }

                if (!inDollarBlock && !inBracketBlock && trimmed == @"\[")
                {
                    inBracketBlock = true;
                    AddLine("$$", origIdx);
                    continue;
                }

                if (inBracketBlock && trimmed == @"\]")
                {
                    inBracketBlock = false;
                    AddLine("$$", origIdx);
                    continue;
                }

                if (!inDollarBlock && trimmed.StartsWith("$$", StringComparison.Ordinal))
                {
                    int firstIdx = rawLine.IndexOf("$$", StringComparison.Ordinal);
                    int secondIdx = rawLine.IndexOf("$$", firstIdx + 2, StringComparison.Ordinal);
                    if (secondIdx >= 0)
                    {
                        string inner = rawLine[(firstIdx + 2)..secondIdx].Trim();
                        AddLine("$$", origIdx);
                        AddLine(inner, origIdx);
                        AddLine("$$", origIdx);
                    }
                    else
                    {
                        string after = rawLine[(firstIdx + 2)..].Trim();
                        AddLine("$$", origIdx);
                        if (!string.IsNullOrEmpty(after))
                            AddLine(after, origIdx);
                        inDollarBlock = true;
                    }
                    continue;
                }

                if (inDollarBlock && trimmed.EndsWith("$$", StringComparison.Ordinal))
                {
                    int closeIdx = rawLine.LastIndexOf("$$", StringComparison.Ordinal);
                    string before = rawLine[..closeIdx].TrimEnd();
                    if (!string.IsNullOrEmpty(before))
                        AddLine(before, origIdx);
                    AddLine("$$", origIdx);
                    inDollarBlock = false;
                    continue;
                }

                AddLine(rawLine, origIdx);
            }

            lineMap = map.ToArray();
            return string.Join("\n", output);
        }

        /// <summary>
        /// Replaces lines [<paramref name="startLineZeroBased"/>..<paramref name="endLineZeroBased"/>]
        /// with <paramref name="replacementSlice"/> while preserving line endings.
        /// </summary>
        public static string ReplaceLineRange(string markdown, int startLineZeroBased, int endLineZeroBased, string replacementSlice)
        {
            string lineEnding = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = new List<string>(markdown.Replace("\r\n", "\n").Split('\n'));
            if (lines.Count == 0)
                return replacementSlice ?? string.Empty;

            int start = Math.Clamp(startLineZeroBased, 0, lines.Count - 1);
            int end = Math.Clamp(endLineZeroBased, start, lines.Count - 1);
            int removeCount = (end - start) + 1;
            lines.RemoveRange(start, removeCount);

            var inserted = (replacementSlice ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            lines.InsertRange(start, inserted);

            return string.Join(lineEnding, lines);
        }

        private Control? RenderParsedBlock(Block block, int baseLineZeroBased, bool isCollapsed)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    return RenderHeading(heading, baseLineZeroBased, isCollapsed);

                case MathBlock mathBlock:
                    return RenderLatexCard(mathBlock.Lines.ToString().Trim(), "$$ Display Math", baseLineZeroBased);

                case FencedCodeBlock fenced:
                {
                    string info = (fenced.Info ?? string.Empty).Trim().ToLowerInvariant();
                    string body = fenced.Lines.ToString();
                    if (info is "latex" or "math" or "tex")
                        return RenderLatexCard(body.Trim(), $"```{info} block", baseLineZeroBased);
                    if (info is "drawing" or "ndraw")
                        return RenderInlineDrawingCard(body, baseLineZeroBased);
                    return RenderCodeCard(body.TrimEnd(), string.IsNullOrEmpty(info) ? "text" : info);
                }

                case CodeBlock codeBlock:
                    return RenderCodeCard(codeBlock.Lines.ToString().TrimEnd(), "code");

                case ParagraphBlock paragraph:
                    return RenderParagraph(paragraph);

                case QuoteBlock quote:
                    return RenderQuote(quote, baseLineZeroBased);

                case ListBlock list:
                    return RenderList(list, baseLineZeroBased);

                case Table table:
                    return RenderTable(table);

                case ThematicBreakBlock:
                    return new Border
                    {
                        Height = 1,
                        Margin = new Thickness(0, 6),
                        Background = BorderBrushColor()
                    };

                default:
                    return null;
            }
        }

        private Button CreateFoldToggleButton(int headingStartLineZeroBased, int level, bool isCollapsed)
        {
            var foldBtn = new Button
            {
                Content = isCollapsed ? "▶" : "▼",
                FontSize = 11,
                MinWidth = 22,
                MinHeight = 20,
                Padding = new Thickness(3, 1),
                Margin = new Thickness(0, 2, 6, 0),
                CornerRadius = new CornerRadius(4),
                Background = isCollapsed ? AccentTintBrush() : Brushes.Transparent,
                Foreground = isCollapsed ? AccentBrush() : MutedBrush(),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(foldBtn, isCollapsed ? $"Expand H{level} section" : $"Collapse H{level} section");
            foldBtn.Click += (_, _) => TogglePreviewHeadingFold(headingStartLineZeroBased);
            return foldBtn;
        }

        private Control RenderHeading(HeadingBlock heading, int origStart, bool isCollapsed)
        {
            double size = heading.Level switch
            {
                1 => _baseFontSize + 11.5,
                2 => _baseFontSize + 6.5,
                3 => _baseFontSize + 3.5,
                4 => _baseFontSize + 1.5,
                5 => _baseFontSize,
                _ => _baseFontSize - 1.0
            };

            var tb = CreateInlineTextBlock(size, FontWeight.Bold, TextBrush());
            if (heading.Inline is not null)
                AppendInlineContainer(tb.Inlines!, heading.Inline, size, FontWeight.Bold, FontStyle.Normal, strikethrough: false);

            Control headingContent = tb;
            if (heading.Level <= 5)
            {
                var foldBtn = CreateFoldToggleButton(origStart, heading.Level, isCollapsed);

                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions(isCollapsed ? "Auto,*,Auto" : "Auto,*")
                };
                Grid.SetColumn(foldBtn, 0);
                Grid.SetColumn(tb, 1);
                row.Children.Add(foldBtn);
                row.Children.Add(tb);

                if (isCollapsed)
                {
                    var badge = new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2),
                        Margin = new Thickness(8, 0, 0, 0),
                        Background = AccentTintBrush(),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = "⋯ Section hidden",
                            FontSize = 11,
                            Foreground = AccentBrush()
                        }
                    };
                    Grid.SetColumn(badge, 2);
                    row.Children.Add(badge);
                }

                headingContent = row;
            }

            if (heading.Level <= 2)
            {
                return new StackPanel
                {
                    Spacing = 4,
                    Margin = new Thickness(0, heading.Level == 1 ? 4 : 8, 0, 2),
                    Children =
                    {
                        headingContent,
                        new Border { Height = 1, Background = BorderBrushColor() }
                    }
                };
            }

            if (headingContent is Control ctrl)
                ctrl.Margin = new Thickness(0, 6, 0, 0);
            return headingContent;
        }

        private Control RenderLatexCard(string latex, string badgeText, int startLine)
        {
            var mathCtrl = new LatexMathControl
            {
                LatexSource = latex,
                FontSize = 20,
                IsDisplayMode = true,
                Foreground = TextBrush(),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4, 6)
            };

            var warn = new TextBlock
            {
                Text = mathCtrl.DiagnosticsText,
                FontSize = 11,
                Foreground = WarningBrush(),
                Margin = new Thickness(8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = mathCtrl.HasWarnings
            };

            var badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2),
                Background = AccentTintBrush(),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = $"{badgeText} (L{startLine + 1})",
                    FontSize = 10.5,
                    Foreground = AccentBrush(),
                    FontWeight = FontWeight.SemiBold
                }
            };

            var topRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { badge, warn }
            };

            return new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = BorderBrushColor(),
                Background = CardBrush(),
                Padding = new Thickness(12, 8),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children = { topRow, mathCtrl }
                }
            };
        }

        public static RenderTargetBitmap RenderCanvasToBitmap(DrawingDocument doc, int? width = null, int? height = null)
        {
            var used = doc.GetUsedBounds(padding: 16.0);
            int targetW = Math.Clamp(width ?? (int)Math.Ceiling(used.Width), 64, 2048);
            int targetH = Math.Clamp(height ?? (int)Math.Ceiling(used.Height), 48, 2048);

            var canvas = new DrawingCanvasControl
            {
                IsDarkTheme = false,
                Width = targetW,
                Height = targetH
            };
            canvas.LoadDocument(doc);
            if (!width.HasValue && !height.HasValue)
                canvas.SetPanOffset(new Point(-used.X, -used.Y));
            canvas.ApplyPaletteColors("#FFFFFF", "#E5E5E5");
            canvas.Measure(new Size(targetW, targetH));
            canvas.Arrange(new Rect(0, 0, targetW, targetH));

            var bitmap = new RenderTargetBitmap(new PixelSize(targetW, targetH), new Vector(96, 96));
            bitmap.Render(canvas);
            return bitmap;
        }

        private void UpdateInlineFenceMarkdownInPlace(int openFenceLine, string updatedJson)
        {
            _markdown = MainWindow.ReplaceFencedBlockBody(_markdown, openFenceLine, updatedJson);
            var seg = _lastSegments.FirstOrDefault(s => s.StartLine == openFenceLine);
            if (seg is not null)
                seg.RawText = MainWindow.ReplaceFencedBlockBody(seg.RawText, 0, updatedJson);
        }

        private static ContextMenu BuildImageContextMenu(Action onEdit, Action? onCopy, Action onDelete)
        {
            var menu = new ContextMenu();

            var editItem = new MenuItem { Header = "✎ Edit Image" };
            editItem.Click += (_, _) => onEdit();
            menu.Items.Add(editItem);

            var copyItem = new MenuItem
            {
                Header = "📋 Copy Image",
                IsEnabled = onCopy is not null
            };
            if (onCopy is not null)
                copyItem.Click += (_, _) => onCopy();
            menu.Items.Add(copyItem);

            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "🗑 Delete Image" };
            deleteItem.Click += (_, _) => onDelete();
            menu.Items.Add(deleteItem);

            return menu;
        }

        private Control RenderInlineDrawingCard(string json, int openFenceLine, int? closeFenceLine = null)
        {
            var doc = DrawingDocument.FromJson(json);
            bool isEditorOpen = _openInlineImageEditors.Contains(openFenceLine);
            DrawingWorkspaceControl? activeWorkspace = null;
            int resolvedEndLine = closeFenceLine ?? (_lastSegments.FirstOrDefault(s => s.StartLine == openFenceLine)?.EndLine ?? openFenceLine);

            void SyncInlineDrawing(DrawingWorkspaceControl ws)
            {
                _inlineDrawingHistories[openFenceLine] = ws.Canvas.GetHistoryState();
                string updatedJson = ws.Canvas.Document.ToJson();
                UpdateInlineFenceMarkdownInPlace(openFenceLine, updatedJson);
                SaveInlineCanvasToFolder?.Invoke(openFenceLine, ws.Canvas.Document);
                InlineDrawingUpdated?.Invoke(this, new InlineDrawingUpdateEventArgs(openFenceLine, updatedJson));
            }

            return BuildInteractiveDrawingImageBlock(
                doc,
                isEditorOpen,
                $"Inline Drawing Image (Line {openFenceLine + 1})",
                startLineZeroBased: openFenceLine,
                endLineZeroBased: resolvedEndLine,
                onOpenEditor: () => OpenInlineImageEditor(openFenceLine),
                onCloseEditor: () =>
                {
                    if (activeWorkspace is not null)
                        SyncInlineDrawing(activeWorkspace);
                    CloseInlineImageEditor(openFenceLine);
                },
                sourceJumpLine: openFenceLine + 1,
                getLatestDoc: () => activeWorkspace?.Canvas.Document ?? doc,
                wireWorkspace: workspace =>
                {
                    activeWorkspace = workspace;
                    if (_inlineDrawingHistories.TryGetValue(openFenceLine, out var savedHistory))
                        workspace.Canvas.RestoreHistoryState(savedHistory.Undo, savedHistory.Redo);

                    void Activate()
                    {
                        LastActiveInlineCanvas = workspace.Canvas;
                        InlineCanvasActivated?.Invoke(this, workspace.Canvas);
                    }

                    workspace.Canvas.PointerPressed += (_, _) => Activate();
                    workspace.Canvas.GotFocus += (_, _) => Activate();
                    workspace.Canvas.HistoryChanged += (_, _) =>
                    {
                        _inlineDrawingHistories[openFenceLine] = workspace.Canvas.GetHistoryState();
                        Activate();
                    };
                    workspace.DocumentChanged += (_, _) =>
                    {
                        Activate();
                        SyncInlineDrawing(workspace);
                    };
                });
        }

        private Control RenderEmbeddedNdrawCard(string targetName, int startLine = 0, int? endLine = null)
        {
            var doc = _embeddedDrawingCache.TryGetValue(targetName, out var cached)
                ? cached.Clone()
                : (ResolveEmbeddedDrawing?.Invoke(targetName) ?? new DrawingDocument());
            bool isEditorOpen = _openEmbeddedImageEditors.Contains(targetName);
            DrawingWorkspaceControl? activeWorkspace = null;
            int resolvedEndLine = endLine ?? startLine;

            void SyncEmbeddedDrawing(DrawingWorkspaceControl ws)
            {
                _embeddedDrawingCache[targetName] = ws.Canvas.Document.Clone();
                ExternalDrawingUpdated?.Invoke(this, new ExternalDrawingUpdateEventArgs(targetName, ws.Canvas.Document.ToJson()));
            }

            return BuildInteractiveDrawingImageBlock(
                doc,
                isEditorOpen,
                $"Embedded Drawing Image: {targetName}",
                startLineZeroBased: startLine,
                endLineZeroBased: resolvedEndLine,
                onOpenEditor: () =>
                {
                    _openEmbeddedImageEditors.Add(targetName);
                    RenderMarkdown(_markdown);
                },
                onCloseEditor: () =>
                {
                    if (activeWorkspace is not null)
                        SyncEmbeddedDrawing(activeWorkspace);
                    if (_openEmbeddedImageEditors.Remove(targetName))
                        RenderMarkdown(_markdown);
                },
                sourceJumpLine: null,
                getLatestDoc: () => activeWorkspace?.Canvas.Document ?? _embeddedDrawingCache.GetValueOrDefault(targetName) ?? doc,
                wireWorkspace: workspace =>
                {
                    activeWorkspace = workspace;
                    void Activate()
                    {
                        LastActiveInlineCanvas = workspace.Canvas;
                        InlineCanvasActivated?.Invoke(this, workspace.Canvas);
                    }

                    workspace.Canvas.PointerPressed += (_, _) => Activate();
                    workspace.Canvas.GotFocus += (_, _) => Activate();
                    workspace.Canvas.HistoryChanged += (_, _) => Activate();
                    workspace.DocumentChanged += (_, _) =>
                    {
                        Activate();
                        SyncEmbeddedDrawing(workspace);
                    };
                });
        }

        private Control BuildInteractiveDrawingImageBlock(
            DrawingDocument doc,
            bool isEditorOpen,
            string title,
            int startLineZeroBased,
            int endLineZeroBased,
            Action onOpenEditor,
            Action onCloseEditor,
            int? sourceJumpLine,
            Func<DrawingDocument> getLatestDoc,
            Action<DrawingWorkspaceControl> wireWorkspace)
        {
            Action deleteAction = () => DeleteImageBlockAtLineRange(startLineZeroBased, endLineZeroBased);
            Action copyAction = () => CopyImageRequested?.Invoke(this, RenderCanvasToBitmap(getLatestDoc()));

            if (isEditorOpen)
            {
                var workspace = new DrawingWorkspaceControl(doc, compactInlineMode: true, isDarkTheme: _isDarkTheme);
                workspace.ApplyThemePalette(_palette, _isDarkTheme);
                wireWorkspace(workspace);

                var headerRow = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 6,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                headerRow.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = AccentBrush(),
                    VerticalAlignment = VerticalAlignment.Center
                });

                var doneBtn = new Button
                {
                    Content = "✓ Done Editing Image",
                    FontSize = 11,
                    Padding = new Thickness(8, 3),
                    CornerRadius = new CornerRadius(4),
                    Background = ThemeConfigService.ParseBrush(_palette.AccentColor, "#0078D4"),
                    Foreground = Brushes.White
                };
                doneBtn.Click += (_, _) => onCloseEditor();

                var copyBtn = new Button
                {
                    Content = "📋 Copy Image",
                    FontSize = 11,
                    Padding = new Thickness(7, 3),
                    CornerRadius = new CornerRadius(4)
                };
                copyBtn.Click += (_, _) => copyAction();

                var deleteBtn = new Button
                {
                    Content = "🗑 Delete Image",
                    FontSize = 11,
                    Padding = new Thickness(7, 3),
                    CornerRadius = new CornerRadius(4)
                };
                deleteBtn.Click += (_, _) => deleteAction();

                var openActions = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { doneBtn, copyBtn, deleteBtn }
                };

                if (sourceJumpLine.HasValue)
                {
                    int targetLine = sourceJumpLine.Value;
                    var jumpBtn = new Button
                    {
                        Content = "Source",
                        FontSize = 11,
                        Padding = new Thickness(7, 3),
                        CornerRadius = new CornerRadius(4)
                    };
                    jumpBtn.Click += (_, _) => LineFocusRequested?.Invoke(this, targetLine);
                    openActions.Children.Add(jumpBtn);
                }

                Grid.SetColumn(openActions, 1);
                headerRow.Children.Add(openActions);

                return new StackPanel
                {
                    Spacing = 4,
                    Children = { headerRow, workspace }
                };
            }

            var used = doc.GetUsedBounds(padding: 20.0);
            double maxPreviewW = 860.0;
            double maxPreviewH = 420.0;
            double fitScale = Math.Min(1.0, Math.Min(maxPreviewW / Math.Max(1.0, used.Width), maxPreviewH / Math.Max(1.0, used.Height)));
            double previewW = Math.Clamp(Math.Ceiling(used.Width * fitScale), 160.0, maxPreviewW);
            double previewH = Math.Clamp(Math.Ceiling(used.Height * fitScale), 96.0, maxPreviewH);

            var previewCanvas = new DrawingCanvasControl
            {
                IsDarkTheme = false,
                IsHitTestVisible = false,
                Width = previewW,
                Height = previewH
            };
            previewCanvas.LoadDocument(doc);
            if (fitScale < 0.999)
                previewCanvas.SetZoom(fitScale);
            previewCanvas.SetPanOffset(new Point(-used.X * fitScale, -used.Y * fitScale));
            previewCanvas.ApplyPaletteColors("#FFFFFF", "#E5E5E5");
            previewCanvas.AttachedToVisualTree += (_, _) => previewCanvas.InvalidateVisual();

            var overlayHint = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 4),
                Margin = new Thickness(0, 0, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Color.FromArgb(225, 15, 23, 42)),
                Child = new TextBlock
                {
                    Text = "🎨 Click to edit • Right-click for menu",
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White
                }
            };

            var clickableOverlay = new Grid
            {
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = new Cursor(StandardCursorType.Hand),
                Children = { previewCanvas, overlayHint }
            };
            clickableOverlay.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(clickableOverlay).Properties.IsLeftButtonPressed)
                {
                    onOpenEditor();
                    e.Handled = true;
                }
            };

            var imageFrame = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = BorderBrushColor(),
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                ClipToBounds = true,
                Focusable = true,
                ContextMenu = BuildImageContextMenu(onOpenEditor, copyAction, deleteAction),
                Child = clickableOverlay
            };
            imageFrame.KeyDown += (_, ke) =>
            {
                if (ke.Key is Key.Delete or Key.Back)
                {
                    deleteAction();
                    ke.Handled = true;
                }
            };

            var collapsedHeader = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 6,
                Margin = new Thickness(0, 0, 0, 3)
            };
            collapsedHeader.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = MutedBrush(),
                VerticalAlignment = VerticalAlignment.Center
            });

            var copyCollapsedBtn = new Button
            {
                Content = "📋 Copy Image",
                FontSize = 11,
                Padding = new Thickness(7, 2),
                CornerRadius = new CornerRadius(4)
            };
            copyCollapsedBtn.Click += (_, _) => copyAction();

            var editImgBtn = new Button
            {
                Content = "Edit Image",
                FontSize = 11,
                Padding = new Thickness(7, 2),
                CornerRadius = new CornerRadius(4)
            };
            editImgBtn.Click += (_, _) => onOpenEditor();

            var deleteCollapsedBtn = new Button
            {
                Content = "🗑 Delete",
                FontSize = 11,
                Padding = new Thickness(7, 2),
                CornerRadius = new CornerRadius(4)
            };
            deleteCollapsedBtn.Click += (_, _) => deleteAction();

            var collapsedActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children = { copyCollapsedBtn, editImgBtn, deleteCollapsedBtn }
            };
            Grid.SetColumn(collapsedActions, 1);
            collapsedHeader.Children.Add(collapsedActions);

            return new StackPanel
            {
                Spacing = 3,
                Children = { collapsedHeader, imageFrame }
            };
        }

        private Control RenderCodeCard(string code, string language)
        {
            var copyBtn = new Button
            {
                Content = "Copy",
                FontSize = 11,
                Padding = new Thickness(7, 2),
                CornerRadius = new CornerRadius(4)
            };
            copyBtn.Click += async (_, _) =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard is not null)
                    await topLevel.Clipboard.SetTextAsync(code);
            };

            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            header.Children.Add(new TextBlock
            {
                Text = language,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = MutedBrush(),
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(copyBtn, 1);
            header.Children.Add(copyBtn);

            var codeText = new SelectableTextBlock
            {
                Text = code,
                FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace"),
                FontSize = 13,
                Foreground = TextBrush(),
                TextWrapping = TextWrapping.Wrap
            };

            return new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = BorderBrushColor(),
                Background = CodeCardBrush(),
                Padding = new Thickness(12, 8),
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children = { header, codeText }
                }
            };
        }

        private Control RenderParagraph(ParagraphBlock paragraph)
        {
            var tb = CreateInlineTextBlock(_baseFontSize, FontWeight.Normal, TextBrush());
            if (paragraph.Inline is not null)
                AppendInlineContainer(tb.Inlines!, paragraph.Inline, _baseFontSize, FontWeight.Normal, FontStyle.Normal, strikethrough: false);
            return tb;
        }

        public static string ExtractCleanEmbedTarget(string rawInner, out string? pipeParam, out int? customWidth)
        {
            customWidth = null;
            string cleanTarget = DocumentAssetService.CleanObsidianImageTarget(rawInner, out pipeParam);
            if (!string.IsNullOrWhiteSpace(pipeParam))
            {
                string dimToken = pipeParam.Trim();
                int xIdx = dimToken.IndexOfAny(new[] { 'x', 'X' });
                string widthPart = xIdx > 0 ? dimToken[..xIdx] : dimToken;
                if (int.TryParse(widthPart, out int parsedW) && parsedW > 0)
                    customWidth = Math.Clamp(parsedW, 32, 1600);
            }
            return cleanTarget;
        }

        private static bool TryParseMarkdownImageLine(string trimmed, out string alt, out string url, out int? customWidth)
        {
            alt = string.Empty;
            url = string.Empty;
            customWidth = null;

            var m = MarkdownImageLineRegex.Match(trimmed);
            if (!m.Success)
                return false;

            string rawAlt = m.Groups[1].Value.Trim();
            string rawUrl = m.Groups[2].Value.Trim();

            int pipeIdx = rawAlt.IndexOf('|');
            if (pipeIdx >= 0)
            {
                string right = rawAlt[(pipeIdx + 1)..].Trim();
                rawAlt = rawAlt[..pipeIdx].Trim();
                int xIdx = right.IndexOfAny(new[] { 'x', 'X' });
                string widthPart = xIdx > 0 ? right[..xIdx] : right;
                if (int.TryParse(widthPart, out int parsedW) && parsedW > 0)
                    customWidth = Math.Clamp(parsedW, 32, 1600);
            }

            alt = rawAlt;
            url = DocumentAssetService.CleanObsidianImageTarget(rawUrl, out _);
            return true;
        }

        private static bool IsImageExtension(string path)
        {
            string cleaned = DocumentAssetService.CleanObsidianImageTarget(path, out _);
            try
            {
                string ext = Path.GetExtension(cleaned).ToLowerInvariant();
                return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".svg";
            }
            catch
            {
                return false;
            }
        }

        private string? TryResolveImageFile(string urlOrTarget)
        {
            if (string.IsNullOrWhiteSpace(urlOrTarget))
                return null;
            string cleaned = DocumentAssetService.CleanObsidianImageTarget(urlOrTarget, out _);
            string? resolved = ResolveVaultImagePath?.Invoke(cleaned);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                return resolved;
            try
            {
                return File.Exists(cleaned) ? Path.GetFullPath(cleaned) : null;
            }
            catch
            {
                return null;
            }
        }

        private Control RenderMarkdownImageCard(string urlOrTarget, string altText, int origStart, int origEnd, int? requestedWidth = null)
        {
            string editorKey = $"{origStart}:{urlOrTarget}";
            bool isEditorOpen = _openMarkdownImageEditors.Contains(editorKey);
            string? resolvedFile = TryResolveImageFile(urlOrTarget);
            Action editAction = () =>
            {
                _openMarkdownImageEditors.Add(editorKey);
                RenderMarkdown(_markdown);
            };
            Action deleteAction = () => DeleteImageBlockAtLineRange(origStart, origEnd);

            if (!isEditorOpen)
            {
                Control imageBody;
                Bitmap? loadedBitmap = null;
                if (!string.IsNullOrWhiteSpace(resolvedFile) && !resolvedFile.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        loadedBitmap = new Bitmap(resolvedFile);
                        double natW = Math.Max(1, loadedBitmap.PixelSize.Width);
                        double natH = Math.Max(1, loadedBitmap.PixelSize.Height);
                        double maxW = requestedWidth.HasValue ? Math.Clamp(requestedWidth.Value, 32.0, 960.0) : 760.0;
                        double scale = requestedWidth.HasValue ? maxW / natW : 1.0;
                        if (natW * scale > maxW)
                            scale = maxW / natW;
                        if (natH * scale > 520.0)
                            scale = Math.Min(scale, 520.0 / natH);
                        double displayW = Math.Max(32.0, Math.Round(natW * scale));
                        double displayH = Math.Max(24.0, Math.Round(natH * scale));

                        imageBody = new Image
                        {
                            Source = loadedBitmap,
                            Width = displayW,
                            Height = displayH,
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top
                        };
                    }
                    catch
                    {
                        imageBody = BuildMissingImageVisual(urlOrTarget, altText);
                    }
                }
                else
                {
                    imageBody = BuildMissingImageVisual(urlOrTarget, altText);
                }

                Action? copyAction = loadedBitmap is not null
                    ? () => CopyImageRequested?.Invoke(this, loadedBitmap)
                    : null;

                var overlayHint = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(7, 3),
                    Margin = new Thickness(6),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Opacity = 0.88,
                    Background = new SolidColorBrush(Color.FromArgb(215, 15, 23, 42)),
                    Child = new TextBlock
                    {
                        Text = "✎ Edit",
                        FontSize = 10.5,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White
                    }
                };

                var clickable = new Grid
                {
                    Background = Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Children = { imageBody, overlayHint }
                };
                if (loadedBitmap is null)
                    clickable.MinHeight = 100;
                clickable.PointerPressed += (_, e) =>
                {
                    if (e.GetCurrentPoint(clickable).Properties.IsLeftButtonPressed)
                    {
                        editAction();
                        e.Handled = true;
                    }
                };

                var imgHeader = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Margin = new Thickness(0, 0, 0, 3)
                };
                imgHeader.Children.Add(new TextBlock
                {
                    Text = $"Image: {(string.IsNullOrWhiteSpace(altText) ? urlOrTarget : altText)}",
                    FontSize = 11.5,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = MutedBrush(),
                    VerticalAlignment = VerticalAlignment.Center
                });

                var headerActions = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 5
                };
                if (copyAction is not null)
                {
                    var copyImgBtn = new Button
                    {
                        Content = "📋 Copy Image",
                        FontSize = 11,
                        Padding = new Thickness(7, 2),
                        CornerRadius = new CornerRadius(4)
                    };
                    copyImgBtn.Click += (_, _) => copyAction();
                    headerActions.Children.Add(copyImgBtn);
                }

                var editImgBtn = new Button
                {
                    Content = "✎ Edit",
                    FontSize = 11,
                    Padding = new Thickness(7, 2),
                    CornerRadius = new CornerRadius(4)
                };
                editImgBtn.Click += (_, _) => editAction();
                headerActions.Children.Add(editImgBtn);

                var deleteImgBtn = new Button
                {
                    Content = "🗑 Delete",
                    FontSize = 11,
                    Padding = new Thickness(7, 2),
                    CornerRadius = new CornerRadius(4)
                };
                deleteImgBtn.Click += (_, _) => deleteAction();
                headerActions.Children.Add(deleteImgBtn);

                Grid.SetColumn(headerActions, 1);
                imgHeader.Children.Add(headerActions);

                var imgFrame = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    BorderBrush = BorderBrushColor(),
                    Background = Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    ClipToBounds = true,
                    Focusable = true,
                    ContextMenu = BuildImageContextMenu(editAction, copyAction, deleteAction),
                    Child = clickable
                };
                imgFrame.KeyDown += (_, ke) =>
                {
                    if (ke.Key is Key.Delete or Key.Back)
                    {
                        deleteAction();
                        ke.Handled = true;
                    }
                };

                return new StackPanel
                {
                    Spacing = 3,
                    Children = { imgHeader, imgFrame }
                };
            }

            var altBox = new TextBox
            {
                Text = altText,
                PlaceholderText = "Image caption / alt text",
                FontSize = 12.5
            };
            var urlBox = new TextBox
            {
                Text = urlOrTarget,
                PlaceholderText = "Image path or URL (e.g. Assets/diagram.png)",
                FontSize = 12.5,
                FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace")
            };

            var applyBtn = new Button
            {
                Content = "Apply Image Edit",
                FontSize = 11,
                Padding = new Thickness(8, 3),
                CornerRadius = new CornerRadius(4),
                Background = ThemeConfigService.ParseBrush(_palette.AccentColor, "#0078D4"),
                Foreground = Brushes.White
            };
            applyBtn.Click += (_, _) =>
            {
                string nextAlt = (altBox.Text ?? string.Empty).Trim();
                string nextUrl = (urlBox.Text ?? string.Empty).Trim();
                string updatedMd = ReplaceLineRange(_markdown, origStart, origEnd, $"![{nextAlt}]({nextUrl})");
                _openMarkdownImageEditors.Remove(editorKey);
                RenderMarkdown(updatedMd);
                DocumentEditedFromPreview?.Invoke(this, updatedMd);
            };

            var convertDrawBtn = new Button
            {
                Content = "Open Drawing Canvas Here",
                FontSize = 11,
                Padding = new Thickness(8, 3),
                CornerRadius = new CornerRadius(4)
            };
            convertDrawBtn.Click += (_, _) =>
            {
                var doc = new DrawingDocument { Background = CanvasBackgroundKind.Grid };
                string replacement;
                if (CreateSavedDocumentCanvas is not null)
                {
                    var created = CreateSavedDocumentCanvas(doc);
                    replacement = created.EmbedMarkdown;
                    _openEmbeddedImageEditors.Add($"canvas/{created.FileName}");
                }
                else
                {
                    replacement = $"```drawing\n{doc.ToJson()}\n```";
                    _openInlineImageEditors.Add(origStart);
                }
                string updatedMd = ReplaceLineRange(_markdown, origStart, origEnd, replacement);
                _openMarkdownImageEditors.Remove(editorKey);
                RenderMarkdown(updatedMd);
                DocumentEditedFromPreview?.Invoke(this, updatedMd);
            };

            var closeBtn = new Button
            {
                Content = "Close",
                FontSize = 11,
                Padding = new Thickness(8, 3),
                CornerRadius = new CornerRadius(4)
            };
            closeBtn.Click += (_, _) =>
            {
                _openMarkdownImageEditors.Remove(editorKey);
                RenderMarkdown(_markdown);
            };

            var deleteInEditorBtn = new Button
            {
                Content = "🗑 Delete Image",
                FontSize = 11,
                Padding = new Thickness(8, 3),
                CornerRadius = new CornerRadius(4)
            };
            deleteInEditorBtn.Click += (_, _) => deleteAction();

            return new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = ThemeConfigService.ParseBrush(_palette.AccentColor, "#0078D4"),
                Background = CardBrush(),
                Padding = new Thickness(12, 10),
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Image Editor (Line {origStart + 1})",
                            FontSize = 12,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = AccentBrush()
                        },
                        altBox,
                        urlBox,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 6,
                            Children = { deleteInEditorBtn, convertDrawBtn, closeBtn, applyBtn }
                        }
                    }
                }
            };
        }

        private static Control BuildMissingImageVisual(string urlOrTarget, string altText) => new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock
                {
                    Text = $"🖼 {(string.IsNullOrWhiteSpace(altText) ? "Markdown Image" : altText)}",
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(urlOrTarget) ? "(no path set)" : urlOrTarget,
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(Color.Parse("#475569")),
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        };

        private Control RenderQuote(QuoteBlock quote, int baseLineZeroBased)
        {
            var inner = new StackPanel { Spacing = 4 };
            foreach (Block child in quote)
            {
                var visual = RenderParsedBlock(child, baseLineZeroBased, isCollapsed: false);
                if (visual is not null)
                    inner.Children.Add(visual);
            }

            return new Border
            {
                BorderThickness = new Thickness(4, 0, 0, 0),
                BorderBrush = AccentBrush(),
                Background = CardBrush(),
                CornerRadius = new CornerRadius(0, 6, 6, 0),
                Padding = new Thickness(12, 6),
                Child = inner
            };
        }

        private Control RenderList(ListBlock list, int baseLineZeroBased)
        {
            var stack = new StackPanel
            {
                Spacing = 4,
                Margin = new Thickness(8, 1, 0, 1)
            };

            int idx = int.TryParse(list.OrderedStart, out int startNum) ? startNum : 1;
            foreach (Block block in list)
            {
                if (block is not ListItemBlock item) continue;

                bool isTask = false;
                bool isChecked = false;
                var contentStack = new StackPanel { Spacing = 3 };

                foreach (Block child in item)
                {
                    if (child is ParagraphBlock p && p.Inline?.FirstChild is TaskList task)
                    {
                        isTask = true;
                        isChecked = task.Checked;
                        var tb = CreateInlineTextBlock(_baseFontSize, FontWeight.Normal, isChecked ? MutedBrush() : TextBrush());
                        AppendInlineContainer(tb.Inlines!, p.Inline, _baseFontSize, FontWeight.Normal, FontStyle.Normal, strikethrough: isChecked, skipTaskPrefix: true);
                        contentStack.Children.Add(tb);
                    }
                    else
                    {
                        var visual = RenderParsedBlock(child, baseLineZeroBased + item.Line, isCollapsed: false);
                        if (visual is not null)
                            contentStack.Children.Add(visual);
                    }
                }

                Control bulletControl;
                if (isTask)
                {
                    int lineZeroBased = baseLineZeroBased + item.Line;
                    var cb = new CheckBox
                    {
                        IsChecked = isChecked,
                        MinWidth = 22,
                        MinHeight = 20,
                        Padding = new Thickness(2, 0),
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 1, 6, 0)
                    };
                    cb.IsCheckedChanged += (_, _) =>
                    {
                        TaskItemToggled?.Invoke(this, new TaskToggleEventArgs(lineZeroBased, cb.IsChecked == true));
                    };
                    bulletControl = cb;
                }
                else
                {
                    bulletControl = new TextBlock
                    {
                        Text = list.IsOrdered ? $"{idx}." : "•",
                        FontSize = _baseFontSize,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = AccentBrush(),
                        Margin = new Thickness(0, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Top
                    };
                }

                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*")
                };
                Grid.SetColumn(bulletControl, 0);
                Grid.SetColumn(contentStack, 1);
                row.Children.Add(bulletControl);
                row.Children.Add(contentStack);

                stack.Children.Add(row);
                idx++;
            }

            return stack;
        }

        private Control RenderTable(Table table)
        {
            var rows = table.OfType<TableRow>().ToList();
            if (rows.Count == 0)
                return new Border();

            int colCount = rows.Max(r => r.Count);
            var grid = new Grid();
            for (int c = 0; c < colCount; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (int r = 0; r < rows.Count; r++)
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                for (int c = 0; c < row.Count; c++)
                {
                    if (row[c] is not TableCell cell) continue;

                    var cellContent = new StackPanel { Spacing = 4 };
                    foreach (Block b in cell)
                    {
                        if (b is ParagraphBlock p && p.Inline is not null)
                        {
                            var tb = CreateInlineTextBlock(13.5, row.IsHeader ? FontWeight.Bold : FontWeight.Normal, TextBrush());
                            AppendInlineContainer(tb.Inlines!, p.Inline, 13.5, row.IsHeader ? FontWeight.Bold : FontWeight.Normal, FontStyle.Normal, false);
                            cellContent.Children.Add(tb);
                        }
                    }

                    var cellBorder = new Border
                    {
                        Padding = new Thickness(10, 6),
                        BorderThickness = new Thickness(0, 0, c < colCount - 1 ? 1 : 0, r < rows.Count - 1 ? 1 : 0),
                        BorderBrush = BorderBrushColor(),
                        Background = row.IsHeader
                            ? AccentTintBrush()
                            : (r % 2 == 1 ? CardBrush() : Brushes.Transparent),
                        Child = cellContent
                    };

                    Grid.SetRow(cellBorder, r);
                    Grid.SetColumn(cellBorder, c);
                    grid.Children.Add(cellBorder);
                }
            }

            return new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = BorderBrushColor(),
                ClipToBounds = true,
                Child = grid
            };
        }

        private void AppendInlineContainer(
            InlineCollection inlines,
            ContainerInline container,
            double fontSize,
            FontWeight weight,
            FontStyle style,
            bool strikethrough,
            bool skipTaskPrefix = false)
        {
            foreach (MdInline inline in container)
            {
                if (skipTaskPrefix && inline is TaskList)
                    continue;

                AppendInline(inlines, inline, fontSize, weight, style, strikethrough);
            }
        }

        private void AppendInline(
            InlineCollection inlines,
            MdInline inline,
            double fontSize,
            FontWeight weight,
            FontStyle style,
            bool strikethrough)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    AppendLiteralWithObsidianTokens(inlines, literal.Content.ToString(), fontSize, weight, style, strikethrough);
                    break;

                case MathInline math:
                {
                    var mathCtrl = new LatexMathControl
                    {
                        LatexSource = math.Content.ToString(),
                        FontSize = Math.Max(14, fontSize + 1),
                        IsDisplayMode = math.DelimiterCount == 2,
                        Foreground = TextBrush(),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    inlines.Add(new InlineUIContainer(mathCtrl) { BaselineAlignment = BaselineAlignment.Center });
                    break;
                }

                case CodeInline code:
                {
                    var codeRun = new Run($" {code.Content} ")
                    {
                        FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace"),
                        FontSize = Math.Max(12.5, fontSize - 1.0),
                        Foreground = AccentBrush(),
                        Background = CodeCardBrush()
                    };
                    if (strikethrough)
                        codeRun.TextDecorations = TextDecorations.Strikethrough;
                    inlines.Add(codeRun);
                    break;
                }

                case EmphasisInline emphasis:
                {
                    FontWeight nextWeight = weight;
                    FontStyle nextStyle = style;
                    bool nextStrike = strikethrough;

                    if (emphasis.DelimiterChar == '~')
                        nextStrike = true;
                    else if (emphasis.DelimiterCount >= 2)
                        nextWeight = FontWeight.Bold;
                    else
                        nextStyle = FontStyle.Italic;

                    AppendInlineContainer(inlines, emphasis, fontSize, nextWeight, nextStyle, nextStrike);
                    break;
                }

                case LinkInline link:
                {
                    string label = ExtractPlainText(link);
                    if (string.IsNullOrWhiteSpace(label))
                        label = link.Url ?? (link.IsImage ? "image" : "link");

                    if (link.IsImage && !string.IsNullOrWhiteSpace(link.Url))
                    {
                        string? resolvedImg = TryResolveImageFile(link.Url);
                        if (!string.IsNullOrWhiteSpace(resolvedImg) && !resolvedImg.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var thumb = new Image
                                {
                                    Source = new Bitmap(resolvedImg),
                                    MaxHeight = 48,
                                    MaxWidth = 120,
                                    Stretch = Stretch.Uniform
                                };
                                var thumbWrap = new Border
                                {
                                    Background = Brushes.White,
                                    CornerRadius = new CornerRadius(4),
                                    Padding = new Thickness(3),
                                    Margin = new Thickness(2, 0),
                                    Child = thumb
                                };
                                ToolTip.SetTip(thumbWrap, $"Image: {label} ({link.Url})");
                                inlines.Add(new InlineUIContainer(thumbWrap) { BaselineAlignment = BaselineAlignment.Center });
                                break;
                            }
                            catch
                            {
                                // Fall through to inline badge
                            }
                        }
                    }

                    var linkBtn = new Button
                    {
                        Content = link.IsImage ? $"🖼 {label}" : label,
                        FontSize = fontSize - 0.5,
                        MinHeight = 0,
                        Padding = new Thickness(4, 0),
                        Margin = new Thickness(1, 0),
                        CornerRadius = new CornerRadius(4),
                        Foreground = AccentBrush(),
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    if (!string.IsNullOrEmpty(link.Url))
                    {
                        ToolTip.SetTip(linkBtn, link.Url);
                        if (link.Url.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase) || link.Url.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                            linkBtn.Click += (_, _) => WikiLinkClicked?.Invoke(this, link.Url);
                    }
                    inlines.Add(new InlineUIContainer(linkBtn) { BaselineAlignment = BaselineAlignment.Center });
                    break;
                }

                case LineBreakInline lb:
                    inlines.Add(new Run(lb.IsHard ? "\n" : " "));
                    break;

                case ContainerInline subContainer:
                    AppendInlineContainer(inlines, subContainer, fontSize, weight, style, strikethrough);
                    break;
            }
        }

        private void AppendLiteralWithObsidianTokens(
            InlineCollection inlines,
            string text,
            double fontSize,
            FontWeight weight,
            FontStyle style,
            bool strikethrough)
        {
            if (string.IsNullOrEmpty(text))
                return;

            int lastPos = 0;
            foreach (Match m in InlineTokenRegex.Matches(text))
            {
                if (m.Index > lastPos)
                    inlines.Add(CreateStyledRun(text[lastPos..m.Index], weight, style, strikethrough));

                if (m.Groups[2].Success)
                {
                    bool isEmbed = m.Value.StartsWith("![[", StringComparison.Ordinal);
                    string target = ExtractCleanEmbedTarget(m.Groups[2].Value.Trim(), out _, out _);
                    string display = m.Groups[3].Success ? m.Groups[3].Value.Trim() : target;

                    if (isEmbed && IsImageExtension(target))
                    {
                        string? resolvedImg = TryResolveImageFile(target);
                        if (!string.IsNullOrWhiteSpace(resolvedImg) && !resolvedImg.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var thumb = new Image
                                {
                                    Source = new Bitmap(resolvedImg),
                                    MaxHeight = 48,
                                    MaxWidth = 120,
                                    Stretch = Stretch.Uniform
                                };
                                var thumbWrap = new Border
                                {
                                    Background = Brushes.White,
                                    CornerRadius = new CornerRadius(4),
                                    Padding = new Thickness(3),
                                    Margin = new Thickness(2, 0),
                                    Child = thumb
                                };
                                ToolTip.SetTip(thumbWrap, $"Image: {target}");
                                inlines.Add(new InlineUIContainer(thumbWrap) { BaselineAlignment = BaselineAlignment.Center });
                                lastPos = m.Index + m.Length;
                                continue;
                            }
                            catch
                            {
                                // Fall through to image badge
                            }
                        }

                        var imgBadge = new Border
                        {
                            Padding = new Thickness(5, 1),
                            Margin = new Thickness(1, 0),
                            CornerRadius = new CornerRadius(5),
                            Background = AccentTintBrush(),
                            Child = new TextBlock
                            {
                                Text = $"🖼 {display}",
                                FontSize = Math.Max(12, fontSize - 1),
                                Foreground = AccentBrush()
                            }
                        };
                        inlines.Add(new InlineUIContainer(imgBadge) { BaselineAlignment = BaselineAlignment.Center });
                        lastPos = m.Index + m.Length;
                        continue;
                    }

                    var pill = new Button
                    {
                        Content = display,
                        FontSize = Math.Max(12, fontSize - 1),
                        MinHeight = 0,
                        Padding = new Thickness(5, 1),
                        Margin = new Thickness(1, 0),
                        CornerRadius = new CornerRadius(5),
                        Background = AccentTintBrush(),
                        Foreground = AccentBrush(),
                        BorderThickness = new Thickness(0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ToolTip.SetTip(pill, $"Open [[{target}]]");
                    pill.Click += (_, _) => WikiLinkClicked?.Invoke(this, target);
                    inlines.Add(new InlineUIContainer(pill) { BaselineAlignment = BaselineAlignment.Center });
                }
                else if (m.Groups[4].Success)
                {
                    string tag = "#" + m.Groups[4].Value;
                    var tagBtn = new Button
                    {
                        Content = tag,
                        FontSize = Math.Max(11.5, fontSize - 1.5),
                        MinHeight = 0,
                        Padding = new Thickness(5, 1),
                        Margin = new Thickness(1, 0),
                        CornerRadius = new CornerRadius(10),
                        Background = TagTintBrush(),
                        Foreground = TagBrush(),
                        BorderThickness = new Thickness(0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    tagBtn.Click += (_, _) => TagClicked?.Invoke(this, tag);
                    inlines.Add(new InlineUIContainer(tagBtn) { BaselineAlignment = BaselineAlignment.Center });
                }

                lastPos = m.Index + m.Length;
            }

            if (lastPos < text.Length)
                inlines.Add(CreateStyledRun(text[lastPos..], weight, style, strikethrough));
        }

        private static Run CreateStyledRun(string text, FontWeight weight, FontStyle style, bool strikethrough)
        {
            var run = new Run(text)
            {
                FontWeight = weight,
                FontStyle = style
            };
            if (strikethrough)
                run.TextDecorations = TextDecorations.Strikethrough;
            return run;
        }

        private static SelectableTextBlock CreateInlineTextBlock(double fontSize, FontWeight weight, IBrush foreground)
        {
            return new SelectableTextBlock
            {
                FontSize = fontSize,
                FontWeight = weight,
                Foreground = foreground,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(0, 1, 0, 2)
            };
        }

        private static string ExtractPlainText(ContainerInline container)
        {
            var parts = new List<string>();
            foreach (MdInline inline in container)
            {
                if (inline is LiteralInline lit)
                    parts.Add(lit.Content.ToString());
                else if (inline is CodeInline code)
                    parts.Add(code.Content);
                else if (inline is ContainerInline sub)
                    parts.Add(ExtractPlainText(sub));
            }
            return string.Concat(parts);
        }

        private IBrush TextBrush() => ThemeConfigService.ParseBrush(_palette.TextColor, _isDarkTheme ? "#E0E0E0" : "#1E1E1E");
        private IBrush MutedBrush() => ThemeConfigService.ParseBrush(_palette.MutedTextColor, _isDarkTheme ? "#9E9E9E" : "#616161");
        private IBrush CardBrush() => ThemeConfigService.ParseBrush(_palette.CardBackground, _isDarkTheme ? "#2A2D2E" : "#F8F8F8");
        private IBrush CodeCardBrush() => ThemeConfigService.ParseBrush(_palette.CodeBackground, _isDarkTheme ? "#181818" : "#F0F0F0");
        private IBrush BorderBrushColor() => ThemeConfigService.ParseBrush(_palette.BorderColor, _isDarkTheme ? "#3C3C3C" : "#D4D4D4");
        private IBrush AccentBrush() => ThemeConfigService.ParseBrush(_palette.AccentTextColor, _isDarkTheme ? "#4CC2FF" : "#005FB8");
        private IBrush AccentTintBrush() => ThemeConfigService.ParseBrush(_palette.AccentTintColor, _isDarkTheme ? "#263B4D" : "#D6EBFF");
        private IBrush TagBrush() => ThemeConfigService.ParseBrush(_palette.TagColor, _isDarkTheme ? "#6CCB5F" : "#107C10");
        private IBrush TagTintBrush() => ThemeConfigService.ParseBrush(_palette.TagTintColor, _isDarkTheme ? "#233B23" : "#DFF6DD");
        private IBrush WarningBrush() => ThemeConfigService.ParseBrush(_palette.WarningColor, _isDarkTheme ? "#FCE100" : "#9D5D00");
    }
}
