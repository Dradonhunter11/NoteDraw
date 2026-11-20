using System;
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
            string baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(sourceFilePath));
            string ext = Path.GetExtension(sourceFilePath);
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

            File.Copy(sourceFilePath, destPath, overwrite: false);
            return (destPath, fileName, $"![{fileName}](image/{fileName})");
        }

        public string? ResolveImagePath(string urlOrTarget, string? activeDocDir = null, string? vaultPath = null)
        {
            if (string.IsNullOrWhiteSpace(urlOrTarget))
                return null;

            string cleaned = urlOrTarget.Trim().Trim('"');
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory, cleaned),
                Path.Combine(ImageFolderPath, Path.GetFileName(cleaned)),
                Path.Combine(Directory.GetCurrentDirectory(), cleaned),
                !string.IsNullOrWhiteSpace(activeDocDir) ? Path.Combine(activeDocDir, cleaned) : string.Empty,
                !string.IsNullOrWhiteSpace(vaultPath) ? Path.Combine(vaultPath, cleaned) : string.Empty,
                File.Exists(cleaned) ? Path.GetFullPath(cleaned) : string.Empty
            };

            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    return Path.GetFullPath(candidate);
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
            string normalized = (targetName ?? string.Empty).Trim().Replace('\\', '/');
            if (normalized.StartsWith("canvas/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized["canvas/".Length..];

            string fileName = Path.GetFileName(normalized);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"canvas_{DateTime.Now:yyyyMMdd_HHmmss}.ndraw";
            if (!fileName.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase))
                fileName += ".ndraw";

            string exeCanvasCandidate = Path.GetFullPath(Path.Combine(CanvasFolderPath, fileName));
            if (File.Exists(exeCanvasCandidate))
                return exeCanvasCandidate;

            if (!string.IsNullOrWhiteSpace(vaultPath))
            {
                string vaultExact = Path.GetFullPath(Path.Combine(vaultPath, normalized));
                if (File.Exists(vaultExact))
                    return vaultExact;

                string vaultNamed = Path.GetFullPath(Path.Combine(vaultPath, fileName));
                if (File.Exists(vaultNamed))
                    return vaultNamed;
            }

            return exeCanvasCandidate;
        }

        public DrawingDocument LoadOrCreateCanvas(string targetName, string? vaultPath = null)
        {
            string resolvedPath = ResolveCanvasPath(targetName, vaultPath);
            if (File.Exists(resolvedPath))
                return DrawingDocument.FromJson(File.ReadAllText(resolvedPath));

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
