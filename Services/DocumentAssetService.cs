using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia.Media.Imaging;
using NoteDraw.Drawing;

namespace NoteDraw.Services
{
    /// <summary>
    /// Manages pasted images (`&lt;exe&gt;/image`) and saved document drawing canvases (`&lt;exe&gt;/canvas`)
    /// relative to the running application executable directory.
    /// </summary>
    public sealed class DocumentAssetService
    {
        public string ImageFolderPath { get; set; } =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "image"));

        public string CanvasFolderPath { get; set; } =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "canvas"));

        public void EnsureFoldersExist()
        {
            Directory.CreateDirectory(ImageFolderPath);
            Directory.CreateDirectory(CanvasFolderPath);
        }

        public (string FullPath, string FileName, string MarkdownLink) SavePastedBitmap(Bitmap bitmap, string prefix = "pasted")
        {
            if (bitmap is null)
                throw new ArgumentNullException(nameof(bitmap));

            EnsureFoldersExist();
            string cleanPrefix = SanitizeFileName(string.IsNullOrWhiteSpace(prefix) ? "pasted" : prefix);
            string fileName = $"{cleanPrefix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid().ToString("N")[..4]}.png";
            string fullPath = Path.Combine(ImageFolderPath, fileName);
            bitmap.Save(fullPath);
            return (fullPath, fileName, $"![{fileName}](image/{fileName})");
        }

        public (string FullPath, string FileName, string MarkdownLink) ImportImageFile(string sourceFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
                throw new FileNotFoundException("Source image file was not found.", sourceFilePath);

            EnsureFoldersExist();
            string fullSource = Path.GetFullPath(sourceFilePath);
            string fullImageDir = Path.GetFullPath(ImageFolderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? sourceDir = Path.GetDirectoryName(fullSource)
                ?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrEmpty(sourceDir) &&
                string.Equals(sourceDir, fullImageDir, StringComparison.OrdinalIgnoreCase))
            {
                string existingName = Path.GetFileName(fullSource);
                return (fullSource, existingName, $"![{existingName}](image/{existingName})");
            }

            string baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(fullSource));
            string ext = Path.GetExtension(fullSource);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".png";

            string fileName = $"{baseName}{ext}";
            string destPath = Path.Combine(ImageFolderPath, fileName);
            int counter = 1;
            while (File.Exists(destPath))
            {
                fileName = $"{baseName}_{counter}{ext}";
                destPath = Path.Combine(ImageFolderPath, fileName);
                counter++;
            }

            File.Copy(fullSource, destPath, overwrite: false);
            return (destPath, fileName, $"![{fileName}](image/{fileName})");
        }

        public static string CleanObsidianImageTarget(string? rawTarget, out string? pipeAnnotation)
        {
            pipeAnnotation = null;
            if (string.IsNullOrWhiteSpace(rawTarget))
                return string.Empty;

            string working = rawTarget.Trim();
            int pipeIdx = working.IndexOf('|');
            if (pipeIdx >= 0)
            {
                pipeAnnotation = working[(pipeIdx + 1)..].Trim();
                if (string.IsNullOrEmpty(pipeAnnotation))
                    pipeAnnotation = null;
                working = working[..pipeIdx].Trim();
            }

            if (working.StartsWith('<') && working.EndsWith('>') && working.Length >= 2)
                working = working[1..^1].Trim();

            working = working.Trim('"').Trim();

            if (!working.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !working.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                int spaceQuote = working.IndexOf(" \"", StringComparison.Ordinal);
                if (spaceQuote > 0 && working.EndsWith('"'))
                    working = working[..spaceQuote].Trim();

                int hashIdx = working.IndexOf('#');
                if (hashIdx > 0)
                    working = working[..hashIdx].Trim();
            }

            if (working.StartsWith('<') && working.EndsWith('>') && working.Length >= 2)
                working = working[1..^1].Trim();

            return working;
        }

        private static string? TrySafeFullPathIfFileExists(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return null;
            try
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch
            {
                // Ignore malformed paths or inaccessible locations
            }
            return null;
        }

        private static string? SafeCombine(string? baseDir, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(baseDir) || string.IsNullOrWhiteSpace(relativePath))
                return null;
            try
            {
                return Path.Combine(baseDir, relativePath);
            }
            catch
            {
                return null;
            }
        }

        private static string? FindFileInVaultByName(string vaultPath, string fileName)
        {
            if (string.IsNullOrWhiteSpace(vaultPath) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(vaultPath))
                return null;

            var queue = new Queue<string>();
            queue.Enqueue(vaultPath);

            while (queue.Count > 0)
            {
                string dir = queue.Dequeue();
                try
                {
                    foreach (string file in Directory.EnumerateFiles(dir))
                    {
                        if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
                            return Path.GetFullPath(file);
                    }
                }
                catch
                {
                    // Skip unreadable directory entries
                }

                try
                {
                    foreach (string sub in Directory.EnumerateDirectories(dir))
                    {
                        string subName = Path.GetFileName(sub);
                        if (subName.StartsWith('.'))
                            continue;
                        queue.Enqueue(sub);
                    }
                }
                catch
                {
                    // Skip inaccessible subdirectories
                }
            }

            return null;
        }

        public string? ResolveImagePath(string urlOrTarget, string? activeDocDir = null, string? vaultPath = null)
        {
            string cleaned = CleanObsidianImageTarget(urlOrTarget, out _);
            if (string.IsNullOrWhiteSpace(cleaned))
                return null;
            if (cleaned.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                cleaned.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return null;

            var variants = new List<string> { cleaned };
            try
            {
                string unescaped = Uri.UnescapeDataString(cleaned);
                if (!string.Equals(unescaped, cleaned, StringComparison.Ordinal))
                    variants.Add(unescaped);
            }
            catch
            {
                // Keep raw cleaned path if URI unescaping fails
            }

            foreach (string rawVariant in variants)
            {
                string osRel = rawVariant
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .TrimStart(Path.DirectorySeparatorChar);

                string fileName = string.Empty;
                try
                {
                    fileName = Path.GetFileName(osRel);
                }
                catch
                {
                    fileName = string.Empty;
                }

                string?[] candidates =
                {
                    SafeCombine(activeDocDir, osRel),
                    SafeCombine(vaultPath, osRel),
                    SafeCombine(ImageFolderPath, osRel),
                    !string.IsNullOrEmpty(fileName) ? SafeCombine(ImageFolderPath, fileName) : null,
                    SafeCombine(AppContext.BaseDirectory, osRel),
                    SafeCombine(Directory.GetCurrentDirectory(), osRel),
                    rawVariant
                };

                foreach (string? candidate in candidates)
                {
                    string? found = TrySafeFullPathIfFileExists(candidate);
                    if (found is not null)
                        return found;
                }

                if (!string.IsNullOrWhiteSpace(vaultPath) && !string.IsNullOrWhiteSpace(fileName))
                {
                    string? vaultMatch = FindFileInVaultByName(vaultPath, fileName);
                    if (vaultMatch is not null)
                        return vaultMatch;
                }
            }

            return null;
        }

        public (string FullPath, string FileName, string EmbedMarkdown) CreateSavedCanvas(
            DrawingDocument? initialDoc = null,
            string? preferredName = null)
        {
            EnsureFoldersExist();
            string baseName = !string.IsNullOrWhiteSpace(preferredName)
                ? SanitizeFileName(Path.GetFileNameWithoutExtension(preferredName))
                : $"canvas_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid().ToString("N")[..4]}";

            string fileName = baseName.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase)
                ? baseName
                : $"{baseName}.ndraw";
            string fullPath = Path.Combine(CanvasFolderPath, fileName);
            int counter = 1;
            while (File.Exists(fullPath))
            {
                fileName = $"{baseName}_{counter}.ndraw";
                fullPath = Path.Combine(CanvasFolderPath, fileName);
                counter++;
            }

            string json = (initialDoc ?? new DrawingDocument { Background = CanvasBackgroundKind.Grid }).ToJson();
            File.WriteAllText(fullPath, json);
            return (fullPath, fileName, $"![[canvas/{fileName}]]");
        }

        public string SaveInlineCanvasToCanvasFolder(string? documentName, int openFenceLineZeroBased, DrawingDocument doc)
        {
            EnsureFoldersExist();
            string rawTitle = Path.GetFileNameWithoutExtension(documentName ?? "document");
            string baseName = SanitizeFileName(string.IsNullOrWhiteSpace(rawTitle) ? "document" : rawTitle);
            string fileName = $"{baseName}_canvas_L{Math.Max(1, openFenceLineZeroBased + 1)}.ndraw";
            string fullPath = Path.Combine(CanvasFolderPath, fileName);
            File.WriteAllText(fullPath, (doc ?? new DrawingDocument()).ToJson());
            return fullPath;
        }

        public string ResolveCanvasPath(string targetName, string? vaultPath = null)
        {
            EnsureFoldersExist();
            string cleaned = CleanObsidianImageTarget(targetName, out _);
            string normalized = cleaned.Replace('\\', '/');
            if (normalized.StartsWith("canvas/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized["canvas/".Length..];

            string rawBase = normalized;
            try
            {
                rawBase = Path.GetFileName(normalized);
            }
            catch
            {
                rawBase = normalized;
            }

            string stemRaw = rawBase.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase)
                ? rawBase[..^".ndraw".Length]
                : rawBase;
            string stem = SanitizeFileName(stemRaw);
            if (string.IsNullOrWhiteSpace(stem))
                stem = $"canvas_{DateTime.Now:yyyyMMdd_HHmmss}";
            string fileName = $"{stem}.ndraw";

            string exeCanvasCandidate = Path.GetFullPath(Path.Combine(CanvasFolderPath, fileName));
            if (File.Exists(exeCanvasCandidate))
                return exeCanvasCandidate;

            if (!string.IsNullOrWhiteSpace(vaultPath))
            {
                string? vaultExact = SafeCombine(vaultPath, normalized.Replace('/', Path.DirectorySeparatorChar));
                if (TrySafeFullPathIfFileExists(vaultExact) is string foundExact)
                    return foundExact;

                string? vaultNamed = SafeCombine(vaultPath, fileName);
                if (TrySafeFullPathIfFileExists(vaultNamed) is string foundNamed)
                    return foundNamed;
            }

            return exeCanvasCandidate;
        }

        public DrawingDocument? TryLoadExistingCanvas(string targetName, string? vaultPath = null)
        {
            string cleaned = CleanObsidianImageTarget(targetName, out _);
            if (!cleaned.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase))
                return null;

            string resolvedPath = ResolveCanvasPath(cleaned, vaultPath);
            if (!File.Exists(resolvedPath))
                return null;

            try
            {
                return DrawingDocument.FromJson(File.ReadAllText(resolvedPath));
            }
            catch
            {
                return null;
            }
        }

        public DrawingDocument LoadOrCreateCanvas(string targetName, string? vaultPath = null)
        {
            string resolvedPath = ResolveCanvasPath(targetName, vaultPath);
            if (File.Exists(resolvedPath))
            {
                try
                {
                    return DrawingDocument.FromJson(File.ReadAllText(resolvedPath));
                }
                catch
                {
                    return new DrawingDocument { Background = CanvasBackgroundKind.Grid };
                }
            }

            var blank = new DrawingDocument { Background = CanvasBackgroundKind.Grid };
            string? dir = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(resolvedPath, blank.ToJson());
            return blank;
        }

        public string SaveCanvas(string targetName, string json, string? vaultPath = null)
        {
            string resolvedPath = ResolveCanvasPath(targetName, vaultPath);
            string? dir = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(resolvedPath, json ?? new DrawingDocument().ToJson());
            return resolvedPath;
        }

        private static string SanitizeFileName(string name)
        {
            var sb = new StringBuilder(name.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char ch in name.Trim())
            {
                if (Array.IndexOf(invalid, ch) >= 0 || char.IsWhiteSpace(ch))
                    sb.Append('_');
                else
                    sb.Append(ch);
            }
            string result = sb.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(result) ? "asset" : result;
        }
    }
}
