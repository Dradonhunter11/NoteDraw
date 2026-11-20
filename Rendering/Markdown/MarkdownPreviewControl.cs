using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
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
    /// Renders Markdown documents using Markdig into native Avalonia controls,
    /// supporting Obsidian-style fenced ```latex / ```math blocks, $$...$$ display math,
    /// inline $...$ LaTeX, interactive inline ```drawing canvases, ![[*.ndraw]] embeds,
    /// clickable [[WikiLinks]], #tags, interactive checklists, and pipe tables.
    /// Maintainers: Add handlers for new Markdig block or inline types in <see cref="RenderBlock"/> / <see cref="AppendInline"/>.
    /// </summary>
    public sealed class MarkdownPreviewControl : Border
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        private static readonly Regex InlineTokenRegex = new(
            @"(!?\[\[([^\]\|]+?)(?:\|([^\]]+?))?\]\]|(?<![A-Za-z0-9_/&#])#([A-Za-z][A-Za-z0-9_\-/]*))",
            RegexOptions.Compiled);

        private readonly StackPanel _rootStack;
        private readonly ScrollViewer _scrollViewer;
        private readonly Dictionary<int, (List<DrawingDocument> Undo, List<DrawingDocument> Redo)> _inlineDrawingHistories = new();
        private readonly HashSet<int> _collapsedPreviewHeadingLines = new();
        private string _markdown = string.Empty;
        private bool _isDarkTheme = true;
        private ThemePalette _palette = ThemePalette.CreateClassicDark();
        private double _baseFontSize = 14.5;

        public StackPanel RootPanel => _rootStack;
        public DrawingCanvasControl? LastActiveInlineCanvas { get; private set; }
        public IReadOnlySet<int> CollapsedHeadingLines => _collapsedPreviewHeadingLines;

        public Func<string, DrawingDocument?>? ResolveEmbeddedDrawing { get; set; }

        public event EventHandler<int>? LineFocusRequested;
        public event EventHandler<string>? WikiLinkClicked;
        public event EventHandler<string>? TagClicked;
        public event EventHandler<InlineDrawingUpdateEventArgs>? InlineDrawingUpdated;
        public event EventHandler<ExternalDrawingUpdateEventArgs>? ExternalDrawingUpdated;
        public event EventHandler<TaskToggleEventArgs>? TaskItemToggled;
        public event EventHandler<DrawingCanvasControl>? InlineCanvasActivated;
        public event EventHandler<string>? DocumentEditedFromPreview;

        public void ClearInlineDrawingHistories()
        {
            _inlineDrawingHistories.Clear();
            _collapsedPreviewHeadingLines.Clear();
            LastActiveInlineCanvas = null;
        }

        public void TogglePreviewHeadingFold(int headingStartLineZeroBased)
        {
            if (!_collapsedPreviewHeadingLines.Remove(headingStartLineZeroBased))
                _collapsedPreviewHeadingLines.Add(headingStartLineZeroBased);
            RenderMarkdown(_markdown);
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

        public MarkdownPreviewControl()
        {
            _rootStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 10,
                Margin = new Thickness(18, 14, 18, 28)
            };

            _scrollViewer = new ScrollViewer
            {
                Content = _rootStack
            };

            Child = _scrollViewer;
        }

        public void RenderMarkdown(string? markdown)
        {
            _markdown = markdown ?? string.Empty;
            _rootStack.Children.Clear();

            if (string.IsNullOrWhiteSpace(_markdown))
            {
                _rootStack.Children.Add(new TextBlock
                {
                    Text = "Nothing to preview yet. Use the quick composer below or the editor to write Markdown, ```latex, or ```drawing blocks!",
                    Foreground = MutedBrush(),
                    FontStyle = FontStyle.Italic,
                    FontSize = 14
                });
                _rootStack.Children.Add(CreateQuickAppendCard());
                return;
            }

            string normalized = NormalizeMathBlocksWithMap(_markdown, out int[] lineMap);
            string[] origLines = _markdown.Replace("\r\n", "\n").Split('\n');
            MarkdownDocument doc = Markdig.Markdown.Parse(normalized, Pipeline);
            var blocks = doc.Where(b => b is not LinkReferenceDefinitionGroup).ToList();

            int? collapsedHeadingLevel = null;

            for (int i = 0; i < blocks.Count; i++)
            {
                Block block = blocks[i];
                int origStart = MapOrigLine(block.Line, lineMap, origLines.Length);
                int nextOrigStart = i + 1 < blocks.Count
                    ? MapOrigLine(blocks[i + 1].Line, lineMap, origLines.Length)
                    : origLines.Length;

                int origEnd = ComputeBlockEndLine(block, origLines, origStart, nextOrigStart);

                if (block is HeadingBlock heading)
                {
                    if (collapsedHeadingLevel.HasValue && heading.Level <= collapsedHeadingLevel.Value)
                        collapsedHeadingLevel = null;

                    if (collapsedHeadingLevel.HasValue)
                        continue;

                    bool isCollapsed = heading.Level <= 5 && _collapsedPreviewHeadingLines.Contains(origStart);
                    Control? headingVisual = RenderHeading(heading, origStart, isCollapsed);
                    if (headingVisual is not null)
                    {
                        _rootStack.Children.Add(WrapEditableBlock(headingVisual, origStart, origEnd, origLines));
                    }

                    if (isCollapsed)
                        collapsedHeadingLevel = heading.Level;
                    continue;
                }

                if (collapsedHeadingLevel.HasValue)
                    continue;

                Control? visual = RenderBlock(block, origStart, origEnd, origLines);
                if (visual is not null)
                    _rootStack.Children.Add(visual);
            }

            _rootStack.Children.Add(CreateQuickAppendCard());
        }

        /// <summary>
        /// Normalizes Obsidian-style $$...$$ and \[...\] display math blocks (for example when math
        /// starts on the same line as $$) so Markdig parses every display block reliably as MathBlock.
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

        private static int MapOrigLine(int normalizedLine, int[] lineMap, int origLineCount)
        {
            if (lineMap.Length == 0 || origLineCount == 0)
                return 0;
            int clamped = Math.Clamp(normalizedLine, 0, lineMap.Length - 1);
            return Math.Clamp(lineMap[clamped], 0, origLineCount - 1);
        }

        private static int ComputeBlockEndLine(Block block, string[] origLines, int startLine, int nextBlockStart)
        {
            if (origLines.Length == 0)
                return 0;

            if (block is HeadingBlock)
                return Math.Clamp(startLine, 0, origLines.Length - 1);

            if (block is FencedCodeBlock)
            {
                string startTrim = origLines[Math.Clamp(startLine, 0, origLines.Length - 1)].Trim();
                if (block is MathBlock)
                {
                    if ((startTrim.StartsWith("$$", StringComparison.Ordinal) && startTrim.IndexOf("$$", 2, StringComparison.Ordinal) >= 0) ||
                        (startTrim.StartsWith(@"\[", StringComparison.Ordinal) && startTrim.EndsWith(@"\]", StringComparison.Ordinal) && startTrim.Length >= 4))
                    {
                        return startLine;
                    }

                    for (int i = startLine + 1; i < origLines.Length; i++)
                    {
                        string t = origLines[i].Trim();
                        if (t.EndsWith("$$", StringComparison.Ordinal) || t == @"\]")
                            return i;
                    }
                    return origLines.Length - 1;
                }

                for (int i = startLine + 1; i < origLines.Length; i++)
                {
                    if (origLines[i].Trim().StartsWith("```", StringComparison.Ordinal))
                        return i;
                }
                return origLines.Length - 1;
            }

            int end = Math.Clamp(nextBlockStart - 1, startLine, origLines.Length - 1);
            while (end > startLine && string.IsNullOrWhiteSpace(origLines[end]))
                end--;
            return end;
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

            if (!string.IsNullOrEmpty(replacementSlice))
            {
                var inserted = replacementSlice.Replace("\r\n", "\n").Split('\n');
                lines.InsertRange(start, inserted);
            }

            return string.Join(lineEnding, lines);
        }

        private Control? RenderBlock(Block block, int origStart = 0, int origEnd = 0, string[]? origLines = null)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    return RenderHeading(heading, origStart, isCollapsed: false);

                case MathBlock mathBlock:
                    return RenderLatexCard(mathBlock.Lines.ToString().Trim(), "$$ Display Math", origStart, origEnd, "dollar");

                case FencedCodeBlock fenced:
                    return RenderFencedBlock(fenced, origStart, origEnd);

                case CodeBlock codeBlock:
                {
                    var card = RenderCodeCard(codeBlock.Lines.ToString().TrimEnd(), "code");
                    return origLines is not null ? WrapEditableBlock(card, origStart, origEnd, origLines) : card;
                }

                case ParagraphBlock paragraph:
                {
                    var pCtrl = RenderParagraph(paragraph, origStart, origEnd, out bool alreadyWrapped);
                    return (!alreadyWrapped && origLines is not null) ? WrapEditableBlock(pCtrl, origStart, origEnd, origLines) : pCtrl;
                }

                case QuoteBlock quote:
                {
                    var qCtrl = RenderQuote(quote);
                    return origLines is not null ? WrapEditableBlock(qCtrl, origStart, origEnd, origLines) : qCtrl;
                }

                case ListBlock list:
                {
                    var lCtrl = RenderList(list);
                    return origLines is not null ? WrapEditableBlock(lCtrl, origStart, origEnd, origLines) : lCtrl;
                }

                case Table table:
                {
                    var tCtrl = RenderTable(table);
                    return origLines is not null ? WrapEditableBlock(tCtrl, origStart, origEnd, origLines) : tCtrl;
                }

                case ThematicBreakBlock:
                    return new Border
                    {
                        Height = 1,
                        Margin = new Thickness(0, 8),
                        Background = BorderBrushColor()
                    };

                default:
                    return null;
            }
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
                ToolTip.SetTip(foldBtn, isCollapsed ? $"Expand H{heading.Level} section" : $"Collapse H{heading.Level} section");
                foldBtn.Click += (_, _) => TogglePreviewHeadingFold(origStart);

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
                    Margin = new Thickness(0, heading.Level == 1 ? 4 : 10, 0, 2),
                    Children =
                    {
                        headingContent,
                        new Border { Height = 1, Background = BorderBrushColor() }
                    }
                };
            }

            if (headingContent is Control ctrl)
                ctrl.Margin = new Thickness(0, 8, 0, 0);
            return headingContent;
        }

        private Control RenderFencedBlock(FencedCodeBlock fenced, int origStart, int origEnd)
        {
            string info = (fenced.Info ?? string.Empty).Trim().ToLowerInvariant();
            string body = fenced.Lines.ToString();

            if (info is "latex" or "math" or "tex")
                return RenderLatexCard(body.Trim(), $"```{info} block", origStart, origEnd, info);

            if (info is "drawing" or "ndraw")
                return RenderInlineDrawingCard(body, origStart);

            return RenderCodeCard(body.TrimEnd(), string.IsNullOrEmpty(info) ? "text" : info);
        }

        private Control RenderLatexCard(string latex, string badgeText, int startLine, int endLine, string fenceKind)
        {
            var mathCtrl = new LatexMathControl
            {
                LatexSource = latex,
                FontSize = 20,
                IsDisplayMode = true,
                Foreground = TextBrush(),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4, 8)
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

            var header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                ColumnSpacing = 6
            };

            var badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2),
                Background = AccentTintBrush(),
                Child = new TextBlock
                {
                    Text = $"{badgeText} (L{startLine + 1})",
                    FontSize = 11,
                    Foreground = AccentBrush(),
                    FontWeight = FontWeight.SemiBold
                }
            };
            Grid.SetColumn(badge, 0);
            header.Children.Add(badge);

            Grid.SetColumn(warn, 1);
            header.Children.Add(warn);

            var inlineEditBox = new TextBox
            {
                Text = latex,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace"),
                FontSize = 12.5,
                MinHeight = 56
            };

            var inlineEditPanel = new StackPanel
            {
                IsVisible = false,
                Spacing = 6,
                Margin = new Thickness(0, 6, 0, 0)
            };

            var editHereBtn = new Button
            {
                Content = "Edit Inline",
                FontSize = 11,
                Padding = new Thickness(7, 2),
                CornerRadius = new CornerRadius(4)
            };

            var sourceJumpBtn = new Button
            {
                Content = "Source",
                FontSize = 11,
                Padding = new Thickness(7, 2),
                CornerRadius = new CornerRadius(4)
            };
            sourceJumpBtn.Click += (_, _) => LineFocusRequested?.Invoke(this, startLine + 1);

            Grid.SetColumn(editHereBtn, 2);
            Grid.SetColumn(sourceJumpBtn, 3);
            header.Children.Add(editHereBtn);
            header.Children.Add(sourceJumpBtn);

            void CommitInlineLatex()
            {
                string edited = (inlineEditBox.Text ?? string.Empty).Trim();
                string wrapped = fenceKind == "dollar"
                    ? $"$$\n{edited}\n$$"
                    : $"```{fenceKind}\n{edited}\n```";
                string updatedMd = ReplaceLineRange(_markdown, startLine, endLine, wrapped);
                inlineEditPanel.IsVisible = false;
                RenderMarkdown(updatedMd);
                DocumentEditedFromPreview?.Invoke(this, updatedMd);
            }

            void ToggleInlineEditor()
            {
                inlineEditPanel.IsVisible = !inlineEditPanel.IsVisible;
                if (inlineEditPanel.IsVisible)
                {
                    inlineEditBox.Text = mathCtrl.LatexSource;
                    inlineEditBox.Focus();
                    inlineEditBox.CaretIndex = inlineEditBox.Text?.Length ?? 0;
                }
            }

            inlineEditBox.TextChanged += (_, _) =>
            {
                mathCtrl.LatexSource = inlineEditBox.Text ?? string.Empty;
                warn.Text = mathCtrl.DiagnosticsText;
                warn.IsVisible = mathCtrl.HasWarnings;
            };
            inlineEditBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    CommitInlineLatex();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    mathCtrl.LatexSource = latex;
                    warn.Text = mathCtrl.DiagnosticsText;
                    warn.IsVisible = mathCtrl.HasWarnings;
                    inlineEditPanel.IsVisible = false;
                    e.Handled = true;
                }
            };

            var applyBtn = new Button
            {
                Content = "Apply (Ctrl+Enter)",
                FontSize = 11,
                Padding = new Thickness(8, 3),
                CornerRadius = new CornerRadius(4),
                Background = ThemeConfigService.ParseBrush(_palette.AccentColor, "#0078D4"),
                Foreground = Brushes.White
            };
            applyBtn.Click += (_, _) => CommitInlineLatex();

            var cancelBtn = new Button
            {
                Content = "Cancel",
                FontSize = 11,
                Padding = new Thickness(8, 3),
                CornerRadius = new CornerRadius(4)
            };
            cancelBtn.Click += (_, _) =>
            {
                mathCtrl.LatexSource = latex;
                inlineEditPanel.IsVisible = false;
            };

            inlineEditPanel.Children.Add(inlineEditBox);
            inlineEditPanel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 6,
                Children = { cancelBtn, applyBtn }
            });

            editHereBtn.Click += (_, _) => ToggleInlineEditor();
            mathCtrl.DoubleTapped += (_, e) =>
            {
                ToggleInlineEditor();
                e.Handled = true;
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
                    Children = { header, mathCtrl, inlineEditPanel }
                }
            };
        }

        private Control RenderInlineDrawingCard(string json, int openFenceLine)
        {
            var doc = DrawingDocument.FromJson(json);
            var workspace = new DrawingWorkspaceControl(doc, compactInlineMode: true, isDarkTheme: _isDarkTheme);
            workspace.ApplyThemePalette(_palette, _isDarkTheme);
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
                _inlineDrawingHistories[openFenceLine] = workspace.Canvas.GetHistoryState();
                Activate();
                string updatedJson = workspace.Canvas.Document.ToJson();
                InlineDrawingUpdated?.Invoke(this, new InlineDrawingUpdateEventArgs(openFenceLine, updatedJson));
            };

            var headerRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(0, 0, 0, 4)
            };
            headerRow.Children.Add(new TextBlock
            {
                Text = $"Inline Hand Drawing (Line {openFenceLine + 1})",
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = MutedBrush(),
                VerticalAlignment = VerticalAlignment.Center
            });

            var jumpBtn = new Button
            {
                Content = "View Source Block",
                FontSize = 11,
                Padding = new Thickness(7, 2),
                CornerRadius = new CornerRadius(4)
            };
            jumpBtn.Click += (_, _) => LineFocusRequested?.Invoke(this, openFenceLine + 1);
            Grid.SetColumn(jumpBtn, 1);
            headerRow.Children.Add(jumpBtn);

            return new StackPanel
            {
                Spacing = 4,
                Children = { headerRow, workspace }
            };
        }

        private Control RenderEmbeddedNdrawCard(string targetName)
        {
            var doc = ResolveEmbeddedDrawing?.Invoke(targetName) ?? new DrawingDocument();
            var workspace = new DrawingWorkspaceControl(doc, compactInlineMode: true, isDarkTheme: _isDarkTheme);
            workspace.ApplyThemePalette(_palette, _isDarkTheme);
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
                ExternalDrawingUpdated?.Invoke(this, new ExternalDrawingUpdateEventArgs(targetName, workspace.Canvas.Document.ToJson()));
            };

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Embedded Drawing: {targetName}",
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = AccentBrush(),
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };

            return new StackPanel
            {
                Spacing = 4,
                Children = { header, workspace }
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

        private Control RenderParagraph(ParagraphBlock paragraph, int origStart, int origEnd, out bool alreadyWrapped)
        {
            alreadyWrapped = false;
            if (paragraph.Inline is not null)
            {
                var meaningful = paragraph.Inline
                    .Where(i => i is not LiteralInline lit || !string.IsNullOrWhiteSpace(lit.Content.ToString()))
                    .ToList();

                if (meaningful.Count == 1 && meaningful[0] is MathInline singleDisplayMath && singleDisplayMath.DelimiterCount == 2)
                {
                    alreadyWrapped = true;
                    return RenderLatexCard(singleDisplayMath.Content.ToString().Trim(), "$$ Display Math", origStart, origEnd, "dollar");
                }

                string raw = ExtractPlainText(paragraph.Inline).Trim();
                if (raw.StartsWith("![[", StringComparison.Ordinal) && raw.EndsWith("]]", StringComparison.Ordinal))
                {
                    string target = raw[3..^2].Trim();
                    if (target.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase) || ResolveEmbeddedDrawing?.Invoke(target) is not null)
                    {
                        alreadyWrapped = true;
                        return RenderEmbeddedNdrawCard(target);
                    }
                }
            }

            var tb = CreateInlineTextBlock(_baseFontSize, FontWeight.Normal, TextBrush());
            if (paragraph.Inline is not null)
                AppendInlineContainer(tb.Inlines!, paragraph.Inline, _baseFontSize, FontWeight.Normal, FontStyle.Normal, strikethrough: false);
            return tb;
        }

        private Control WrapEditableBlock(Control renderedControl, int startLine, int endLine, string[] origLines)
        {
            int s = Math.Clamp(startLine, 0, Math.Max(0, origLines.Length - 1));
            int e = Math.Clamp(endLine, s, Math.Max(0, origLines.Length - 1));
            string slice = origLines.Length > 0
                ? string.Join("\n", origLines[s..(e + 1)])
                : string.Empty;

            var host = new Border();
            var editBtn = new Button
            {
                Content = "✎",
                FontSize = 11,
                MinHeight = 0,
                Padding = new Thickness(5, 2),
                Margin = new Thickness(4, 0, 0, 0),
                CornerRadius = new CornerRadius(4),
                Opacity = 0.55,
                Background = Brushes.Transparent,
                Foreground = MutedBrush(),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Top
            };
            ToolTip.SetTip(editBtn, $"Edit block L{s + 1}–L{e + 1} in place (or double-click)");

            var displayRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(renderedControl, 0);
            Grid.SetColumn(editBtn, 1);
            displayRow.Children.Add(renderedControl);
            displayRow.Children.Add(editBtn);

            host.Child = displayRow;

            void EnterEditMode()
            {
                var editBox = new TextBox
                {
                    Text = slice,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace"),
                    FontSize = 13
                };

                var liveInlineMath = new LatexMathControl
                {
                    FontSize = 16,
                    IsDisplayMode = false,
                    Foreground = TextBrush(),
                    IsVisible = false
                };

                void UpdateLiveMathHint()
                {
                    var ctx = LatexLiveDetector.DetectAtCursor(editBox.Text ?? string.Empty, editBox.CaretIndex);
                    if (ctx.IsFound)
                    {
                        liveInlineMath.LatexSource = ctx.LatexCode;
                        liveInlineMath.IsDisplayMode = ctx.IsDisplayMode;
                        liveInlineMath.IsVisible = true;
                    }
                    else
                    {
                        liveInlineMath.IsVisible = false;
                    }
                }

                editBox.TextChanged += (_, _) => UpdateLiveMathHint();
                editBox.PropertyChanged += (_, args) =>
                {
                    if (args.Property == TextBox.CaretIndexProperty)
                        UpdateLiveMathHint();
                };

                void CommitBlockEdit()
                {
                    string updatedMd = ReplaceLineRange(_markdown, s, e, editBox.Text ?? string.Empty);
                    RenderMarkdown(updatedMd);
                    DocumentEditedFromPreview?.Invoke(this, updatedMd);
                }

                editBox.KeyDown += (_, ke) =>
                {
                    if (ke.Key == Key.Enter && ke.KeyModifiers.HasFlag(KeyModifiers.Control))
                    {
                        CommitBlockEdit();
                        ke.Handled = true;
                    }
                    else if (ke.Key == Key.Escape)
                    {
                        host.Child = displayRow;
                        ke.Handled = true;
                    }
                };

                var applyBtn = new Button
                {
                    Content = "Apply (Ctrl+Enter)",
                    FontSize = 11,
                    Padding = new Thickness(8, 3),
                    CornerRadius = new CornerRadius(4),
                    Background = ThemeConfigService.ParseBrush(_palette.AccentColor, "#0078D4"),
                    Foreground = Brushes.White
                };
                applyBtn.Click += (_, _) => CommitBlockEdit();

                var cancelBtn = new Button
                {
                    Content = "Cancel (Esc)",
                    FontSize = 11,
                    Padding = new Thickness(8, 3),
                    CornerRadius = new CornerRadius(4)
                };
                cancelBtn.Click += (_, _) => host.Child = displayRow;

                host.Child = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    BorderBrush = ThemeConfigService.ParseBrush(_palette.AccentColor, "#0078D4"),
                    Background = CardBrush(),
                    Padding = new Thickness(10, 8),
                    Child = new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"Editing Lines {s + 1}–{e + 1}",
                                FontSize = 11,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = AccentBrush()
                            },
                            editBox,
                            liveInlineMath,
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                HorizontalAlignment = HorizontalAlignment.Right,
                                Spacing = 6,
                                Children = { cancelBtn, applyBtn }
                            }
                        }
                    }
                };

                editBox.Focus();
                editBox.CaretIndex = editBox.Text?.Length ?? 0;
                UpdateLiveMathHint();
            }

            editBtn.Click += (_, _) => EnterEditMode();
            renderedControl.DoubleTapped += (_, de) =>
            {
                EnterEditMode();
                de.Handled = true;
            };

            return host;
        }

        private Control CreateQuickAppendCard()
        {
            var appendBox = new TextBox
            {
                PlaceholderText = "+ Add Markdown paragraph, heading, $LaTeX$, or ```latex block directly from preview (Ctrl+Enter)...",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace"),
                FontSize = 12.5
            };

            var addBtn = new Button
            {
                Content = "Add Block",
                FontSize = 11.5,
                Padding = new Thickness(9, 4),
                CornerRadius = new CornerRadius(4),
                Background = ThemeConfigService.ParseBrush(_palette.AccentColor, "#0078D4"),
                Foreground = Brushes.White
            };

            var addDrawBtn = new Button
            {
                Content = "+ Drawing Canvas",
                FontSize = 11.5,
                Padding = new Thickness(9, 4),
                CornerRadius = new CornerRadius(4)
            };

            void CommitAppend(string content)
            {
                if (string.IsNullOrWhiteSpace(content)) return;
                string lineEnding = _markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                string sep = string.IsNullOrWhiteSpace(_markdown) ? string.Empty : $"{lineEnding}{lineEnding}";
                string updated = $"{_markdown.TrimEnd()}{sep}{content}";
                appendBox.Text = string.Empty;
                RenderMarkdown(updated);
                DocumentEditedFromPreview?.Invoke(this, updated);
            }

            addBtn.Click += (_, _) => CommitAppend(appendBox.Text ?? string.Empty);
            addDrawBtn.Click += (_, _) =>
            {
                var doc = new DrawingDocument { Background = CanvasBackgroundKind.Grid };
                CommitAppend($"```drawing\n{doc.ToJson()}\n```");
            };
            appendBox.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter && ke.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    CommitAppend(appendBox.Text ?? string.Empty);
                    ke.Handled = true;
                }
            };

            return new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = BorderBrushColor(),
                Background = CardBrush(),
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(10, 8),
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        appendBox,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 6,
                            Children = { addDrawBtn, addBtn }
                        }
                    }
                }
            };
        }

        private Control RenderQuote(QuoteBlock quote)
        {
            var inner = new StackPanel { Spacing = 6 };
            foreach (Block child in quote)
            {
                var visual = RenderBlock(child);
                if (visual is not null)
                    inner.Children.Add(visual);
            }

            return new Border
            {
                BorderThickness = new Thickness(4, 0, 0, 0),
                BorderBrush = AccentBrush(),
                Background = CardBrush(),
                CornerRadius = new CornerRadius(0, 6, 6, 0),
                Padding = new Thickness(12, 8),
                Child = inner
            };
        }

        private Control RenderList(ListBlock list)
        {
            var stack = new StackPanel
            {
                Spacing = 5,
                Margin = new Thickness(8, 2, 0, 2)
            };

            int idx = 1;
            foreach (Block block in list)
            {
                if (block is not ListItemBlock item) continue;

                bool isTask = false;
                bool isChecked = false;
                var contentStack = new StackPanel { Spacing = 4 };

                foreach (Block child in item)
                {
                    if (child is ParagraphBlock p && p.Inline?.FirstChild is TaskList task)
                    {
                        isTask = true;
                        isChecked = task.Checked;
                        var tb = CreateInlineTextBlock(14.5, FontWeight.Normal, isChecked ? MutedBrush() : TextBrush());
                        AppendInlineContainer(tb.Inlines!, p.Inline, 14.5, FontWeight.Normal, FontStyle.Normal, strikethrough: isChecked, skipTaskPrefix: true);
                        contentStack.Children.Add(tb);
                    }
                    else
                    {
                        var visual = RenderBlock(child);
                        if (visual is not null)
                            contentStack.Children.Add(visual);
                    }
                }

                Control bulletControl;
                if (isTask)
                {
                    int lineZeroBased = item.Line;
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
                        FontSize = 14.5,
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
                        label = link.Url ?? "link";

                    var linkBtn = new Button
                    {
                        Content = label,
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
                    // [[WikiLink]] or [[WikiLink|Alias]]
                    string target = m.Groups[2].Value.Trim();
                    string display = m.Groups[3].Success ? m.Groups[3].Value.Trim() : target;
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
                    // #tag pill
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
                Padding = new Thickness(0, 2, 0, 4)
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
