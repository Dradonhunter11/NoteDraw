using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NoteDraw.Services
{
    public sealed record HeadingSection(
        int HeadingLineZeroBased,
        int EndLineZeroBased,
        int Level,
        string Title,
        string HeadingRawLine);

    public sealed record EditorGutterLineInfo(
        int LineNumberOneBased,
        int LineIndexZeroBased,
        int StartCharOffset,
        bool IsHeadingH1ToH5,
        int HeadingLevel,
        bool IsFolded,
        bool IsFoldable,
        string HeadingTitle);

    public sealed record FoldedSectionBadge(
        int HeadingLineZeroBased,
        int Level,
        string Title,
        int HiddenLineCount);

    /// <summary>
    /// Detects H1-H5 Markdown heading sections and manages folding/unfolding
    /// sections inside the text editor without losing hidden lines when saving or previewing.
    /// </summary>
    public sealed class EditorFoldManager
    {
        private static readonly Regex HeadingRegex = new(@"^(#{1,5})\s+(.+?)\s*$", RegexOptions.Compiled);
        private static readonly Regex FoldTokenRegex = new(@"\s*«folded:H([1-5]):(\d+)\s+lines#(\d+)»\s*$", RegexOptions.Compiled);

        private readonly Dictionary<int, string> _foldedBodiesById = new();
        private int _nextFoldId = 1;

        public bool HasFoldedSections => _foldedBodiesById.Count > 0;

        public void Clear()
        {
            _foldedBodiesById.Clear();
        }

        public static IReadOnlyList<HeadingSection> DetectSections(string? text)
        {
            var sections = new List<HeadingSection>();
            if (string.IsNullOrEmpty(text))
                return sections;

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            var rawHeadings = new List<(int Line, int Level, string Title, string Raw)>();
            bool inFence = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }
                if (inFence) continue;

                // Strip any fold marker when inspecting heading title
                string cleanLine = FoldTokenRegex.Replace(lines[i], string.Empty);
                var m = HeadingRegex.Match(cleanLine);
                if (m.Success)
                {
                    rawHeadings.Add((i, m.Groups[1].Value.Length, m.Groups[2].Value.Trim(), lines[i]));
                }
            }

            for (int i = 0; i < rawHeadings.Count; i++)
            {
                var h = rawHeadings[i];
                int endLine = lines.Length - 1;
                for (int j = i + 1; j < rawHeadings.Count; j++)
                {
                    if (rawHeadings[j].Level <= h.Level)
                    {
                        endLine = rawHeadings[j].Line - 1;
                        break;
                    }
                }
                sections.Add(new HeadingSection(h.Line, Math.Max(h.Line, endLine), h.Level, h.Title, h.Raw));
            }

            return sections;
        }

        public string ToggleFoldAtLine(string currentEditorText, int headingLineZeroBased)
        {
            if (string.IsNullOrEmpty(currentEditorText))
                return currentEditorText ?? string.Empty;

            string[] lines = currentEditorText.Replace("\r\n", "\n").Split('\n');
            if (headingLineZeroBased < 0 || headingLineZeroBased >= lines.Length)
                return currentEditorText;

            if (FoldTokenRegex.IsMatch(lines[headingLineZeroBased]))
                return UnfoldSectionAtLine(currentEditorText, headingLineZeroBased);

            return FoldSectionAtLine(currentEditorText, headingLineZeroBased);
        }

        public string FoldSectionAtLine(string currentEditorText, int headingLineZeroBased)
        {
            if (string.IsNullOrEmpty(currentEditorText))
                return currentEditorText ?? string.Empty;

            string lineEnding = currentEditorText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = new List<string>(currentEditorText.Replace("\r\n", "\n").Split('\n'));
            if (headingLineZeroBased < 0 || headingLineZeroBased >= lines.Count)
                return currentEditorText;

            if (FoldTokenRegex.IsMatch(lines[headingLineZeroBased]))
                return currentEditorText;

            var sections = DetectSections(currentEditorText);
            HeadingSection? target = null;
            foreach (var sec in sections)
            {
                if (sec.HeadingLineZeroBased == headingLineZeroBased)
                {
                    target = sec;
                    break;
                }
            }

            if (target is null || target.EndLineZeroBased <= target.HeadingLineZeroBased)
                return currentEditorText;

            int count = target.EndLineZeroBased - target.HeadingLineZeroBased;
            var bodySlice = lines.GetRange(target.HeadingLineZeroBased + 1, count);
            string bodyText = string.Join("\n", bodySlice);

            int foldId = _nextFoldId++;
            _foldedBodiesById[foldId] = bodyText;

            lines[target.HeadingLineZeroBased] = $"{lines[target.HeadingLineZeroBased]} «folded:H{target.Level}:{count} lines#{foldId}»";
            lines.RemoveRange(target.HeadingLineZeroBased + 1, count);

            return string.Join(lineEnding, lines);
        }

        public string UnfoldSectionAtLine(string currentEditorText, int headingLineZeroBased)
        {
            if (string.IsNullOrEmpty(currentEditorText))
                return currentEditorText ?? string.Empty;

            string lineEnding = currentEditorText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = new List<string>(currentEditorText.Replace("\r\n", "\n").Split('\n'));
            if (headingLineZeroBased < 0 || headingLineZeroBased >= lines.Count)
                return currentEditorText;

            var match = FoldTokenRegex.Match(lines[headingLineZeroBased]);
            if (!match.Success)
                return currentEditorText;

            lines[headingLineZeroBased] = lines[headingLineZeroBased][..match.Index];
            if (int.TryParse(match.Groups[3].Value, out int foldId) && _foldedBodiesById.Remove(foldId, out string? body))
            {
                if (!string.IsNullOrEmpty(body) || body == string.Empty)
                {
                    var bodyLines = body.Split('\n');
                    lines.InsertRange(headingLineZeroBased + 1, bodyLines);
                }
            }

            return string.Join(lineEnding, lines);
        }

        public string UnfoldAll(string currentEditorText)
        {
            if (string.IsNullOrEmpty(currentEditorText))
                return currentEditorText ?? string.Empty;

            string working = currentEditorText;
            bool changed;
            do
            {
                changed = false;
                string[] lines = working.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (FoldTokenRegex.IsMatch(lines[i]))
                    {
                        working = UnfoldSectionAtLine(working, i);
                        changed = true;
                        break;
                    }
                }
            }
            while (changed);

            return working;
        }

        /// <summary>
        /// Reconstructs the complete unfolded document text without modifying the active fold state.
        /// Used for saving, live Markdown preview, outline extraction, and search.
        /// </summary>
        public string GetFullUnfoldedText(string currentEditorText)
        {
            if (string.IsNullOrEmpty(currentEditorText))
                return currentEditorText ?? string.Empty;

            string lineEnding = currentEditorText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var workingLines = new List<string>(currentEditorText.Replace("\r\n", "\n").Split('\n'));

            bool expandedAny;
            int safety = 0;
            do
            {
                expandedAny = false;
                for (int i = 0; i < workingLines.Count; i++)
                {
                    var match = FoldTokenRegex.Match(workingLines[i]);
                    if (!match.Success) continue;

                    workingLines[i] = workingLines[i][..match.Index];
                    if (int.TryParse(match.Groups[3].Value, out int foldId) &&
                        _foldedBodiesById.TryGetValue(foldId, out string? body))
                    {
                        workingLines.InsertRange(i + 1, body.Split('\n'));
                    }
                    expandedAny = true;
                    break;
                }
                safety++;
            }
            while (expandedAny && safety < 512);

            return string.Join(lineEnding, workingLines);
        }

        public IReadOnlyList<EditorGutterLineInfo> GetGutterLines(string currentEditorText)
        {
            var result = new List<EditorGutterLineInfo>();
            string text = currentEditorText ?? string.Empty;
            var sections = DetectSections(text);
            var sectionByLine = new Dictionary<int, HeadingSection>(sections.Count);
            foreach (var sec in sections)
                sectionByLine[sec.HeadingLineZeroBased] = sec;

            int lineIdx = 0;
            int lineStart = 0;

            for (int i = 0; i <= text.Length; i++)
            {
                if (i == text.Length || text[i] == '\n')
                {
                    int rawEnd = (i > lineStart && text[i - 1] == '\r') ? i - 1 : i;
                    string lineStr = text[lineStart..rawEnd];
                    bool isFolded = FoldTokenRegex.IsMatch(lineStr);

                    if (sectionByLine.TryGetValue(lineIdx, out var sec))
                    {
                        bool foldable = isFolded || sec.EndLineZeroBased > sec.HeadingLineZeroBased;
                        result.Add(new EditorGutterLineInfo(
                            lineIdx + 1, lineIdx, lineStart,
                            IsHeadingH1ToH5: true,
                            sec.Level,
                            isFolded,
                            foldable,
                            sec.Title));
                    }
                    else
                    {
                        result.Add(new EditorGutterLineInfo(
                            lineIdx + 1, lineIdx, lineStart,
                            IsHeadingH1ToH5: false,
                            0,
                            IsFolded: false,
                            IsFoldable: false,
                            string.Empty));
                    }

                    lineIdx++;
                    lineStart = i + 1;
                }
            }

            return result;
        }

        public IReadOnlyList<FoldedSectionBadge> GetFoldedBadges(string currentEditorText)
        {
            var badges = new List<FoldedSectionBadge>();
            if (string.IsNullOrEmpty(currentEditorText))
                return badges;

            string[] lines = currentEditorText.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var m = FoldTokenRegex.Match(lines[i]);
                if (!m.Success) continue;

                int level = int.Parse(m.Groups[1].Value);
                int count = int.Parse(m.Groups[2].Value);
                string clean = FoldTokenRegex.Replace(lines[i], string.Empty);
                var hm = HeadingRegex.Match(clean);
                string title = hm.Success ? hm.Groups[2].Value.Trim() : clean.Trim();
                badges.Add(new FoldedSectionBadge(i, level, title, count));
            }
            return badges;
        }
    }
}
