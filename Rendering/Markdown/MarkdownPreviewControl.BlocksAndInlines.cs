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
    public sealed partial class MarkdownPreviewControl
    {
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
                    string rawInfo = (fenced.Info ?? string.Empty).Trim();
                    string info = rawInfo.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;
                    string body = fenced.Lines.ToString();
                    if (info is "latex" or "math" or "tex")
                        return RenderLatexCard(body.Trim(), $"```{info} block", baseLineZeroBased);
                    if (info is "drawing" or "ndraw")
                        return RenderInlineDrawingCard(body, baseLineZeroBased);
                    return RenderCodeCard(body.TrimEnd(), string.IsNullOrEmpty(rawInfo) ? "text" : rawInfo);
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


        private Control RenderCodeCard(string code, string language)
        {
            string displayLang = CodeSyntaxHighlighter.GetDisplayName(language);
            string normalizedLang = CodeSyntaxHighlighter.NormalizeLanguage(language);
            var monoFont = new FontFamily("Cascadia Code, Consolas, Courier New, monospace");

            var copyBtn = new Button
            {
                Content = "Copy",
                FontSize = 11,
                Padding = new Thickness(8, 2),
                CornerRadius = new CornerRadius(4)
            };
            copyBtn.Click += async (_, _) =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard is not null)
                    await topLevel.Clipboard.SetTextAsync(code);
                copyBtn.Content = "Copied ✓";
                await System.Threading.Tasks.Task.Delay(1500);
                copyBtn.Content = "Copy";
            };

            var langBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2),
                Background = AccentTintBrush(),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = displayLang,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = AccentBrush()
                }
            };

            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            header.Children.Add(langBadge);
            Grid.SetColumn(copyBtn, 1);
            header.Children.Add(copyBtn);

            var codeText = new SelectableTextBlock
            {
                FontFamily = monoFont,
                FontSize = 13,
                Foreground = CodeSyntaxHighlighter.GetTokenBrush(CodeTokenKind.Plain, _isDarkTheme),
                TextWrapping = TextWrapping.NoWrap
            };
            var tokens = CodeSyntaxHighlighter.Tokenize(code, normalizedLang);
            foreach (var token in tokens)
            {
                codeText.Inlines!.Add(new Run(token.Text)
                {
                    Foreground = CodeSyntaxHighlighter.GetTokenBrush(token.Kind, _isDarkTheme),
                    FontWeight = CodeSyntaxHighlighter.GetTokenFontWeight(token.Kind),
                    FontStyle = CodeSyntaxHighlighter.GetTokenFontStyle(token.Kind)
                });
            }

            var codeScroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                AllowAutoHide = false,
                Content = codeText
            };

            string[] splitLines = code.Replace("\r\n", "\n").Split('\n');
            Control bodyControl;
            if (splitLines.Length > 1)
            {
                string lineNumbers = string.Join("\n", Enumerable.Range(1, splitLines.Length));
                var gutter = new TextBlock
                {
                    Text = lineNumbers,
                    FontFamily = monoFont,
                    FontSize = 13,
                    TextAlignment = TextAlignment.Right,
                    Foreground = MutedBrush(),
                    Opacity = 0.55,
                    Margin = new Thickness(0, 0, 10, 0),
                    Padding = new Thickness(0)
                };
                var codeGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                codeGrid.Children.Add(gutter);
                Grid.SetColumn(codeScroller, 1);
                codeGrid.Children.Add(codeScroller);
                bodyControl = codeGrid;
            }
            else
            {
                bodyControl = codeScroller;
            }

            return new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = BorderBrushColor(),
                Background = CodeCardBrush(),
                Padding = new Thickness(12, 8),
                Child = new StackPanel
                {
                    Spacing = 8,
                    Children = { header, new Border { Height = 1, Background = BorderBrushColor(), Opacity = 0.6 }, bodyControl }
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
    }
}
