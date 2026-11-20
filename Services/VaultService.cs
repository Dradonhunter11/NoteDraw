using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NoteDraw.Drawing;

namespace NoteDraw.Services
{
    public enum VaultEntryKind
    {
        Folder,
        MarkdownNote,
        DrawingFile
    }

    /// <summary>
    /// Represents a folder, Markdown note (.md), or standalone vector drawing (.ndraw) in a Vault.
    /// </summary>
    public sealed record VaultEntry(
        string Name,
        string FullPath,
        string RelativePath,
        VaultEntryKind Kind,
        int Depth);

    /// <summary>
    /// Heading item extracted from a Markdown document for the Outline view.
    /// </summary>
    public sealed record OutlineItem(int Level, string Title, int LineNumber);

    /// <summary>
    /// Manages the active local note Vault folder, sample starter files,
    /// file CRUD operations, outline/tag/wiki-link indexing, and cross-note resolution.
    /// </summary>
    public sealed class VaultService
    {
        private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.+?)\s*$", RegexOptions.Compiled);
        private static readonly Regex TagRegex = new(@"(?<![A-Za-z0-9_/&#])#([A-Za-z][A-Za-z0-9_\-/]*)", RegexOptions.Compiled);
        private static readonly Regex WikiLinkRegex = new(@"\[\[([^\]\|]+?)(?:\|[^\]]+?)?\]\]", RegexOptions.Compiled);

        public string VaultPath { get; private set; }

        public VaultService(string? initialVaultPath = null)
        {
            VaultPath = string.IsNullOrWhiteSpace(initialVaultPath)
                ? GetDefaultVaultPath()
                : Path.GetFullPath(initialVaultPath);

            EnsureVaultInitialized();
        }

        public static string GetDefaultVaultPath()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApp))
                localApp = Path.GetTempPath();
            return Path.Combine(localApp, "NoteDraw", "SampleVault");
        }

        public void OpenVault(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("Vault path cannot be empty.", nameof(folderPath));

            VaultPath = Path.GetFullPath(folderPath);
            Directory.CreateDirectory(VaultPath);
        }

        public void EnsureVaultInitialized()
        {
            Directory.CreateDirectory(VaultPath);
            bool hasContent = Directory.EnumerateFiles(VaultPath, "*.*", SearchOption.AllDirectories)
                .Any(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                          f.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase));

            if (!hasContent)
                CreateSampleVaultFiles();
        }

        public IReadOnlyList<VaultEntry> GetEntries(string? searchFilter = null)
        {
            var entries = new List<VaultEntry>();
            if (!Directory.Exists(VaultPath))
                return entries;

            TraverseDirectory(VaultPath, depth: 0, entries);

            if (string.IsNullOrWhiteSpace(searchFilter))
                return entries;

            string query = searchFilter.Trim();
            return entries
                .Where(e => e.Kind != VaultEntryKind.Folder &&
                            (e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                             e.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        private void TraverseDirectory(string dir, int depth, List<VaultEntry> output)
        {
            var subDirs = Directory.GetDirectories(dir)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

            foreach (string sub in subDirs)
            {
                string rel = Path.GetRelativePath(VaultPath, sub);
                output.Add(new VaultEntry(Path.GetFileName(sub), sub, rel, VaultEntryKind.Folder, depth));
                TraverseDirectory(sub, depth + 1, output);
            }

            var files = Directory.GetFiles(dir)
                .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                string rel = Path.GetRelativePath(VaultPath, file);
                var kind = file.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase)
                    ? VaultEntryKind.DrawingFile
                    : VaultEntryKind.MarkdownNote;
                output.Add(new VaultEntry(Path.GetFileName(file), file, rel, kind, depth));
            }
        }

        public string ReadFile(string fullPath)
            => File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;

        public void WriteFile(string fullPath, string content)
        {
            string? parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            File.WriteAllText(fullPath, content);
        }

        public VaultEntry CreateNote(string title, string? initialContent = null, string? subfolderRelative = null)
        {
            string safe = SanitizeFileName(title, "Untitled Note");
            string fileName = safe.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? safe : $"{safe}.md";
            string targetDir = string.IsNullOrWhiteSpace(subfolderRelative)
                ? VaultPath
                : Path.Combine(VaultPath, subfolderRelative);
            Directory.CreateDirectory(targetDir);

            string fullPath = EnsureUniquePath(Path.Combine(targetDir, fileName));
            string displayName = Path.GetFileNameWithoutExtension(fullPath);
            string content = initialContent ?? $"# {displayName}\n\nStart writing Markdown, `$LaTeX$`, or insert a drawing block!\n";
            File.WriteAllText(fullPath, content);

            string rel = Path.GetRelativePath(VaultPath, fullPath);
            return new VaultEntry(Path.GetFileName(fullPath), fullPath, rel, VaultEntryKind.MarkdownNote, 0);
        }

        public VaultEntry CreateDrawing(string title, DrawingDocument? initialDoc = null, string? subfolderRelative = null)
        {
            string safe = SanitizeFileName(title, "Untitled Drawing");
            string fileName = safe.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase) ? safe : $"{safe}.ndraw";
            string targetDir = string.IsNullOrWhiteSpace(subfolderRelative)
                ? VaultPath
                : Path.Combine(VaultPath, subfolderRelative);
            Directory.CreateDirectory(targetDir);

            string fullPath = EnsureUniquePath(Path.Combine(targetDir, fileName));
            var doc = initialDoc ?? new DrawingDocument();
            File.WriteAllText(fullPath, doc.ToJson());

            string rel = Path.GetRelativePath(VaultPath, fullPath);
            return new VaultEntry(Path.GetFileName(fullPath), fullPath, rel, VaultEntryKind.DrawingFile, 0);
        }

        public bool RenameEntry(string oldPath, string newName, out string newPath)
        {
            newPath = oldPath;
            if (!File.Exists(oldPath) && !Directory.Exists(oldPath))
                return false;

            string? dir = Path.GetDirectoryName(oldPath);
            if (string.IsNullOrEmpty(dir))
                return false;

            string ext = Path.GetExtension(oldPath);
            string sanitized = SanitizeFileName(newName, Path.GetFileNameWithoutExtension(oldPath));
            if (!string.IsNullOrEmpty(ext) && !sanitized.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                sanitized += ext;

            string candidate = Path.Combine(dir, sanitized);
            if (string.Equals(candidate, oldPath, StringComparison.OrdinalIgnoreCase) || File.Exists(candidate) || Directory.Exists(candidate))
                return false;

            if (File.Exists(oldPath))
                File.Move(oldPath, candidate);
            else
                Directory.Move(oldPath, candidate);

            newPath = candidate;
            return true;
        }

        public void DeleteEntry(string fullPath)
        {
            if (File.Exists(fullPath))
                File.Delete(fullPath);
            else if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, recursive: true);
        }

        public VaultEntry? ResolveWikiLink(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return null;

            string clean = target.Trim();
            int pipeIdx = clean.IndexOf('|');
            if (pipeIdx >= 0)
                clean = clean[..pipeIdx].Trim();
            int hashIdx = clean.IndexOf('#');
            if (hashIdx >= 0)
                clean = clean[..hashIdx].Trim();

            var files = GetEntries().Where(e => e.Kind != VaultEntryKind.Folder).ToList();
            return files.FirstOrDefault(e =>
                       string.Equals(e.Name, clean, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Path.GetFileNameWithoutExtension(e.Name), clean, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(e.RelativePath.Replace('\\', '/'), clean.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Extracts Markdown headings (# to ######) with 1-based line numbers, ignoring fenced code blocks.
        /// </summary>
        public static IReadOnlyList<OutlineItem> ExtractOutline(string? markdown)
        {
            var items = new List<OutlineItem>();
            if (string.IsNullOrEmpty(markdown))
                return items;

            string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
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

                var match = HeadingRegex.Match(lines[i]);
                if (match.Success)
                {
                    items.Add(new OutlineItem(match.Groups[1].Value.Length, match.Groups[2].Value.Trim(), i + 1));
                }
            }
            return items;
        }

        /// <summary>
        /// Extracts unique #tags outside fenced code blocks.
        /// </summary>
        public static IReadOnlyList<string> ExtractTags(string? markdown)
        {
            var tags = new List<string>();
            if (string.IsNullOrEmpty(markdown))
                return tags;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
            bool inFence = false;
            foreach (string line in lines)
            {
                if (line.Trim().StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }
                if (inFence) continue;

                foreach (Match m in TagRegex.Matches(line))
                {
                    string tag = "#" + m.Groups[1].Value;
                    if (seen.Add(tag))
                        tags.Add(tag);
                }
            }
            return tags;
        }

        /// <summary>
        /// Extracts [[WikiLink]] target names from Markdown content.
        /// </summary>
        public static IReadOnlyList<string> ExtractWikiLinks(string? markdown)
        {
            var links = new List<string>();
            if (string.IsNullOrEmpty(markdown))
                return links;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in WikiLinkRegex.Matches(markdown))
            {
                string target = m.Groups[1].Value.Trim();
                if (seen.Add(target))
                    links.Add(target);
            }
            return links;
        }

        private void CreateSampleVaultFiles()
        {
            var welcomeDiagram = CreateStarterDiagram();
            string welcomeNote =
@"# Welcome to NoteDraw

#welcome #notedraw #tutorial

**NoteDraw** combines Obsidian-style **Markdown**, real-time **LaTeX math**, and **vector hand drawing** in one workspace.

## Quick Tips
- [x] **Closable Views**: Click `×` on any panel header to close it, and restore it anytime from the **View** tab in the top toolbar.
- [x] **Live LaTeX Preview**: Place your cursor inside the ```` ```latex ```` block below and edit it to see the formula render as you type!
- [ ] **Inline Hand Drawing**: Sketch directly on the interactive canvas embedded below, or open [[Sketchpad Demo.ndraw]].
- [ ] Explore more equations in [[LaTeX & Math Showcase]].

## Live LaTeX Block (Try editing inside!)

```latex
\int_0^\infty e^{-x^2}\,dx = \frac{\sqrt{\pi}}{2}
```

Inline math also renders inside sentences, such as Euler's identity $e^{i\pi} + 1 = 0$ and the quadratic formula $x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}$.

## Inline Hand Drawing Canvas

```drawing
" + welcomeDiagram.ToJson() + @"
```
";
            WriteFile(Path.Combine(VaultPath, "Welcome to NoteDraw.md"), welcomeNote);

            string mathNote =
@"# LaTeX & Math Showcase

#math #calculus #algebra

Click inside any ```` ```latex ````, ```` ```math ````, `$$...$$`, or `$...$` block in the editor to trigger the **Live LaTeX Code Preview** below the editor.

## Fenced LaTeX Block (Obsidian Style)
```latex
\sum_{n=1}^{\infty} \frac{1}{n^2} = \frac{\pi^2}{6}
```

## Fenced Math Block
```math
f(x) = \begin{cases}
x^2 & x \ge 0 \\
-x & x < 0
\end{cases}
```

## Display Math (`$$...$$`)
$$
A = \begin{pmatrix}
\cos\theta & -\sin\theta \\
\sin\theta & \cos\theta
\end{pmatrix}
$$

## Vectors, Roots & Accents
- Gradient vector: $\nabla f = \left( \frac{\partial f}{\partial x}, \frac{\partial f}{\partial y} \right)$
- Unit vector & mean: $\hat{u} = \frac{\vec{v}}{\|\vec{v}\|}, \quad \bar{x} = \frac{1}{N}\sum_{i=1}^N x_i$
- $n$-th root: $\sqrt[n]{a^n + b^n}$

| Expression | Meaning |
| --- | --- |
| $\forall x \in \mathbb{R}$ | Universal quantifier |
| $\exists \delta > 0$ | Existential quantifier |
| $A \subseteq B$ | Subset relation |
";
            WriteFile(Path.Combine(VaultPath, "LaTeX & Math Showcase.md"), mathNote);

            var sketchDoc = new DrawingDocument
            {
                Background = CanvasBackgroundKind.Dotted,
                Strokes =
                {
                    new DrawingStroke
                    {
                        Tool = DrawingToolKind.Rectangle,
                        ColorHex = "#3B82F6",
                        Thickness = 3,
                        Points = { new(60, 50), new(300, 170) }
                    },
                    new DrawingStroke
                    {
                        Tool = DrawingToolKind.TextLabel,
                        ColorHex = "#3B82F6",
                        TextContent = "\\frac{dy}{dx} = ky",
                        IsLatex = true,
                        Points = { new(115, 92) }
                    },
                    new DrawingStroke
                    {
                        Tool = DrawingToolKind.Arrow,
                        ColorHex = "#10B981",
                        Thickness = 3,
                        Points = { new(305, 110), new(420, 110) }
                    },
                    new DrawingStroke
                    {
                        Tool = DrawingToolKind.Ellipse,
                        ColorHex = "#A855F7",
                        Thickness = 3,
                        Points = { new(430, 55), new(620, 165) }
                    },
                    new DrawingStroke
                    {
                        Tool = DrawingToolKind.TextLabel,
                        ColorHex = "#A855F7",
                        TextContent = "y(t) = Ce^{kt}",
                        IsLatex = true,
                        Points = { new(468, 95) }
                    }
                }
            };
            WriteFile(Path.Combine(VaultPath, "Sketchpad Demo.ndraw"), sketchDoc.ToJson());
        }

        private static DrawingDocument CreateStarterDiagram() => new()
        {
            Background = CanvasBackgroundKind.Grid,
            Strokes =
            {
                new DrawingStroke
                {
                    Tool = DrawingToolKind.Rectangle,
                    ColorHex = "#3B82F6",
                    Thickness = 2.5,
                    Points = { new(48, 48), new(228, 138) }
                },
                new DrawingStroke
                {
                    Tool = DrawingToolKind.TextLabel,
                    ColorHex = "#3B82F6",
                    TextContent = "Markdown + LaTeX",
                    IsLatex = false,
                    Points = { new(72, 82) }
                },
                new DrawingStroke
                {
                    Tool = DrawingToolKind.Arrow,
                    ColorHex = "#10B981",
                    Thickness = 3,
                    Points = { new(238, 93), new(338, 93) }
                },
                new DrawingStroke
                {
                    Tool = DrawingToolKind.Ellipse,
                    ColorHex = "#F59E0B",
                    Thickness = 2.5,
                    Points = { new(348, 44), new(548, 144) }
                },
                new DrawingStroke
                {
                    Tool = DrawingToolKind.TextLabel,
                    ColorHex = "#F59E0B",
                    TextContent = "E = mc^2",
                    IsLatex = true,
                    Points = { new(410, 80) }
                }
            }
        };

        private static string SanitizeFileName(string raw, string fallback)
        {
            string trimmed = (raw ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
                trimmed = fallback;

            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char ch in invalid)
                trimmed = trimmed.Replace(ch, '_');

            return trimmed.Trim();
        }

        private static string EnsureUniquePath(string candidatePath)
        {
            if (!File.Exists(candidatePath))
                return candidatePath;

            string dir = Path.GetDirectoryName(candidatePath) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(candidatePath);
            string ext = Path.GetExtension(candidatePath);

            int counter = 2;
            while (true)
            {
                string next = Path.Combine(dir, $"{name} {counter}{ext}");
                if (!File.Exists(next))
                    return next;
                counter++;
            }
        }
    }
}
