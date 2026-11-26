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
    /// Obsidian-style unified Markdown + LaTeX + Drawing live-preview text editor.
    /// Non-cursor lines render formatted Markdown/LaTeX; clicking once or navigating with arrow keys
    /// reveals raw Markdown syntax directly on the cursor line while clicking images opens the Image Editor.
    /// Maintainers: Add segment classification rules in <see cref="BuildEditorSegments"/> and block renderers in <see cref="RenderParsedBlock"/>.
    /// </summary>
    public sealed partial class MarkdownPreviewControl : Border
    {

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

        private List<EditorSegment> _lastSegments = new();
        private List<EditorSegment> _visibleEditableSegments = new();
        private readonly List<(Control Visual, int StartLine, int EndLine)> _renderedSegmentVisuals = new();
        private string _markdown = string.Empty;
        private bool _isDarkTheme = true;
        private ThemePalette _palette = ThemePalette.CreateClassicDark();
        private double _baseFontSize = 14.5;
        private int _activeSegmentStartLine = -1;
        private int _pendingCaretOffset = -1;
        private int _pendingActiveEditorCaret = -1;
        private int _pendingActiveCursorDisplayLine = -1;
        private TextBox? _activeSegmentTextBox;
        private bool _isPointerPressedInsideCanvas;
        private bool _activePictureCursorIsBefore;
        private Vector? _pendingScrollOffsetRestore;
        private bool _isApplyingScrollRestore;
        private int _scrollRestoreVersion;
        private bool _recoverEditorFocusOnPointerRelease;

        public StackPanel RootPanel => _rootStack;
        public DrawingCanvasControl? LastActiveInlineCanvas { get; private set; }
        public IReadOnlySet<int> CollapsedHeadingLines => _collapsedPreviewHeadingLines;
        public IReadOnlySet<int> OpenInlineImageEditors => _openInlineImageEditors;
        public int ActiveCursorLineZeroBased => _activeSegmentStartLine;
        public TextBox? ActiveEditorTextBox => _activeSegmentTextBox;
        public bool IsActiveCursorAdjacentToPicture => _activeSegmentStartLine >= 0 &&
            _lastSegments.Any(s => s.StartLine == _activeSegmentStartLine && IsPictureSegment(s.Kind));
        public bool IsActivePictureCursorBefore => IsActiveCursorAdjacentToPicture && _activePictureCursorIsBefore;
        public Vector ScrollOffset
        {
            get => _scrollViewer.Offset;
            set => _scrollViewer.Offset = value;
        }

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
        public event EventHandler<OpenCanvasInTabEventArgs>? OpenCanvasInSeparateTabRequested;

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
                Background = Brushes.Transparent,
                BringIntoViewOnFocusChange = false,
                Focusable = false
            };

            _rootStack.AddHandler(Control.RequestBringIntoViewEvent, (_, be) => be.Handled = true,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            _scrollViewer.AddHandler(Control.RequestBringIntoViewEvent, (_, be) => be.Handled = true,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            _scrollViewer.LayoutUpdated += (_, _) =>
            {
                if (_pendingScrollOffsetRestore is { } target)
                    RestoreScrollOffsetIfChanged(target);
            };
            _scrollViewer.ScrollChanged += (_, _) =>
            {
                if (_pendingScrollOffsetRestore is { } target)
                    RestoreScrollOffsetIfChanged(target);
            };

            _scrollViewer.AddHandler(InputElement.PointerPressedEvent, (_, pe) =>
            {
                _isPointerPressedInsideCanvas = IsSourceInsideDrawingCanvas(pe.Source as Visual);
                if (_isPointerPressedInsideCanvas)
                    pe.PreventGestureRecognition();
            }, RoutingStrategies.Tunnel);
            _scrollViewer.AddHandler(InputElement.PointerReleasedEvent, (_, pe) =>
            {
                _isPointerPressedInsideCanvas = false;
                if (_recoverEditorFocusOnPointerRelease &&
                    !IsSourceInsideDrawingCanvas(pe.Source as Visual) &&
                    !IsInteractiveChildControl(pe.Source as Visual, _scrollViewer))
                {
                    _recoverEditorFocusOnPointerRelease = false;
                    FocusActiveEditorTextBox();
                    Dispatcher.UIThread.Post(FocusActiveEditorTextBox, DispatcherPriority.Input);
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            _scrollViewer.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => _isPointerPressedInsideCanvas = false, RoutingStrategies.Tunnel);
            _scrollViewer.AddHandler(InputElement.ScrollGestureEvent, (_, se) =>
            {
                if (_isPointerPressedInsideCanvas || (LastActiveInlineCanvas?.IsPointerPressedOnCanvas ?? false))
                    se.Handled = true;
            }, RoutingStrategies.Tunnel);
            _scrollViewer.AddHandler(InputElement.ScrollGestureInertiaStartingEvent, (_, se) =>
            {
                if (_isPointerPressedInsideCanvas || (LastActiveInlineCanvas?.IsPointerPressedOnCanvas ?? false))
                    se.Handled = true;
            }, RoutingStrategies.Tunnel);

            _scrollViewer.PointerPressed += OnBackgroundPointerPressed;
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnPreviewDragOver, RoutingStrategies.Tunnel);
            AddHandler(DragDrop.DropEvent, OnPreviewDrop, RoutingStrategies.Tunnel);
            Child = _scrollViewer;
        }

        private void RestoreScrollOffsetIfChanged(Vector target)
        {
            if (_isApplyingScrollRestore)
                return;
            if (Math.Abs(_scrollViewer.Offset.Y - target.Y) <= 0.5 &&
                Math.Abs(_scrollViewer.Offset.X - target.X) <= 0.5)
                return;

            _isApplyingScrollRestore = true;
            try
            {
                _scrollViewer.Offset = target;
            }
            finally
            {
                _isApplyingScrollRestore = false;
            }
        }

        private void FocusActiveEditorTextBox()
        {
            var box = _activeSegmentTextBox;
            if (box is null)
                return;

            Vector preservedOffset = _pendingScrollOffsetRestore ?? _scrollViewer.Offset;
            if (TopLevel.GetTopLevel(this) is not null)
                box.Focus();

            if (_pendingActiveEditorCaret >= 0)
            {
                int caret = Math.Clamp(_pendingActiveEditorCaret, 0, (box.Text ?? string.Empty).Length);
                box.CaretIndex = caret;
                box.SelectionStart = caret;
                box.SelectionEnd = caret;
                _pendingActiveEditorCaret = -1;
            }

            if (_pendingActiveCursorDisplayLine > 0)
            {
                int line = _pendingActiveCursorDisplayLine;
                _pendingActiveCursorDisplayLine = -1;
                CursorPositionChanged?.Invoke(this, (line, 1));
            }

            RestoreScrollOffsetIfChanged(preservedOffset);
        }

        private void ApplyScheduledScrollAndFocus(int version)
        {
            if (_scrollRestoreVersion != version)
                return;

            if (_pendingScrollOffsetRestore is { } target)
                RestoreScrollOffsetIfChanged(target);

            FocusActiveEditorTextBox();
        }

        private static bool IsSourceInsideDrawingCanvas(Visual? source)
            => source is DrawingCanvasControl ||
               source?.FindAncestorOfType<DrawingCanvasControl>() is not null;

        public void UpdateCachedEmbeddedDrawing(string targetName, DrawingDocument doc)
        {
            if (string.IsNullOrWhiteSpace(targetName) || doc is null)
                return;
            _embeddedDrawingCache[targetName] = doc.Clone();
        }

        public void ClearInlineDrawingHistories()
        {
            _inlineDrawingHistories.Clear();
            _embeddedDrawingCache.Clear();
            _collapsedPreviewHeadingLines.Clear();
            _openInlineImageEditors.Clear();
            _openEmbeddedImageEditors.Clear();
            _activeSegmentStartLine = -1;
            _pendingCaretOffset = -1;
            _pendingActiveEditorCaret = -1;
            _pendingActiveCursorDisplayLine = -1;
            _activePictureCursorIsBefore = false;
            _pendingScrollOffsetRestore = null;
            _recoverEditorFocusOnPointerRelease = false;
            _scrollRestoreVersion++;
            _scrollViewer.Offset = default;
            _activeSegmentTextBox = null;
            LastActiveInlineCanvas = null;
        }

        /// <summary>
        /// Places a classic text-editor cursor immediately before or after a rendered picture segment.
        /// </summary>
        public void ActivatePictureAdjacentCursor(int pictureStartLineZeroBased, bool beforePicture = false)
        {
            var segments = _lastSegments.Count > 0
                ? _lastSegments
                : BuildEditorSegments(_markdown.Replace("\r\n", "\n").Split('\n'), ResolveEmbeddedDrawing);

            var target = segments.FirstOrDefault(s =>
                pictureStartLineZeroBased >= s.StartLine &&
                pictureStartLineZeroBased <= s.EndLine &&
                IsPictureSegment(s.Kind));

            if (target is null)
            {
                ActivateSegmentAtLine(pictureStartLineZeroBased);
                return;
            }

            _activeSegmentStartLine = target.StartLine;
            _activePictureCursorIsBefore = beforePicture;
            _pendingCaretOffset = 0;
            RenderMarkdown(_markdown);
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
            {
                _activeSegmentStartLine = -1;
                _pendingCaretOffset = -1;
                _activePictureCursorIsBefore = false;
                _activeSegmentTextBox = null;
                RenderMarkdown(_markdown);
            }
        }

        /// <summary>
        /// Marks an embedded `.ndraw` canvas editor as open and resets any stale active text segment
        /// so the upcoming markdown rebuild opens the canvas editor cleanly without double-rendering.
        /// </summary>
        public void PrepareEmbeddedCanvasForOpen(string targetName)
        {
            if (string.IsNullOrWhiteSpace(targetName)) return;
            _openEmbeddedImageEditors.Add(targetName);
            _activeSegmentStartLine = -1;
            _pendingCaretOffset = -1;
            _activePictureCursorIsBefore = false;
            _activeSegmentTextBox = null;
        }

        public void OpenEmbeddedImageEditor(string targetName)
        {
            if (string.IsNullOrWhiteSpace(targetName)) return;
            PrepareEmbeddedCanvasForOpen(targetName);
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
            if (IsPictureSegment(target.Kind))
            {
                _activePictureCursorIsBefore = caretAtAbsoluteStart && !caretAtAbsoluteEnd;
                _pendingCaretOffset = 0;
            }
            else
            {
                _activePictureCursorIsBefore = false;
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
            }

            RenderMarkdown(_markdown);
        }

        public void DeactivateActiveSegment()
        {
            if (_activeSegmentStartLine < 0)
                return;
            _activeSegmentStartLine = -1;
            _pendingCaretOffset = -1;
            _activePictureCursorIsBefore = false;
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
            Vector savedScrollOffset = _scrollViewer.Offset;
            bool preserveScroll = _activeSegmentStartLine >= 0 || savedScrollOffset.Y > 0 || savedScrollOffset.X > 0;
            int restoreVersion = ++_scrollRestoreVersion;
            if (preserveScroll)
                _pendingScrollOffsetRestore = savedScrollOffset;
            else
                _pendingScrollOffsetRestore = null;

            _markdown = markdown ?? string.Empty;
            _rootStack.Children.Clear();
            _renderedSegmentVisuals.Clear();
            _activeSegmentTextBox = null;
            _pendingActiveEditorCaret = -1;
            _pendingActiveCursorDisplayLine = -1;

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
                        if (!_openInlineImageEditors.Contains(segment.StartLine))
                            _visibleEditableSegments.Add(segment);
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
                        if (!_openEmbeddedImageEditors.Contains(cleanTarget))
                            _visibleEditableSegments.Add(segment);
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
                        _visibleEditableSegments.Add(segment);
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

            if (preserveScroll)
            {
                double measureWidth = _scrollViewer.Bounds.Width > 0
                    ? _scrollViewer.Bounds.Width
                    : (_scrollViewer.Viewport.Width > 0 ? _scrollViewer.Viewport.Width : Bounds.Width);
                if (measureWidth > 0)
                    _rootStack.Measure(new Size(measureWidth, double.PositiveInfinity));

                if (_scrollViewer.Bounds.Width > 0 && _scrollViewer.Bounds.Height > 0)
                {
                    _scrollViewer.Measure(_scrollViewer.Bounds.Size);
                    _scrollViewer.Arrange(new Rect(_scrollViewer.Bounds.Size));
                }

                RestoreScrollOffsetIfChanged(savedScrollOffset);
                FocusActiveEditorTextBox();

                Dispatcher.UIThread.Post(() => ApplyScheduledScrollAndFocus(restoreVersion), DispatcherPriority.Loaded);
                Dispatcher.UIThread.Post(() => ApplyScheduledScrollAndFocus(restoreVersion), DispatcherPriority.Default);
                Dispatcher.UIThread.Post(() => ApplyScheduledScrollAndFocus(restoreVersion), DispatcherPriority.Input);
                Dispatcher.UIThread.Post(() =>
                {
                    if (_scrollRestoreVersion != restoreVersion)
                        return;
                    if (_pendingScrollOffsetRestore is { } target)
                        RestoreScrollOffsetIfChanged(target);
                    _pendingScrollOffsetRestore = null;
                }, DispatcherPriority.ContextIdle);
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
            if (e.Source is Visual src &&
                (src is DrawingWorkspaceControl || src.FindAncestorOfType<DrawingWorkspaceControl>() is not null))
                return;

            var pointInfo = e.GetCurrentPoint(_scrollViewer);
            if (!pointInfo.Properties.IsLeftButtonPressed)
                return;

            // Hit-test the clicked vertical row first so clicking next to a picture or short line activates that row.
            Point clickInRoot = _scrollViewer.TranslatePoint(e.GetPosition(_scrollViewer), _rootStack)
                                ?? new Point(
                                    e.GetPosition(_scrollViewer).X - _rootStack.Margin.Left,
                                    e.GetPosition(_scrollViewer).Y - _rootStack.Margin.Top + _scrollViewer.Offset.Y);

            foreach (var (visual, startLine, _) in _renderedSegmentVisuals)
            {
                Rect b = visual.Bounds;
                if (b.Height > 0 && clickInRoot.Y >= b.Top - 2 && clickInRoot.Y <= b.Bottom + 2)
                {
                    var hitSeg = _lastSegments.FirstOrDefault(s => s.StartLine == startLine);
                    if (hitSeg is not null && IsPictureSegment(hitSeg.Kind))
                        ActivatePictureAdjacentCursor(startLine, beforePicture: clickInRoot.X < 16);
                    else
                        ActivateSegmentAtLine(startLine, caretAtAbsoluteEnd: true);
                    _recoverEditorFocusOnPointerRelease = true;
                    e.Handled = true;
                    return;
                }
            }

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
                _recoverEditorFocusOnPointerRelease = true;
                e.Handled = true;
                return;
            }

            var lastTextSeg = _visibleEditableSegments[^1];
            ActivateSegmentAtLine(lastTextSeg.StartLine, caretAtAbsoluteEnd: true);
            _recoverEditorFocusOnPointerRelease = true;
            e.Handled = true;
        }


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
