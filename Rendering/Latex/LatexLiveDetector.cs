using System;
using System.Collections.Generic;

namespace NoteDraw.Rendering.Latex
{
    /// <summary>
    /// Describes the LaTeX expression active at or around the user's editor caret.
    /// </summary>
    public sealed record LatexCursorContext(
        bool IsFound,
        string LatexCode,
        string BlockKind,
        int StartLine,
        int EndLine,
        bool IsDisplayMode,
        bool IsClosed)
    {
        public static readonly LatexCursorContext None =
            new(false, string.Empty, string.Empty, 0, 0, true, true);
    }

    /// <summary>
    /// Detects Obsidian-style fenced LaTeX blocks (```latex / ```math), display math blocks ($$...$$),
    /// and inline math ($...$) around a caret offset in Markdown source text.
    /// </summary>
    public static class LatexLiveDetector
    {
        private readonly record struct LineSpan(int LineNumber, int StartOffset, int EndOffset, string Text);

        /// <summary>
        /// Inspects <paramref name="markdown"/> around <paramref name="caretIndex"/> and returns
        /// the enclosing LaTeX block or inline math expression for real-time live preview.
        /// </summary>
        public static LatexCursorContext DetectAtCursor(string? markdown, int caretIndex)
        {
            if (string.IsNullOrEmpty(markdown))
                return LatexCursorContext.None;

            int clampedCaret = Math.Clamp(caretIndex, 0, markdown.Length);
            var lines = SplitLinesWithOffsets(markdown);
            if (lines.Count == 0)
                return LatexCursorContext.None;

            int caretLineIndex = FindLineIndex(lines, clampedCaret);

            // 1. Scan fenced blocks (```latex, ```math, or non-math fences to skip)
            var fencedResult = DetectFencedBlock(lines, caretLineIndex);
            if (fencedResult.InsideNonMathFence)
                return LatexCursorContext.None;
            if (fencedResult.Context.IsFound)
                return fencedResult.Context;

            // 2. Scan multi-line and single-line $$...$$ display math blocks
            var displayResult = DetectDisplayDollarBlock(lines, caretLineIndex, clampedCaret);
            if (displayResult.IsFound)
                return displayResult;

            // 3. Scan inline $...$ on the caret's current line
            var currentLine = lines[caretLineIndex];
            int col = Math.Clamp(clampedCaret - currentLine.StartOffset, 0, currentLine.Text.Length);
            return DetectInlineMathOnLine(currentLine.Text, col, currentLine.LineNumber);
        }

        private static (LatexCursorContext Context, bool InsideNonMathFence) DetectFencedBlock(
            List<LineSpan> lines,
            int caretLineIndex)
        {
            bool inFence = false;
            string fenceLang = string.Empty;
            int fenceStartLineIdx = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Text.Trim();
                if (!inFence)
                {
                    if (trimmed.StartsWith("```", StringComparison.Ordinal))
                    {
                        inFence = true;
                        fenceLang = trimmed[3..].Trim().ToLowerInvariant();
                        fenceStartLineIdx = i;
                    }
                }
                else
                {
                    if (trimmed.StartsWith("```", StringComparison.Ordinal))
                    {
                        if (caretLineIndex >= fenceStartLineIdx && caretLineIndex <= i)
                        {
                            if (fenceLang is "latex" or "math" or "tex")
                            {
                                string body = JoinBodyLines(lines, fenceStartLineIdx + 1, i - 1);
                                string kind = fenceLang == "math" ? "```math block" : "```latex block";
                                return (new LatexCursorContext(
                                    true, body, kind,
                                    lines[fenceStartLineIdx].LineNumber,
                                    lines[i].LineNumber,
                                    IsDisplayMode: true,
                                    IsClosed: true), false);
                            }

                            return (LatexCursorContext.None, true);
                        }

                        inFence = false;
                        fenceLang = string.Empty;
                        fenceStartLineIdx = -1;
                    }
                }
            }

            // Unclosed fence containing the caret (live typing before typing closing ```)
            if (inFence && caretLineIndex >= fenceStartLineIdx)
            {
                if (fenceLang is "latex" or "math" or "tex")
                {
                    string body = JoinBodyLines(lines, fenceStartLineIdx + 1, lines.Count - 1);
                    string kind = fenceLang == "math" ? "```math block" : "```latex block";
                    return (new LatexCursorContext(
                        true, body, kind,
                        lines[fenceStartLineIdx].LineNumber,
                        lines[^1].LineNumber,
                        IsDisplayMode: true,
                        IsClosed: false), false);
                }
                return (LatexCursorContext.None, true);
            }

            return (LatexCursorContext.None, false);
        }

        private static LatexCursorContext DetectDisplayDollarBlock(
            List<LineSpan> lines,
            int caretLineIndex,
            int caretOffset)
        {
            // Check single-line $$ ... $$ on the current line first
            var line = lines[caretLineIndex];
            int localCol = Math.Clamp(caretOffset - line.StartOffset, 0, line.Text.Length);
            int pos = 0;
            while (pos < line.Text.Length)
            {
                int open = FindNextDoubleDollar(line.Text, pos);
                if (open < 0) break;

                int close = FindNextDoubleDollar(line.Text, open + 2);
                if (close >= 0)
                {
                    if (localCol >= open && localCol <= close + 2)
                    {
                        string inner = line.Text.Substring(open + 2, close - (open + 2)).Trim();
                        return new LatexCursorContext(
                            true, inner, "$$ display block",
                            line.LineNumber, line.LineNumber,
                            IsDisplayMode: true, IsClosed: true);
                    }
                    pos = close + 2;
                }
                else
                {
                    // Only treat as single-line unclosed if there's code after $$ on same line and no multi-line opener below
                    break;
                }
            }

            // Multi-line $$ ... $$ scan (skipping fenced blocks)
            bool inFence = false;
            bool inDollarBlock = false;
            int dollarStartIdx = -1;
            int dollarOpenCol = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                string text = lines[i].Text;
                string trimmed = text.Trim();
                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }
                if (inFence) continue;

                int idx = 0;
                while (idx < text.Length)
                {
                    int found = FindNextDoubleDollar(text, idx);
                    if (found < 0) break;

                    if (!inDollarBlock)
                    {
                        inDollarBlock = true;
                        dollarStartIdx = i;
                        dollarOpenCol = found;
                        idx = found + 2;
                    }
                    else
                    {
                        if (caretLineIndex >= dollarStartIdx && caretLineIndex <= i)
                        {
                            string body = ExtractSpanBody(lines, dollarStartIdx, dollarOpenCol + 2, i, found);
                            return new LatexCursorContext(
                                true, body, "$$ display block",
                                lines[dollarStartIdx].LineNumber, lines[i].LineNumber,
                                IsDisplayMode: true, IsClosed: true);
                        }
                        inDollarBlock = false;
                        dollarStartIdx = -1;
                        idx = found + 2;
                    }
                }
            }

            if (inDollarBlock && caretLineIndex >= dollarStartIdx)
            {
                string body = ExtractSpanBody(lines, dollarStartIdx, dollarOpenCol + 2, lines.Count - 1, lines[^1].Text.Length);
                return new LatexCursorContext(
                    true, body, "$$ display block",
                    lines[dollarStartIdx].LineNumber, lines[^1].LineNumber,
                    IsDisplayMode: true, IsClosed: false);
            }

            return LatexCursorContext.None;
        }

        private static LatexCursorContext DetectInlineMathOnLine(string lineText, int col, int lineNumber)
        {
            var singleDollarPositions = new List<int>();
            for (int i = 0; i < lineText.Length; i++)
            {
                if (lineText[i] == '\\')
                {
                    i++; // skip escaped character like \$
                    continue;
                }

                if (lineText[i] == '$')
                {
                    if (i + 1 < lineText.Length && lineText[i + 1] == '$')
                    {
                        i++; // skip $$
                        continue;
                    }
                    singleDollarPositions.Add(i);
                }
            }

            for (int p = 0; p + 1 < singleDollarPositions.Count; p += 2)
            {
                int open = singleDollarPositions[p];
                int close = singleDollarPositions[p + 1];
                if (col >= open && col <= close + 1)
                {
                    string inner = lineText.Substring(open + 1, close - open - 1).Trim();
                    return new LatexCursorContext(
                        true, inner, "$ inline math",
                        lineNumber, lineNumber,
                        IsDisplayMode: false, IsClosed: true);
                }
            }

            // Unclosed trailing single '$' while actively typing on that line
            if (singleDollarPositions.Count % 2 == 1)
            {
                int open = singleDollarPositions[^1];
                if (col >= open + 1)
                {
                    string inner = lineText[(open + 1)..];
                    if (!string.IsNullOrWhiteSpace(inner) && !char.IsWhiteSpace(inner[0]))
                    {
                        return new LatexCursorContext(
                            true, inner.Trim(), "$ inline math",
                            lineNumber, lineNumber,
                            IsDisplayMode: false, IsClosed: false);
                    }
                }
            }

            return LatexCursorContext.None;
        }

        private static int FindNextDoubleDollar(string text, int start)
        {
            for (int i = start; i + 1 < text.Length; i++)
            {
                if (text[i] == '\\')
                {
                    i++;
                    continue;
                }
                if (text[i] == '$' && text[i + 1] == '$')
                    return i;
            }
            return -1;
        }

        private static string JoinBodyLines(List<LineSpan> lines, int firstLineIdx, int lastLineIdx)
        {
            if (lastLineIdx < firstLineIdx || firstLineIdx >= lines.Count)
                return string.Empty;

            var parts = new List<string>(lastLineIdx - firstLineIdx + 1);
            for (int i = firstLineIdx; i <= lastLineIdx && i < lines.Count; i++)
                parts.Add(lines[i].Text);
            return string.Join("\n", parts).Trim();
        }

        private static string ExtractSpanBody(
            List<LineSpan> lines,
            int startLineIdx,
            int startCol,
            int endLineIdx,
            int endCol)
        {
            if (startLineIdx == endLineIdx)
            {
                string t = lines[startLineIdx].Text;
                int len = Math.Max(0, Math.Min(endCol, t.Length) - startCol);
                return startCol <= t.Length ? t.Substring(startCol, len).Trim() : string.Empty;
            }

            var parts = new List<string>();
            string first = lines[startLineIdx].Text;
            if (startCol < first.Length)
                parts.Add(first[startCol..]);

            for (int i = startLineIdx + 1; i < endLineIdx; i++)
                parts.Add(lines[i].Text);

            string last = lines[endLineIdx].Text;
            int take = Math.Clamp(endCol, 0, last.Length);
            if (take > 0)
                parts.Add(last[..take]);

            return string.Join("\n", parts).Trim();
        }

        private static List<LineSpan> SplitLinesWithOffsets(string text)
        {
            var result = new List<LineSpan>();
            int lineNum = 1;
            int lineStart = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    int end = (i > lineStart && text[i - 1] == '\r') ? i - 1 : i;
                    result.Add(new LineSpan(lineNum++, lineStart, i, text[lineStart..end]));
                    lineStart = i + 1;
                }
            }

            if (lineStart <= text.Length)
            {
                int end = text.Length;
                if (end > lineStart && text[end - 1] == '\r')
                    end--;
                result.Add(new LineSpan(lineNum, lineStart, text.Length, text[lineStart..end]));
            }

            return result;
        }

        private static int FindLineIndex(List<LineSpan> lines, int offset)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (offset <= lines[i].EndOffset)
                    return i;
            }
            return lines.Count - 1;
        }
    }
}
