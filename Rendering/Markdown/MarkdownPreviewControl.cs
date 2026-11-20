using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
    public sealed class MarkdownPreviewControl : UserControl
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        private static readonly Regex InlineTokenRegex = new(
            @"(!?\[\[([^\]\|]+?)(?:\|([^\]]+?))?\]\]|(?<![A-Za-z0-9_/&#])#([A-Za-z][A-Za-z0-9_\-/]*))",
            RegexOptions.Compiled);

        private readonly StackPanel _rootStack;
        private readonly ScrollViewer _scrollViewer;
        private string _markdown = string.Empty;
        private bool _isDarkTheme = true;

        public Func<string, DrawingDocument?>? ResolveEmbeddedDrawing { get; set; }

        public event EventHandler<int>? LineFocusRequested;
        public event EventHandler<string>? WikiLinkClicked;
        public event EventHandler<string>? TagClicked;
        public event EventHandler<InlineDrawingUpdateEventArgs>? InlineDrawingUpdated;
        public event EventHandler<ExternalDrawingUpdateEventArgs>? ExternalDrawingUpdated;
        public event EventHandler<TaskToggleEventArgs>? TaskItemToggled;

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (_isDarkTheme == value) return;
                _isDarkTheme = value;
                RenderMarkdown(_markdown);
            }
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

            Content = _scrollViewer;
        }

        public void RenderMarkdown(string? markdown)
        {
            _markdown = markdown ?? string.Empty;
            _rootStack.Children.Clear();

            if (string.IsNullOrWhiteSpace(_markdown))
            {
                _rootStack.Children.Add(new TextBlock
                {
                    Text = "Nothing to preview yet. Start writing Markdown, ```latex blocks, or ```drawing blocks!",
                    Foreground = MutedBrush(),
                    FontStyle = FontStyle.Italic,
                    FontSize = 14
                });
                return;
            }

            MarkdownDocument doc = Markdig.Markdown.Parse(_markdown, Pipeline);
            foreach (Block block in doc)
            {
                Control? visual = RenderBlock(block);
                if (visual is not null)
                    _rootStack.Children.Add(visual);
            }
        }

        private Control? RenderBlock(Block block)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    return RenderHeading(heading);

                case MathBlock mathBlock:
                    return RenderLatexCard(mathBlock.Lines.ToString().Trim(), "$$ Display Math", mathBlock.Line);

                case FencedCodeBlock fenced:
                    return RenderFencedBlock(fenced);

                case CodeBlock codeBlock:
                    return RenderCodeCard(codeBlock.Lines.ToString().TrimEnd(), "code");

                case ParagraphBlock paragraph:
                    return RenderParagraph(paragraph);

                case QuoteBlock quote:
                    return RenderQuote(quote);

                case ListBlock list:
                    return RenderList(list);

                case Table table:
                    return RenderTable(table);

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

        private Control RenderHeading(HeadingBlock heading)
        {
            double size = heading.Level switch
            {
                1 => 26,
                2 => 21,
                3 => 18,
                4 => 16,
                5 => 14.5,
                _ => 13.5
            };

            var tb = CreateInlineTextBlock(size, FontWeight.Bold, TextBrush());
            if (heading.Inline is not null)
                AppendInlineContainer(tb.Inlines!, heading.Inline, size, FontWeight.Bold, FontStyle.Normal, strikethrough: false);

            if (heading.Level <= 2)
            {
                return new StackPanel
                {
                    Spacing = 4,
                    Margin = new Thickness(0, heading.Level == 1 ? 4 : 10, 0, 2),
                    Children =
                    {
                        tb,
                        new Border { Height = 1, Background = BorderBrushColor() }
                    }
                };
            }

            tb.Margin = new Thickness(0, 8, 0, 0);
            return tb;
        }

        private Control RenderFencedBlock(FencedCodeBlock fenced)
        {
            string info = (fenced.Info ?? string.Empty).Trim().ToLowerInvariant();
            string body = fenced.Lines.ToString();

            if (info is "latex" or "math" or "tex")
                return RenderLatexCard(body.Trim(), $"```{info} block", fenced.Line);

            if (info is "drawing" or "ndraw")
                return RenderInlineDrawingCard(body, fenced.Line);

            return RenderCodeCard(body.TrimEnd(), string.IsNullOrEmpty(info) ? "text" : info);
        }

        private Control RenderLatexCard(string latex, string badgeText, int zeroBasedLine)
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

            var header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
            };

            var badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2),
                Background = AccentTintBrush(),
                Child = new TextBlock
                {
                    Text = $"{badgeText} (L{zeroBasedLine + 1})",
                    FontSize = 11,
                    Foreground = AccentBrush(),
                    FontWeight = FontWeight.SemiBold
                }
            };
            Grid.SetColumn(badge, 0);
            header.Children.Add(badge);

            if (mathCtrl.HasWarnings)
            {
                var warn = new TextBlock
                {
                    Text = mathCtrl.DiagnosticsText,
                    FontSize = 11,
                    Foreground = WarningBrush(),
                    Margin = new Thickness(8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(warn, 1);
                header.Children.Add(warn);
            }

            var editBtn = new Button
            {
                Content = "Edit in Source",
                FontSize = 11,
                Padding = new Thickness(7, 2),
                CornerRadius = new CornerRadius(4)
            };
            editBtn.Click += (_, _) => LineFocusRequested?.Invoke(this, zeroBasedLine + 1);
            Grid.SetColumn(editBtn, 2);
            header.Children.Add(editBtn);

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
                    Children = { header, mathCtrl }
                }
            };
        }

        private Control RenderInlineDrawingCard(string json, int openFenceLine)
        {
            var doc = DrawingDocument.FromJson(json);
            var workspace = new DrawingWorkspaceControl(doc, compactInlineMode: true, isDarkTheme: _isDarkTheme);
            workspace.DocumentChanged += (_, _) =>
            {
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
            workspace.DocumentChanged += (_, _) =>
            {
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

        private Control RenderParagraph(ParagraphBlock paragraph)
        {
            if (paragraph.Inline is not null)
            {
                string raw = ExtractPlainText(paragraph.Inline).Trim();
                if (raw.StartsWith("![[", StringComparison.Ordinal) && raw.EndsWith("]]", StringComparison.Ordinal))
                {
                    string target = raw[3..^2].Trim();
                    if (target.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase) || ResolveEmbeddedDrawing?.Invoke(target) is not null)
                        return RenderEmbeddedNdrawCard(target);
                }
            }

            var tb = CreateInlineTextBlock(14.5, FontWeight.Normal, TextBrush());
            if (paragraph.Inline is not null)
                AppendInlineContainer(tb.Inlines!, paragraph.Inline, 14.5, FontWeight.Normal, FontStyle.Normal, strikethrough: false);
            return tb;
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
                        MinWidth = 20,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, -4, 4, 0)
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
                    var badge = new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(5, 1),
                        Margin = new Thickness(2, 0),
                        Background = CodeCardBrush(),
                        BorderThickness = new Thickness(1),
                        BorderBrush = BorderBrushColor(),
                        Child = new TextBlock
                        {
                            Text = code.Content,
                            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace"),
                            FontSize = Math.Max(12, fontSize - 1.5),
                            Foreground = AccentBrush()
                        }
                    };
                    inlines.Add(new InlineUIContainer(badge) { BaselineAlignment = BaselineAlignment.Center });
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
                        Padding = new Thickness(4, 1),
                        Margin = new Thickness(1, 0),
                        CornerRadius = new CornerRadius(4),
                        Foreground = AccentBrush(),
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0)
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
                        Padding = new Thickness(6, 1),
                        Margin = new Thickness(2, 0),
                        CornerRadius = new CornerRadius(5),
                        Background = AccentTintBrush(),
                        Foreground = AccentBrush(),
                        BorderThickness = new Thickness(0)
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
                        Padding = new Thickness(6, 1),
                        Margin = new Thickness(2, 0),
                        CornerRadius = new CornerRadius(10),
                        Background = TagTintBrush(),
                        Foreground = TagBrush(),
                        BorderThickness = new Thickness(0)
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
                LineHeight = fontSize * 1.48
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

        private IBrush TextBrush() => new SolidColorBrush(Color.Parse(_isDarkTheme ? "#E2E8F0" : "#1E293B"));
        private IBrush MutedBrush() => new SolidColorBrush(Color.Parse(_isDarkTheme ? "#94A3B8" : "#64748B"));
        private IBrush CardBrush() => new SolidColorBrush(Color.Parse(_isDarkTheme ? "#171D2B" : "#F8FAFC"));
        private IBrush CodeCardBrush() => new SolidColorBrush(Color.Parse(_isDarkTheme ? "#0F141F" : "#F1F5F9"));
        private IBrush BorderBrushColor() => new SolidColorBrush(Color.Parse(_isDarkTheme ? "#2B3548" : "#CBD5E1"));
        private IBrush AccentBrush() => new SolidColorBrush(Color.Parse(_isDarkTheme ? "#60A5FA" : "#2563EB"));
        private IBrush AccentTintBrush() => new SolidColorBrush(Color.Parse(_isDarkTheme ? "#1E3A5F" : "#DBEAFE"));
        private IBrush TagBrush() => new SolidColorBrush(Color.Parse(_isDarkTheme ? "#34D399" : "#059669"));
        private IBrush TagTintBrush() => new SolidColorBrush(Color.Parse(_isDarkTheme ? "#064E3B" : "#D1FAE5"));
        private IBrush WarningBrush() => new SolidColorBrush(Color.Parse(_isDarkTheme ? "#FBBF24" : "#D97706"));
    }
}
