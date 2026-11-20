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
        DrawingFile,
        ImageFile
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

        private readonly List<(string DisplayName, string FullPath)> _externalFolders = new();

        public string VaultPath { get; private set; }

        public VaultService(string? initialVaultPath = null, bool seedSampleIfEmpty = true)
        {
            VaultPath = string.IsNullOrWhiteSpace(initialVaultPath)
                ? GetDefaultVaultPath()
                : Path.GetFullPath(initialVaultPath);

            EnsureVaultInitialized(seedSampleIfEmpty);
        }

        public static string GetDefaultVaultPath()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApp))
                localApp = Path.GetTempPath();
            return Path.Combine(localApp, "NoteDraw", "SampleVault");
        }

        public static bool IsDefaultVaultPath(string? folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return false;

            string normTarget = Path.GetFullPath(folderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normDefault = Path.GetFullPath(GetDefaultVaultPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normTarget, normDefault, StringComparison.OrdinalIgnoreCase);
        }

        public void ClearExternalFolders() => _externalFolders.Clear();

        public void OpenVault(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("Vault path cannot be empty.", nameof(folderPath));

            _externalFolders.Clear();
            VaultPath = Path.GetFullPath(folderPath);
            Directory.CreateDirectory(VaultPath);
        }

        public void EnsureVaultInitialized(bool seedSampleIfEmpty = true)
        {
            Directory.CreateDirectory(VaultPath);
            if (seedSampleIfEmpty && !HasAnyVaultNoteOrDrawing(VaultPath))
                CreateSampleVaultFiles();
        }

        private static bool HasAnyVaultNoteOrDrawing(string rootDir)
        {
            var dirs = new Queue<string>();
            dirs.Enqueue(rootDir);

            while (dirs.Count > 0)
            {
                string current = dirs.Dequeue();
                try
                {
                    foreach (string file in Directory.EnumerateFiles(current))
                    {
                        if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                catch
                {
                    // Skip unreadable directory entries
                }

                try
                {
                    foreach (string sub in Directory.EnumerateDirectories(current))
                    {
                        if (Path.GetFileName(sub).StartsWith('.'))
                            continue;
                        dirs.Enqueue(sub);
                    }
                }
                catch
                {
                    // Skip inaccessible subdirectories
                }
            }

            return false;
        }

        public void RegisterExternalFolder(string displayName, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(fullPath))
                return;

            string normalized = Path.GetFullPath(fullPath);
            _externalFolders.RemoveAll(f =>
                string.Equals(f.DisplayName, displayName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.FullPath, normalized, StringComparison.OrdinalIgnoreCase));
            _externalFolders.Add((displayName.Trim(), normalized));
        }

        public VaultEntry CreateFolder(string folderName, string? parentSubfolderRelative = null)
        {
            string safe = SanitizeFileName(folderName, "New Folder");
            string parentDir = string.IsNullOrWhiteSpace(parentSubfolderRelative)
                ? VaultPath
                : Path.GetFullPath(Path.Combine(VaultPath, parentSubfolderRelative));
            string fullPath = Path.Combine(parentDir, safe);
            Directory.CreateDirectory(fullPath);

            string rel = Path.GetRelativePath(VaultPath, fullPath);
            int depth = string.IsNullOrWhiteSpace(parentSubfolderRelative)
                ? 0
                : parentSubfolderRelative.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return new VaultEntry(Path.GetFileName(fullPath), fullPath, rel, VaultEntryKind.Folder, depth);
        }

        public IReadOnlyList<VaultEntry> GetEntries(string? searchFilter = null)
        {
            var entries = new List<VaultEntry>();
            if (Directory.Exists(VaultPath))
                TraverseDirectory(VaultPath, depth: 0, VaultPath, externalPrefix: null, entries);

            string normalizedVault = Path.GetFullPath(VaultPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var (displayName, extPath) in _externalFolders)
            {
                if (!Directory.Exists(extPath))
                    continue;

                string normalizedExt = Path.GetFullPath(extPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(normalizedExt, normalizedVault, StringComparison.OrdinalIgnoreCase) ||
                    IsDescendantOf(normalizedExt, normalizedVault))
                {
                    continue;
                }

                entries.Add(new VaultEntry(displayName, normalizedExt, displayName, VaultEntryKind.Folder, 0));
                TraverseDirectory(normalizedExt, depth: 1, normalizedExt, externalPrefix: displayName, entries);
            }

            if (string.IsNullOrWhiteSpace(searchFilter))
                return entries;

            string query = searchFilter.Trim();
            var directMatches = entries
                .Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            e.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var matchSet = new HashSet<string>(directMatches.Select(m => m.FullPath), StringComparer.OrdinalIgnoreCase);
            return entries
                .Where(e => matchSet.Contains(e.FullPath) ||
                            (e.Kind == VaultEntryKind.Folder &&
                             directMatches.Any(m => IsDescendantOf(m.FullPath, e.FullPath))))
                .ToList();
        }

        internal static bool IsDescendantOf(string candidatePath, string folderPath)
        {
            string normCandidate = Path.GetFullPath(candidatePath);
            string normFolder = Path.GetFullPath(folderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return normCandidate.StartsWith(normFolder, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSupportedVaultImageExtension(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".svg";
        }

        private static void TraverseDirectory(
            string dir,
            int depth,
            string baseRootDir,
            string? externalPrefix,
            List<VaultEntry> output)
        {
            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.GetDirectories(dir)
                    .Where(d => !Path.GetFileName(d).StartsWith('.'))
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return;
            }

            foreach (string sub in subDirs)
            {
                string rel = Path.GetRelativePath(baseRootDir, sub);
                if (!string.IsNullOrEmpty(externalPrefix))
                    rel = Path.Combine(externalPrefix, rel);
                output.Add(new VaultEntry(Path.GetFileName(sub), sub, rel, VaultEntryKind.Folder, depth));
                TraverseDirectory(sub, depth + 1, baseRootDir, externalPrefix, output);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.GetFiles(dir)
                    .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase) ||
                                IsSupportedVaultImageExtension(f))
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return;
            }

            foreach (string file in files)
            {
                string rel = Path.GetRelativePath(baseRootDir, file);
                if (!string.IsNullOrEmpty(externalPrefix))
                    rel = Path.Combine(externalPrefix, rel);
                var kind = file.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase)
                    ? VaultEntryKind.DrawingFile
                    : IsSupportedVaultImageExtension(file)
                        ? VaultEntryKind.ImageFile
                        : VaultEntryKind.MarkdownNote;
                output.Add(new VaultEntry(Path.GetFileName(file), file, rel, kind, depth));
            }
        }

        public string ReadFile(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                return string.Empty;

            try
            {
                using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch
            {
                return string.Empty;
            }
        }

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

        public IReadOnlyList<string> PasteEntries(
            IEnumerable<string> sourcePaths,
            string? targetDirectoryFullPath,
            bool isMove,
            out IReadOnlyList<(string OldPath, string NewPath)> movedMappings)
        {
            var createdOrMoved = new List<string>();
            var mappings = new List<(string OldPath, string NewPath)>();
            movedMappings = mappings;

            if (sourcePaths is null)
                return createdOrMoved;

            string destDir = string.IsNullOrWhiteSpace(targetDirectoryFullPath)
                ? VaultPath
                : Path.GetFullPath(targetDirectoryFullPath);
            Directory.CreateDirectory(destDir);
            string normalizedDestDir = destDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Deduplicate and drop entries whose ancestor folder is also in the same clipboard batch.
            var normalizedUnique = sourcePaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Where(p => File.Exists(p) || Directory.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var effectiveSources = normalizedUnique
                .Where(p => !normalizedUnique.Any(other =>
                    !string.Equals(other, p, StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(other) &&
                    IsDescendantOf(p, other)))
                .ToList();

            foreach (string src in effectiveSources)
            {
                if (File.Exists(src))
                {
                    string srcParent = (Path.GetDirectoryName(src) ?? string.Empty)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (isMove && string.Equals(srcParent, normalizedDestDir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string baseCandidate = Path.Combine(destDir, Path.GetFileName(src));
                    string destPath = EnsureUniqueCopyPath(baseCandidate, preferCopySuffix: !isMove);

                    if (isMove)
                    {
                        File.Move(src, destPath);
                        mappings.Add((src, destPath));
                    }
                    else
                    {
                        File.Copy(src, destPath, overwrite: false);
                    }

                    createdOrMoved.Add(destPath);
                }
                else if (Directory.Exists(src))
                {
                    if (string.Equals(src, normalizedDestDir, StringComparison.OrdinalIgnoreCase) ||
                        IsDescendantOf(normalizedDestDir, src))
                    {
                        continue;
                    }

                    string srcParent = (Path.GetDirectoryName(src) ?? string.Empty)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (isMove && string.Equals(srcParent, normalizedDestDir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string dirName = Path.GetFileName(src);
                    string baseCandidate = Path.Combine(destDir, dirName);
                    string destPath = EnsureUniqueDirectoryPath(baseCandidate, preferCopySuffix: !isMove);

                    if (isMove)
                    {
                        try
                        {
                            Directory.Move(src, destPath);
                        }
                        catch (IOException)
                        {
                            CopyDirectoryRecursive(src, destPath);
                            Directory.Delete(src, recursive: true);
                        }
                        mappings.Add((src, destPath));
                    }
                    else
                    {
                        CopyDirectoryRecursive(src, destPath);
                    }

                    createdOrMoved.Add(destPath);
                }
            }

            return createdOrMoved;
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, targetFile, overwrite: false);
            }
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string name = Path.GetFileName(subDir);
                if (name.StartsWith('.'))
                    continue;
                CopyDirectoryRecursive(subDir, Path.Combine(destDir, name));
            }
        }

        private static string EnsureUniqueCopyPath(string candidatePath, bool preferCopySuffix)
        {
            if (!File.Exists(candidatePath) && !Directory.Exists(candidatePath))
                return candidatePath;

            string dir = Path.GetDirectoryName(candidatePath) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(candidatePath);
            string ext = Path.GetExtension(candidatePath);

            if (preferCopySuffix)
            {
                string firstCopy = Path.Combine(dir, $"{name} - Copy{ext}");
                if (!File.Exists(firstCopy) && !Directory.Exists(firstCopy))
                    return firstCopy;

                int copyCounter = 2;
                while (true)
                {
                    string next = Path.Combine(dir, $"{name} - Copy {copyCounter}{ext}");
                    if (!File.Exists(next) && !Directory.Exists(next))
                        return next;
                    copyCounter++;
                }
            }

            int counter = 2;
            while (true)
            {
                string next = Path.Combine(dir, $"{name} {counter}{ext}");
                if (!File.Exists(next) && !Directory.Exists(next))
                    return next;
                counter++;
            }
        }

        private static string EnsureUniqueDirectoryPath(string candidateDir, bool preferCopySuffix)
        {
            if (!Directory.Exists(candidateDir) && !File.Exists(candidateDir))
                return candidateDir;

            string parent = Path.GetDirectoryName(candidateDir) ?? string.Empty;
            string name = Path.GetFileName(candidateDir);

            if (preferCopySuffix)
            {
                string firstCopy = Path.Combine(parent, $"{name} - Copy");
                if (!Directory.Exists(firstCopy) && !File.Exists(firstCopy))
                    return firstCopy;

                int copyCounter = 2;
                while (true)
                {
                    string next = Path.Combine(parent, $"{name} - Copy {copyCounter}");
                    if (!Directory.Exists(next) && !File.Exists(next))
                        return next;
                    copyCounter++;
                }
            }

            int counter = 2;
            while (true)
            {
                string next = Path.Combine(parent, $"{name} {counter}");
                if (!Directory.Exists(next) && !File.Exists(next))
                    return next;
                counter++;
            }
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

            string quickRefNote =
@"# Quick Reference

#reference #shortcuts

- Press `?` in the top toolbar or `F1` to open the Markdown & LaTeX Syntax Guide.
- Paste images directly with `Ctrl+V` (saved into `image/`) or insert canvases saved into `canvas/`.
";
            WriteFile(Path.Combine(VaultPath, "Notes", "Quick Reference.md"), quickRefNote);
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
