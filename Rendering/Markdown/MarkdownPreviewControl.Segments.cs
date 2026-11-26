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

                if (TryParseFencedSegment(lines, i, trimmed, out var fencedSeg, out int nextIdx) ||
                    TryParseDisplayMathSegment(lines, i, raw, trimmed, out fencedSeg, out nextIdx) ||
                    TryParseTableSegment(lines, i, trimmed, out fencedSeg, out nextIdx))
                {
                    segments.Add(fencedSeg);
                    i = nextIdx;
                    continue;
                }

                segments.Add(ParseSingleLineSegment(raw, trimmed, i));
                i++;
            }

            return segments;
        }

        private static bool TryParseFencedSegment(
            string[] lines, int startLine, string trimmed, out EditorSegment segment, out int nextIndex)
        {
            if (!trimmed.StartsWith("```", StringComparison.Ordinal) && !trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                segment = null!;
                nextIndex = startLine;
                return false;
            }

            string fencePrefix = trimmed.StartsWith("~~~", StringComparison.Ordinal) ? "~~~" : "```";
            int closeIdx = lines.Length - 1;
            for (int j = startLine + 1; j < lines.Length; j++)
            {
                if (lines[j].Trim().StartsWith(fencePrefix, StringComparison.Ordinal))
                {
                    closeIdx = j;
                    break;
                }
            }

            string rawInfo = trimmed.Length > 3 ? trimmed[3..].Trim() : string.Empty;
            string info = rawInfo.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;
            var kind = info switch
            {
                "drawing" or "ndraw" => EditorSegmentKind.FencedDrawing,
                "latex" or "math" or "tex" => EditorSegmentKind.FencedLatex,
                _ => EditorSegmentKind.FencedCode
            };
            segment = new EditorSegment
            {
                StartLine = startLine,
                EndLine = closeIdx,
                Kind = kind,
                RawText = string.Join("\n", lines[startLine..(closeIdx + 1)])
            };
            nextIndex = closeIdx + 1;
            return true;
        }

        private static bool TryParseDisplayMathSegment(
            string[] lines, int startLine, string raw, string trimmed, out EditorSegment segment, out int nextIndex)
        {
            if (trimmed.StartsWith("$$", StringComparison.Ordinal))
            {
                int second = raw.IndexOf("$$", raw.IndexOf("$$", StringComparison.Ordinal) + 2, StringComparison.Ordinal);
                int closeIdx = startLine;
                if (second < 0)
                {
                    closeIdx = lines.Length - 1;
                    for (int j = startLine + 1; j < lines.Length; j++)
                    {
                        if (lines[j].Trim().EndsWith("$$", StringComparison.Ordinal))
                        {
                            closeIdx = j;
                            break;
                        }
                    }
                }
                segment = new EditorSegment
                {
                    StartLine = startLine,
                    EndLine = closeIdx,
                    Kind = EditorSegmentKind.DisplayMath,
                    RawText = string.Join("\n", lines[startLine..(closeIdx + 1)])
                };
                nextIndex = closeIdx + 1;
                return true;
            }

            if (trimmed.StartsWith(@"\[", StringComparison.Ordinal))
            {
                int closeIdx = startLine;
                if (!(trimmed.EndsWith(@"\]", StringComparison.Ordinal) && trimmed.Length >= 4))
                {
                    closeIdx = lines.Length - 1;
                    for (int j = startLine + 1; j < lines.Length; j++)
                    {
                        if (lines[j].Trim().EndsWith(@"\]", StringComparison.Ordinal))
                        {
                            closeIdx = j;
                            break;
                        }
                    }
                }
                segment = new EditorSegment
                {
                    StartLine = startLine,
                    EndLine = closeIdx,
                    Kind = EditorSegmentKind.DisplayMath,
                    RawText = string.Join("\n", lines[startLine..(closeIdx + 1)])
                };
                nextIndex = closeIdx + 1;
                return true;
            }

            segment = null!;
            nextIndex = startLine;
            return false;
        }

        private static bool TryParseTableSegment(
            string[] lines, int startLine, string trimmed, out EditorSegment segment, out int nextIndex)
        {
            if (!trimmed.StartsWith('|') || startLine + 1 >= lines.Length || !lines[startLine + 1].Trim().StartsWith('|'))
            {
                segment = null!;
                nextIndex = startLine;
                return false;
            }

            int endTable = startLine + 1;
            while (endTable + 1 < lines.Length && lines[endTable + 1].Trim().StartsWith('|'))
                endTable++;

            segment = new EditorSegment
            {
                StartLine = startLine,
                EndLine = endTable,
                Kind = EditorSegmentKind.Table,
                RawText = string.Join("\n", lines[startLine..(endTable + 1)])
            };
            nextIndex = endTable + 1;
            return true;
        }

        private static EditorSegment ParseSingleLineSegment(string raw, string trimmed, int lineIndex)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new EditorSegment { StartLine = lineIndex, EndLine = lineIndex, Kind = EditorSegmentKind.BlankLine, RawText = raw };

            if (trimmed.StartsWith("![[", StringComparison.Ordinal) && trimmed.EndsWith("]]", StringComparison.Ordinal))
            {
                string cleanTarget = ExtractCleanEmbedTarget(trimmed[3..^2].Trim(), out _, out _);
                if (IsImageExtension(cleanTarget))
                    return new EditorSegment { StartLine = lineIndex, EndLine = lineIndex, Kind = EditorSegmentKind.MarkdownImage, RawText = raw };
                if (cleanTarget.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase))
                    return new EditorSegment { StartLine = lineIndex, EndLine = lineIndex, Kind = EditorSegmentKind.EmbeddedDrawing, RawText = raw };
            }

            if (TryParseMarkdownImageLine(trimmed, out _, out _, out _))
                return new EditorSegment { StartLine = lineIndex, EndLine = lineIndex, Kind = EditorSegmentKind.MarkdownImage, RawText = raw };

            var hMatch = HeadingLineRegex.Match(raw);
            if (hMatch.Success)
            {
                return new EditorSegment
                {
                    StartLine = lineIndex,
                    EndLine = lineIndex,
                    Kind = EditorSegmentKind.Heading,
                    HeadingLevel = hMatch.Groups[1].Value.Length,
                    RawText = raw
                };
            }

            return new EditorSegment { StartLine = lineIndex, EndLine = lineIndex, Kind = EditorSegmentKind.SingleLineMarkdown, RawText = raw };
        }

        private Control RenderActiveTextEditorSegment(EditorSegment segment, bool isCollapsed)
        {
            bool isMultiLine = segment.Kind is EditorSegmentKind.FencedLatex
                or EditorSegmentKind.DisplayMath
                or EditorSegmentKind.FencedCode
                or EditorSegmentKind.Table;

            var editBox = CreateActiveSegmentTextBox(segment, isMultiLine);
            var (liveMathCard, refreshLiveLatex) = BuildSegmentLiveLatexCard(segment, editBox);
            WireActiveSegmentInteraction(segment, editBox, isMultiLine, refreshLiveLatex);

            refreshLiveLatex();
            EmitActiveSegmentCursorPos(segment, editBox);

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

        private TextBox CreateActiveSegmentTextBox(EditorSegment segment, bool isMultiLine)
        {
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
                CaretBrush = TextBrush(),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 1),
                MinHeight = 0,
                CaretIndex = resolvedCaret,
                SelectionStart = resolvedCaret,
                SelectionEnd = resolvedCaret
            };

            _activeSegmentTextBox = editBox;
            _pendingActiveEditorCaret = resolvedCaret;
            return editBox;
        }

        private (Border Card, Action Refresh) BuildSegmentLiveLatexCard(EditorSegment segment, TextBox editBox)
        {
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

            void Refresh()
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

            return (liveMathCard, Refresh);
        }

        private void EmitActiveSegmentCursorPos(EditorSegment segment, TextBox editBox)
        {
            string currentText = editBox.Text ?? string.Empty;
            int caret = Math.Clamp(editBox.CaretIndex, 0, currentText.Length);
            var (relLn, col) = GetLineAndColumn(currentText, caret);
            CursorPositionChanged?.Invoke(this, (segment.StartLine + relLn, col));
        }

        private void WireActiveSegmentInteraction(
            EditorSegment segment, TextBox editBox, bool isMultiLine, Action refreshLiveLatex)
        {
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
                refreshLiveLatex();
                EmitActiveSegmentCursorPos(segment, editBox);
                DocumentEditedFromPreview?.Invoke(this, updatedMd);
            };

            editBox.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBox.CaretIndexProperty)
                {
                    refreshLiveLatex();
                    EmitActiveSegmentCursorPos(segment, editBox);
                }
            };

            editBox.AddHandler(InputElement.KeyDownEvent, (_, ke) =>
            {
                if (HandleActiveSegmentKeyDown(segment, editBox, isMultiLine, ke, () => suppressSelfTextChanged = true))
                    ke.Handled = true;
            }, RoutingStrategies.Tunnel);
        }

        private bool HandleActiveSegmentKeyDown(
            EditorSegment segment, TextBox editBox, bool isMultiLine, KeyEventArgs ke, Action suppressTextChanged)
        {
            if (PlatformShortcuts.HasPrimaryModifier(ke.KeyModifiers))
                return ke.Key == Key.V && TryHandleImagePasteCallback is not null && TryHandleImagePasteCallback(segment.StartLine);

            string curText = editBox.Text ?? string.Empty;
            int caret = Math.Clamp(editBox.CaretIndex, 0, curText.Length);
            int curLineIdx = GetCaretLineIndex(curText, caret, out int colInLine, out int totalLines);

            if (ke.Key == Key.Up && curLineIdx == 0)
                return TryActivateRelativeSegment(segment.StartLine, -1, targetColumn: colInLine, fromTop: false);

            if (ke.Key == Key.Down && curLineIdx == totalLines - 1)
                return TryActivateRelativeSegment(segment.StartLine, +1, targetColumn: colInLine, fromTop: true);

            if (ke.Key == Key.Left && caret == 0 && editBox.SelectionStart == editBox.SelectionEnd)
                return TryActivateRelativeSegment(segment.StartLine, -1, caretAtAbsoluteEnd: true);

            if (ke.Key == Key.Right && caret == curText.Length && editBox.SelectionStart == editBox.SelectionEnd)
                return TryActivateRelativeSegment(segment.StartLine, +1, caretAtAbsoluteStart: true);

            if (ke.Key == Key.Escape)
            {
                DeactivateActiveSegment();
                return true;
            }

            if (ke.Key == Key.Tab && segment.Kind == EditorSegmentKind.FencedCode &&
                !PlatformShortcuts.HasAnyCommandOrAltModifier(ke.KeyModifiers) &&
                !ke.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                int selStart = Math.Clamp(editBox.SelectionStart, 0, curText.Length);
                int selEnd = Math.Clamp(editBox.SelectionEnd, 0, curText.Length);
                if (selStart > selEnd) (selStart, selEnd) = (selEnd, selStart);
                editBox.Text = curText[..selStart] + "    " + curText[selEnd..];
                editBox.CaretIndex = selStart + 4;
                return true;
            }

            if (ke.Key == Key.Enter && !ke.KeyModifiers.HasFlag(KeyModifiers.Shift))
                return HandleActiveSegmentEnter(segment, curText, caret, isMultiLine, suppressTextChanged);

            if (ke.Key == Key.Back && caret == 0 && editBox.SelectionStart == editBox.SelectionEnd && segment.StartLine > 0)
                return HandleActiveSegmentBackspace(segment, curText, suppressTextChanged);

            if (ke.Key == Key.Delete && caret == curText.Length && editBox.SelectionStart == editBox.SelectionEnd)
                return HandleActiveSegmentDelete(segment, curText, caret, suppressTextChanged);

            return false;
        }

        private bool HandleActiveSegmentEnter(
            EditorSegment segment, string curText, int caret, bool isMultiLine, Action suppressTextChanged)
        {
            string trimStart = curText.TrimStart();
            bool keepInsideMultiLine = isMultiLine ||
                trimStart.StartsWith("```", StringComparison.Ordinal) ||
                trimStart.StartsWith("~~~", StringComparison.Ordinal) ||
                trimStart.StartsWith("$$", StringComparison.Ordinal) ||
                trimStart.StartsWith(@"\[", StringComparison.Ordinal);

            if (keepInsideMultiLine)
                return false;

            string splitSlice = $"{curText[..caret]}\n{curText[caret..]}";
            suppressTextChanged();
            string updatedMd = ReplaceLineRange(_markdown, segment.StartLine, segment.EndLine, splitSlice);
            _activeSegmentStartLine = segment.StartLine + 1;
            _pendingCaretOffset = 0;
            RenderMarkdown(updatedMd);
            DocumentEditedFromPreview?.Invoke(this, _markdown);
            return true;
        }

        private bool HandleActiveSegmentBackspace(EditorSegment segment, string curText, Action suppressTextChanged)
        {
            var prevAdj = FindImmediatelyPrecedingSegment(segment.StartLine);
            if (prevAdj is not null && IsPictureSegment(prevAdj.Kind))
            {
                suppressTextChanged();
                DeleteImageBlockAtLineRange(prevAdj.StartLine, prevAdj.EndLine, nextActiveLine: prevAdj.StartLine, nextCaretOffset: 0);
                return true;
            }

            if (prevAdj is not null &&
                prevAdj.Kind is EditorSegmentKind.BlankLine or EditorSegmentKind.Heading or EditorSegmentKind.SingleLineMarkdown)
            {
                int joinOffset = prevAdj.RawText.Length;
                string joined = prevAdj.RawText + curText;
                suppressTextChanged();
                string updatedMd = ReplaceLineRange(_markdown, prevAdj.StartLine, segment.EndLine, joined);
                _activeSegmentStartLine = prevAdj.StartLine;
                _pendingCaretOffset = joinOffset;
                RenderMarkdown(updatedMd);
                DocumentEditedFromPreview?.Invoke(this, _markdown);
                return true;
            }
            return false;
        }

        private bool HandleActiveSegmentDelete(EditorSegment segment, string curText, int caret, Action suppressTextChanged)
        {
            var nextAdj = FindImmediatelyFollowingSegment(segment.EndLine);
            if (nextAdj is not null && IsPictureSegment(nextAdj.Kind))
            {
                suppressTextChanged();
                DeleteImageBlockAtLineRange(nextAdj.StartLine, nextAdj.EndLine, nextActiveLine: segment.StartLine, nextCaretOffset: caret);
                return true;
            }

            if (nextAdj is not null &&
                nextAdj.Kind is EditorSegmentKind.BlankLine or EditorSegmentKind.Heading or EditorSegmentKind.SingleLineMarkdown)
            {
                string joined = curText + nextAdj.RawText;
                suppressTextChanged();
                string updatedMd = ReplaceLineRange(_markdown, segment.StartLine, nextAdj.EndLine, joined);
                _activeSegmentStartLine = segment.StartLine;
                _pendingCaretOffset = caret;
                RenderMarkdown(updatedMd);
                DocumentEditedFromPreview?.Invoke(this, _markdown);
                return true;
            }
            return false;
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
                    _lastSegments[^1].StartLine >= currentStartLine)
                {
                    string lineEnding = _markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                    string next = _markdown + lineEnding;
                    int newTailLine = next.Replace("\r\n", "\n").Split('\n').Length - 1;
                    _activeSegmentStartLine = newTailLine;
                    _activePictureCursorIsBefore = false;
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
                _recoverEditorFocusOnPointerRelease = true;
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
    }
}
