using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using NoteDraw.Drawing;
using NoteDraw.Rendering;
using NoteDraw.Rendering.Latex;
using NoteDraw.Rendering.Markdown;
using NoteDraw.Services;
using NoteDraw.UI;

namespace NoteDraw
{
    public partial class MainWindow
    {
        private void OpenStarterDocument()
        {
            var entries = _vault.GetEntries().Where(e => e.Kind != VaultEntryKind.Folder).ToList();
            var welcome = entries.FirstOrDefault(e => e.Name.Equals("Welcome to NoteDraw.md", StringComparison.OrdinalIgnoreCase))
                          ?? entries.FirstOrDefault(e => e.Kind == VaultEntryKind.MarkdownNote)
                          ?? entries.FirstOrDefault(e => e.Kind == VaultEntryKind.DrawingFile)
                          ?? entries.FirstOrDefault();

            if (welcome is not null)
                OpenFileInTab(welcome.FullPath);
        }

        public string? OpenCanvasInSeparateTab(string? targetName, int? inlineOpenFenceLineZeroBased, DrawingDocument document)
        {
            _assets.SetActiveVaultPath(_vault.VaultPath);
            var doc = document?.Clone() ?? new DrawingDocument();
            string json = doc.ToJson();
            string? sourceMdPath = _activeTab is { IsDrawing: false, IsImage: false } ? _activeTab.FilePath : null;
            if (_activeTab is { IsDrawing: false, IsImage: false })
                SaveTabToDisk(_activeTab);

            if (!string.IsNullOrWhiteSpace(targetName))
            {
                string savedPath = _assets.SaveCanvas(targetName, json, _vault.VaultPath);
                var existing = _openTabs.FirstOrDefault(t => string.Equals(t.FilePath, savedPath, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                    existing.Content = json;

                OpenFileInTab(savedPath);
                if (_activeTab is not null)
                {
                    _activeTab.SourceMarkdownFilePath = sourceMdPath;
                    _activeTab.SourceInlineFenceLineZeroBased = null;
                }
                RefreshVaultTree();
                _mediator.PostStatusMessage($"✓ Opened canvas in separate tab: {Path.GetFileName(savedPath)}");
                return savedPath;
            }

            if (inlineOpenFenceLineZeroBased.HasValue)
            {
                string savedPath = _assets.SaveInlineCanvasToCanvasFolder(_activeTab?.Title, inlineOpenFenceLineZeroBased.Value, doc);
                var existing = _openTabs.FirstOrDefault(t => string.Equals(t.FilePath, savedPath, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                    existing.Content = json;

                OpenFileInTab(savedPath);
                if (_activeTab is not null)
                {
                    _activeTab.SourceMarkdownFilePath = sourceMdPath;
                    _activeTab.SourceInlineFenceLineZeroBased = inlineOpenFenceLineZeroBased.Value;
                    _activeTab.Content = json;
                    _standaloneDrawing.LoadDocument(doc);
                }

                RefreshVaultTree();
                _mediator.PostStatusMessage($"✓ Opened inline canvas in separate tab: {Path.GetFileName(savedPath)}");
                return savedPath;
            }

            return null;
        }

        private void OpenFileInTab(string fullPath)
        {
            var existing = _openTabs.FirstOrDefault(t => string.Equals(t.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                bool isImage = IsSupportedImageExtension(fullPath);
                existing = new OpenDocumentTab
                {
                    FilePath = fullPath,
                    Content = isImage ? string.Empty : _vault.ReadFile(fullPath),
                    IsDirty = false
                };
                existing.TextUndo.Reset(existing.Content, 0);
                _openTabs.Add(existing);
            }

            SetActiveTab(existing);
        }

        private static Control BuildStandaloneImageViewer(string imageFilePath)
        {
            if (imageFilePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                return new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"🖼 SVG Vector Image: {Path.GetFileName(imageFilePath)}",
                            FontSize = 14,
                            FontWeight = FontWeight.SemiBold
                        },
                        new TextBlock
                        {
                            Text = imageFilePath,
                            FontSize = 12,
                            Opacity = 0.75
                        }
                    }
                };
            }

            try
            {
                var bmp = new Bitmap(imageFilePath);
                return new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = new Border
                    {
                        Padding = new Thickness(16),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        Child = new Image
                        {
                            Source = bmp,
                            Width = bmp.PixelSize.Width,
                            Height = bmp.PixelSize.Height,
                            Stretch = Stretch.None,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top
                        }
                    }
                };
            }
            catch
            {
                return new TextBlock
                {
                    Text = $"Unable to open image file: {imageFilePath}",
                    Margin = new Thickness(16)
                };
            }
        }

        private void SetActiveTab(OpenDocumentTab? tab)
        {
            _activeTab = tab;
            RefreshTabStrip();
            RefreshVaultTree();

            if (tab is null)
            {
                _suppressEditorTextChanged = true;
                _foldableEditor.LoadText(string.Empty, 0);
                _suppressEditorTextChanged = false;
                _markdownPreview.RenderMarkdown(string.Empty);
                _previewContentHost.Content = _markdownPreview;
                _mediator.NotifyDocumentContentChanged(string.Empty);
                NotifyLiveLatexCursor();
                RefreshViewLayout();
                return;
            }

            if (tab.IsDrawing)
            {
                _preferDrawingUndo = true;
                _previewHeaderTitle.Text = $"VECTOR DRAWING CANVAS — {tab.Title}";
                _standaloneDrawing.LoadDocument(DrawingDocument.FromJson(tab.Content));
                _mediator.NotifyDocumentContentChanged(string.Empty);
            }
            else if (tab.IsImage)
            {
                _preferDrawingUndo = false;
                _previewHeaderTitle.Text = $"IMAGE PREVIEW — {tab.Title}";
                _mediator.NotifyDocumentContentChanged(string.Empty);
            }
            else
            {
                _preferDrawingUndo = false;
                _markdownPreview.ClearInlineDrawingHistories();
                _previewHeaderTitle.Text = $"UNIFIED EDITOR & PREVIEW — {tab.Title}";
                _suppressEditorTextChanged = true;
                _foldableEditor.LoadText(tab.Content, tab.CaretIndex);
                _suppressEditorTextChanged = false;
                _markdownPreview.RenderMarkdown(tab.Content);
                _mediator.NotifyDocumentContentChanged(tab.Content);
                NotifyLiveLatexCursor();
            }

            RefreshViewLayout();
            UpdateUndoRedoButtons();
            UpdateStatusBar();
        }

        private void RefreshTabStrip()
        {
            _tabsStrip.Children.Clear();
            foreach (var tab in _openTabs)
            {
                bool active = ReferenceEquals(tab, _activeTab);
                string dirtyDot = tab.IsDirty ? " ●" : "";
                string icon = tab.IsDrawing ? "🎨" : tab.IsImage ? "🖼" : "📄";

                var selectBtn = new Button
                {
                    Content = $"{icon} {tab.Title}{dirtyDot}",
                    FontSize = 12,
                    Padding = new Thickness(9, 4),
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Foreground = active ? Brushes.White : ForegroundBrush()
                };
                selectBtn.Click += (_, _) => SetActiveTab(tab);

                var closeBtn = new Button
                {
                    Content = "×",
                    FontSize = 12,
                    Padding = new Thickness(5, 2),
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Foreground = active ? Brushes.White : MutedBrush()
                };
                closeBtn.Click += (_, _) => CloseTab(tab);

                var pill = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background = active
                        ? AccentBrush()
                        : InactiveTabBrush(),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { selectBtn, closeBtn }
                    }
                };
                _tabsStrip.Children.Add(pill);
            }
        }

        private void CloseTab(OpenDocumentTab tab)
        {
            if (_autoSaveEnabled && tab.IsDirty && !tab.IsImage)
                SaveTabToDisk(tab);

            int idx = _openTabs.IndexOf(tab);
            _openTabs.Remove(tab);
            if (ReferenceEquals(_activeTab, tab))
            {
                var next = _openTabs.Count > 0 ? _openTabs[Math.Clamp(idx - 1, 0, _openTabs.Count - 1)] : null;
                SetActiveTab(next);
            }
            else
            {
                RefreshTabStrip();
            }
        }

        private void SaveActiveTab()
        {
            if (_activeTab is null || _activeTab.IsImage) return;
            SaveTabToDisk(_activeTab);

            if (_activeTab.IsDrawing && !string.IsNullOrWhiteSpace(_activeTab.SourceMarkdownFilePath))
            {
                var hostTab = _openTabs.FirstOrDefault(t =>
                    !t.IsDrawing &&
                    !t.IsImage &&
                    string.Equals(t.FilePath, _activeTab.SourceMarkdownFilePath, StringComparison.OrdinalIgnoreCase));
                if (hostTab is not null && hostTab.IsDirty)
                    SaveTabToDisk(hostTab);
            }

            RefreshTabStrip();
            UpdateStatusBar();
            _mediator.RefreshGitStatus();

            string fileName = Path.GetFileName(_activeTab.FilePath);
            bool isThemeFile = string.Equals(fileName, ThemeConfigService.DarkFileName, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(fileName, ThemeConfigService.LightFileName, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(_activeTab.FilePath, _themeConfig.DarkConfigPath, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(_activeTab.FilePath, _themeConfig.LightConfigPath, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(_activeTab.FilePath, _themeConfig.ActiveConfigPath, StringComparison.OrdinalIgnoreCase);
            if (isThemeFile)
            {
                _themeConfig.LoadPaletteFromFile(_activeTab.FilePath);
                ApplyTheme(_themeConfig.IsDark, persistToConfig: false);
            }
        }

        private void OpenDarkThemeConfigInTab()
        {
            if (string.IsNullOrWhiteSpace(_themeConfig.DarkConfigPath) || !File.Exists(_themeConfig.DarkConfigPath))
                _themeConfig.Load(_vault.VaultPath);
            _showRawSourceInUnifiedWindow = true;
            OpenFileInTab(_themeConfig.DarkConfigPath);
        }

        private void OpenLightThemeConfigInTab()
        {
            if (string.IsNullOrWhiteSpace(_themeConfig.LightConfigPath) || !File.Exists(_themeConfig.LightConfigPath))
                _themeConfig.Load(_vault.VaultPath);
            _showRawSourceInUnifiedWindow = true;
            OpenFileInTab(_themeConfig.LightConfigPath);
        }

        private async System.Threading.Tasks.Task LoadCustomThemeFileAsync()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load NoteDraw Theme File (dark.json / light.json / *.json)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON Theme File")
                    {
                        Patterns = new[] { "*.json" }
                    }
                }
            });
            if (files.Count == 0) return;

            string? path = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            _themeConfig.LoadPaletteFromFile(path);
            ApplyTheme(_themeConfig.IsDark, persistToConfig: false);
            UpdateStatusBar();
        }

        private string GetAutoSaveMenuHeader()
            => _autoSaveEnabled ? "[✓] Auto-Save Documents While Editing" : "[ ] Auto-Save Documents While Editing";

        public void SetAutoSaveEnabled(bool enabled)
        {
            _autoSaveEnabled = enabled;
            _themeConfig.Config.AutoSave = enabled;
            _themeConfig.SaveOptions();
            _autoSaveMenuItem.Header = GetAutoSaveMenuHeader();
            if (_autoSaveEnabled)
                SaveAllDirtyTabs();
            UpdateStatusBar();
            RefreshTabStrip();
        }

        private void SaveTabToDisk(OpenDocumentTab tab)
        {
            if (tab.IsImage)
                return;
            _vault.WriteFile(tab.FilePath, tab.Content);
            tab.IsDirty = false;
        }

        public void SaveAllDirtyTabs()
        {
            foreach (var tab in _openTabs)
            {
                if (tab.IsDirty && !tab.IsImage)
                    SaveTabToDisk(tab);
            }
            RefreshTabStrip();
            UpdateStatusBar();
        }

        public void ShowUnsavedExitMessageBox()
        {
            var dirtyNames = _openTabs
                .Where(t => t.IsDirty && !t.IsImage)
                .Select(t => $"• {t.Title}")
                .ToList();

            _mediator.ShowUnsavedExitDialog(dirtyNames);
        }

        public void ApplyUnsavedExitDecision(UnsavedExitDecision decision, bool closeWindow = false)
        {
            switch (decision)
            {
                case UnsavedExitDecision.Save:
                    SaveAllDirtyTabs();
                    _mediator.HideUnsavedExitDialog();
                    _isConfirmedExit = true;
                    if (closeWindow)
                        Close();
                    break;

                case UnsavedExitDecision.Discard:
                    foreach (var tab in _openTabs)
                        tab.IsDirty = false;
                    _mediator.HideUnsavedExitDialog();
                    _isConfirmedExit = true;
                    RefreshTabStrip();
                    UpdateStatusBar();
                    if (closeWindow)
                        Close();
                    break;

                case UnsavedExitDecision.Cancel:
                default:
                    _mediator.HideUnsavedExitDialog();
                    _isConfirmedExit = false;
                    break;
            }
        }

        private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_isConfirmedExit)
                return;

            if (_autoSaveEnabled)
            {
                SaveAllDirtyTabs();
                return;
            }

            if (HasUnsavedDocuments)
            {
                e.Cancel = true;
                ShowUnsavedExitMessageBox();
            }
        }

        private void NotifyLiveLatexCursor()
            => _mediator.NotifyEditorCursorChanged(
                _editorTextBox.Text ?? string.Empty,
                _editorTextBox.CaretIndex,
                _activeTab is { IsDrawing: false });

        private void UpdateStatusBar()
        {
            string saveText;
            string countsText;
            string? caretText = null;

            if (_activeTab is null)
            {
                saveText = "No document open";
                countsText = "";
                caretText = "";
            }
            else
            {
                saveText = _activeTab.IsDirty
                    ? $"● Unsaved changes ({PlatformShortcuts.Format("S")})"
                    : (_autoSaveEnabled ? "✓ Auto-saved" : "✓ Saved");
                if (_activeTab.IsDrawing)
                {
                    int strokeCount = _standaloneDrawing.Canvas.Document.Strokes.Count;
                    countsText = $"Strokes: {strokeCount}";
                    caretText = "Canvas Mode";
                }
                else
                {
                    string text = _activeTab.Content ?? string.Empty;
                    int words = string.IsNullOrWhiteSpace(text)
                        ? 0
                        : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                    countsText = $"{words} words • {text.Length} chars";

                    if (_showRawSourceInUnifiedWindow)
                    {
                        string rawText = _editorTextBox.Text ?? string.Empty;
                        int caret = Math.Clamp(_editorTextBox.CaretIndex, 0, rawText.Length);
                        var (ln, col) = GetLineAndColumn(rawText, caret);
                        _editorCursorLabel.Text = $"Ln {ln}, Col {col}";
                        caretText = $"Ln {ln}, Col {col}";
                    }
                    else if (_markdownPreview.ActiveCursorLineZeroBased < 0)
                    {
                        caretText = "Live Editor";
                    }
                }
            }

            string viewsText = $"Views: {_viewState.VisibleCount}/{_viewState.TotalCount} (View & Mode → View)";
            _mediator.NotifyStatusBarRefresh(new StatusBarSnapshot(saveText, countsText, caretText, viewsText));
        }

        private void JumpEditorToLine(int oneBasedLine)
        {
            if (!_showRawSourceInUnifiedWindow && _activeTab is { IsDrawing: false })
            {
                _viewState.OpenView(WorkspaceViewKind.PreviewPane);
                _markdownPreview.ActivateSegmentAtLine(Math.Max(0, oneBasedLine - 1), caretAtAbsoluteStart: true);
                UpdateStatusBar();
                return;
            }

            SetUnifiedWindowMode(showRawSource: true);
            _foldableEditor.UnfoldAllSections();
            string text = _editorTextBox.Text ?? string.Empty;
            int offset = GetOffsetForLine(text, oneBasedLine);
            _editorTextBox.Focus();
            _editorTextBox.CaretIndex = offset;
            NotifyLiveLatexCursor();
            UpdateStatusBar();
        }

        private void InsertUtilitySnippet(string snippet, int caretOffsetFromInsertEnd = 0)
        {
            if (_activeTab is null || _activeTab.IsDrawing) return;
            if (!_showRawSourceInUnifiedWindow)
            {
                string cur = _activeTab.Content ?? string.Empty;
                string lineEnding = cur.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                string sep = string.IsNullOrWhiteSpace(cur) ? string.Empty : lineEnding;
                string next = $"{cur.TrimEnd()}{sep}{snippet.TrimStart('\r', '\n')}";
                ApplyMarkdownContentUpdate(next, rebuildPreview: true);
                return;
            }

            string current = _editorTextBox.Text ?? string.Empty;
            int start = Math.Clamp(_editorTextBox.SelectionStart, 0, current.Length);
            int end = Math.Clamp(_editorTextBox.SelectionEnd, 0, current.Length);
            if (start > end) (start, end) = (end, start);

            string updated = current[..start] + snippet + current[end..];
            int newCaret = Math.Clamp(start + snippet.Length + caretOffsetFromInsertEnd, 0, updated.Length);

            bool hasFolds = _foldableEditor.FoldManager.HasFoldedSections;
            string fullUpdated = _foldableEditor.FoldManager.GetFullUnfoldedText(updated);
            ApplyMarkdownContentUpdate(fullUpdated, rebuildPreview: true, newCaretIndex: hasFolds ? (int?)null : newCaret);
            _editorTextBox.Focus();
        }

        private void InsertCodeBlockAtCaret()
        {
            if (_activeTab is null || _activeTab.IsDrawing || _activeTab.IsImage) return;
            const string template = "```csharp\n// Write code here\n```";
            if (!_showRawSourceInUnifiedWindow)
            {
                string cur = _activeTab.Content ?? string.Empty;
                string lineEnding = cur.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                string sep = string.IsNullOrWhiteSpace(cur) ? string.Empty : $"{lineEnding}{lineEnding}";
                string block = template.Replace("\n", lineEnding);
                string next = $"{cur.TrimEnd()}{sep}{block}{lineEnding}";
                int insertedLine = (cur.TrimEnd() + sep).Replace("\r\n", "\n").Split('\n').Length - 1;
                ApplyMarkdownContentUpdate(next, rebuildPreview: true);
                _markdownPreview.ActivateSegmentAtLine(Math.Max(0, insertedLine), caretAtAbsoluteEnd: true);
                return;
            }

            InsertUtilitySnippet($"\n{template}\n");
        }

        private void InsertNewDrawingBlockAtCaret()
        {
            if (_activeTab is null || _activeTab.IsDrawing || _activeTab.IsImage) return;
            _assets.SetActiveVaultPath(_vault.VaultPath);
            var blank = new DrawingDocument { Background = CanvasBackgroundKind.Grid };
            var saved = _assets.CreateSavedCanvas(blank);
            string embed = saved.EmbedMarkdown;

            if (!_showRawSourceInUnifiedWindow)
            {
                string cur = _activeTab.Content ?? string.Empty;
                string lineEnding = cur.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                string sep = string.IsNullOrWhiteSpace(cur) ? string.Empty : $"{lineEnding}{lineEnding}";
                string next = $"{cur.TrimEnd()}{sep}{embed}{lineEnding}";
                _markdownPreview.PrepareEmbeddedCanvasForOpen($"canvas/{saved.FileName}");
                ApplyMarkdownContentUpdate(next, rebuildPreview: true);
            }
            else
            {
                InsertUtilitySnippet($"\n{embed}\n");
            }

            // Always persist the host Markdown document to disk when a canvas file is created and linked into it.
            SaveTabToDisk(_activeTab);
            RefreshTabStrip();
            UpdateStatusBar();
            _mediator.PostStatusMessage($"✓ Canvas saved to canvas/{saved.FileName}");
            RefreshVaultTree();
        }

        private DrawingWorkspaceControl? GetActiveCanvasWorkspaceForClipboard()
        {
            if (_activeTab?.IsDrawing == true)
                return _standaloneDrawing;

            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused)
                return focused as DrawingWorkspaceControl ?? focused.FindAncestorOfType<DrawingWorkspaceControl>();

            return null;
        }

        private async System.Threading.Tasks.Task CopyBitmapToClipboardAsync(Bitmap bitmap)
        {
            _inAppCopiedBitmap = bitmap;
            DrawingWorkspaceControl.SharedInAppClipboardBitmap = bitmap;
            if (Clipboard is not null)
            {
                try
                {
                    await Clipboard.SetBitmapAsync(bitmap);
                }
                catch
                {
                    // In-app _inAppCopiedBitmap remains available even if system clipboard is busy
                }
            }
            _mediator.PostStatusMessage("✓ Image copied to clipboard");
        }

        private bool TryHandleImagePasteKey(int? insertAfterLineZeroBased = null)
        {
            if (_activeTab is null || _activeTab.IsDrawing)
                return false;

            if (Clipboard is not null)
            {
                try
                {
                    var formatsTask = Clipboard.GetDataFormatsAsync();
                    if (formatsTask.IsCompletedSuccessfully)
                    {
                        var formats = formatsTask.Result;
                        bool hasBitmap = formats.Contains(DataFormat.Bitmap);
                        bool hasFilesOnly = formats.Contains(DataFormat.File) && !formats.Contains(DataFormat.Text);
                        if (hasBitmap || hasFilesOnly)
                        {
                            _ = PasteImageIntoDocumentAsync(allowFilePickerFallback: false, targetLineZeroBased: insertAfterLineZeroBased);
                            return true;
                        }

                        if (!formats.Contains(DataFormat.Text) && _inAppCopiedBitmap is not null)
                        {
                            _ = PasteImageIntoDocumentAsync(allowFilePickerFallback: false, targetLineZeroBased: insertAfterLineZeroBased);
                            return true;
                        }

                        return false;
                    }
                }
                catch
                {
                    // Fall through to async clipboard inspection if not synchronously ready
                }

                _ = PasteImageIntoDocumentAsync(allowFilePickerFallback: false, targetLineZeroBased: insertAfterLineZeroBased);
                return false;
            }

            if (_inAppCopiedBitmap is not null)
            {
                var saved = _assets.SavePastedBitmap(_inAppCopiedBitmap, "pasted");
                InsertImageMarkdownIntoActiveDocument(saved.MarkdownLink, insertAfterLineZeroBased);
                RefreshVaultTree();
                return true;
            }

            return false;
        }

        private async System.Threading.Tasks.Task PasteImageIntoDocumentAsync(bool allowFilePickerFallback, int? targetLineZeroBased = null)
        {
            if (_activeTab is null)
                return;

            if (GetActiveCanvasWorkspaceForClipboard() is { } activeCanvasWs)
            {
                if (await activeCanvasWs.PasteImageFromClipboardAsync(activeCanvasWs.Canvas.HoverCanvasPosition, allowFilePickerFallback))
                    _mediator.PostStatusMessage("✓ Pasted image into canvas");
                return;
            }

            if (_activeTab.IsDrawing)
                return;

            string? markdownLink = null;
            string? savedFileName = null;

            if (Clipboard is not null)
            {
                try
                {
                    var bmp = await Clipboard.TryGetBitmapAsync();
                    if (bmp is not null)
                    {
                        var saved = _assets.SavePastedBitmap(bmp, "pasted");
                        markdownLink = saved.MarkdownLink;
                        savedFileName = saved.FileName;
                    }
                }
                catch
                {
                    // Continue checking file formats
                }

                if (markdownLink is null)
                {
                    try
                    {
                        var files = await Clipboard.TryGetFilesAsync();
                        if (files is not null)
                        {
                            foreach (var item in files)
                            {
                                string? path = item.TryGetLocalPath();
                                if (!string.IsNullOrWhiteSpace(path) && IsSupportedImageFile(path))
                                {
                                    var imported = _assets.ImportImageFile(path);
                                    markdownLink = imported.MarkdownLink;
                                    savedFileName = imported.FileName;
                                    break;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Continue to in-app / picker fallback
                    }
                }
            }

            if (markdownLink is null && _inAppCopiedBitmap is not null)
            {
                bool clipboardHasText = false;
                if (Clipboard is not null)
                {
                    try
                    {
                        clipboardHasText = !string.IsNullOrEmpty(await Clipboard.TryGetTextAsync());
                    }
                    catch
                    {
                        clipboardHasText = false;
                    }
                }

                if (!clipboardHasText || allowFilePickerFallback)
                {
                    var saved = _assets.SavePastedBitmap(_inAppCopiedBitmap, "copied");
                    markdownLink = saved.MarkdownLink;
                    savedFileName = saved.FileName;
                }
            }

            if (markdownLink is null && allowFilePickerFallback)
            {
                var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Image to Paste into NoteDraw (copies into image folder)",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Images")
                        {
                            Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" }
                        }
                    }
                });

                string? pickedPath = picked.FirstOrDefault()?.TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(pickedPath) && IsSupportedImageFile(pickedPath))
                {
                    var imported = _assets.ImportImageFile(pickedPath);
                    markdownLink = imported.MarkdownLink;
                    savedFileName = imported.FileName;
                }
            }

            if (markdownLink is not null)
            {
                InsertImageMarkdownIntoActiveDocument(markdownLink, targetLineZeroBased);
                RefreshVaultTree();
                if (!string.IsNullOrWhiteSpace(savedFileName))
                    _mediator.PostStatusMessage($"✓ Pasted image saved to image/{savedFileName}");
            }
        }

        private void InsertImageMarkdownIntoActiveDocument(string markdownLink, int? targetLineZeroBased)
        {
            if (_activeTab is null || _activeTab.IsDrawing)
                return;

            if (_showRawSourceInUnifiedWindow)
            {
                InsertUtilitySnippet($"\n{markdownLink}\n");
                return;
            }

            string current = _activeTab.Content ?? string.Empty;
            string lineEnding = current.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string next;
            int insertedImageLine;

            if (targetLineZeroBased.HasValue && !string.IsNullOrWhiteSpace(current))
            {
                var lines = current.Replace("\r\n", "\n").Split('\n').ToList();
                int idx = Math.Clamp(targetLineZeroBased.Value, 0, Math.Max(0, lines.Count - 1));
                if (string.IsNullOrWhiteSpace(lines[idx]))
                {
                    lines[idx] = markdownLink;
                    insertedImageLine = idx;
                }
                else
                {
                    lines.Insert(idx + 1, markdownLink);
                    insertedImageLine = idx + 1;
                }

                if (insertedImageLine == lines.Count - 1)
                    lines.Add(string.Empty);

                next = string.Join(lineEnding, lines);
            }
            else
            {
                string sep = string.IsNullOrWhiteSpace(current) ? string.Empty : $"{lineEnding}{lineEnding}";
                string withImage = $"{current.TrimEnd()}{sep}{markdownLink}";
                insertedImageLine = withImage.Replace("\r\n", "\n").Split('\n').Length - 1;
                next = $"{withImage}{lineEnding}";
            }

            ApplyMarkdownContentUpdate(next, rebuildPreview: true);
            if (!_showRawSourceInUnifiedWindow)
                _markdownPreview.ActivatePictureAdjacentCursor(insertedImageLine, beforePicture: false);
        }

        internal static bool IsSupportedImageExtension(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".svg";
        }

        private static bool IsSupportedImageFile(string path)
            => File.Exists(path) && IsSupportedImageExtension(path);

        private void ApplyMarkdownContentUpdate(string updated, bool rebuildPreview, int? newCaretIndex = null, bool recordUndo = true, bool atomicUndo = true)
        {
            if (_activeTab is null || _activeTab.IsDrawing) return;

            string prevText = _activeTab.Content;
            int prevCaret = _activeTab.CaretIndex;
            int resolvedCaret = newCaretIndex.HasValue
                ? Math.Clamp(newCaretIndex.Value, 0, updated.Length)
                : Math.Clamp(_editorTextBox.CaretIndex, 0, updated.Length);

            if (recordUndo)
                _activeTab.TextUndo.RecordEdit(prevText, prevCaret, updated, resolvedCaret, isAtomic: atomicUndo);

            _activeTab.Content = updated;
            _activeTab.CaretIndex = resolvedCaret;
            _activeTab.IsDirty = true;
            if (_autoSaveEnabled)
                SaveTabToDisk(_activeTab);

            _suppressEditorTextChanged = true;
            _foldableEditor.LoadText(updated, resolvedCaret);
            _suppressEditorTextChanged = false;

            if (rebuildPreview)
                _markdownPreview.RenderMarkdown(updated);

            _mediator.NotifyDocumentContentChanged(updated);
            NotifyLiveLatexCursor();
            UpdateUndoRedoButtons();
            UpdateStatusBar();
            RefreshTabStrip();
        }

        private DrawingCanvasControl? GetTargetDrawingCanvas()
        {
            if (_activeTab?.IsDrawing == true)
                return _standaloneDrawing.Canvas;
            return _markdownPreview.LastActiveInlineCanvas;
        }

        private bool PerformDrawingUndo()
        {
            var canvas = GetTargetDrawingCanvas();
            bool ok = canvas is not null && canvas.Undo();
            UpdateUndoRedoButtons();
            return ok;
        }

        private bool PerformDrawingRedo()
        {
            var canvas = GetTargetDrawingCanvas();
            bool ok = canvas is not null && canvas.Redo();
            UpdateUndoRedoButtons();
            return ok;
        }

        private void PerformUndo()
        {
            if (_activeTab is null) return;

            if (_activeTab.IsDrawing || (_preferDrawingUndo && _markdownPreview.LastActiveInlineCanvas is { CanUndo: true }))
            {
                PerformDrawingUndo();
                return;
            }

            if (_activeTab.TextUndo.Undo(_activeTab.Content, _activeTab.CaretIndex, out var restored))
            {
                ApplyMarkdownContentUpdate(restored.Text, rebuildPreview: true, newCaretIndex: restored.CaretIndex, recordUndo: false);
            }
            else if (_markdownPreview.LastActiveInlineCanvas is { CanUndo: true })
            {
                PerformDrawingUndo();
            }
        }

        private void PerformRedo()
        {
            if (_activeTab is null) return;

            if (_activeTab.IsDrawing || (_preferDrawingUndo && _markdownPreview.LastActiveInlineCanvas is { CanRedo: true }))
            {
                PerformDrawingRedo();
                return;
            }

            if (_activeTab.TextUndo.Redo(_activeTab.Content, _activeTab.CaretIndex, out var restored))
            {
                ApplyMarkdownContentUpdate(restored.Text, rebuildPreview: true, newCaretIndex: restored.CaretIndex, recordUndo: false);
            }
            else if (_markdownPreview.LastActiveInlineCanvas is { CanRedo: true })
            {
                PerformDrawingRedo();
            }
        }

        private void UpdateUndoRedoButtons()
        {
            if (_activeTab is null)
            {
                _topUndoBtn.IsEnabled = false;
                _topRedoBtn.IsEnabled = false;
                _topUndoBtn.Content = "↶ Undo";
                _topRedoBtn.Content = "↷ Redo";
                return;
            }

            if (_activeTab.IsDrawing)
            {
                _topUndoBtn.IsEnabled = _standaloneDrawing.Canvas.CanUndo;
                _topRedoBtn.IsEnabled = _standaloneDrawing.Canvas.CanRedo;
                _topUndoBtn.Content = _standaloneDrawing.Canvas.UndoCount > 0
                    ? $"↶ Undo ({_standaloneDrawing.Canvas.UndoCount})"
                    : "↶ Undo";
                _topRedoBtn.Content = _standaloneDrawing.Canvas.RedoCount > 0
                    ? $"↷ Redo ({_standaloneDrawing.Canvas.RedoCount})"
                    : "↷ Redo";
                return;
            }

            var inlineCanvas = _markdownPreview.LastActiveInlineCanvas;
            if (_preferDrawingUndo && inlineCanvas is not null && (inlineCanvas.CanUndo || inlineCanvas.CanRedo))
            {
                _topUndoBtn.IsEnabled = inlineCanvas.CanUndo;
                _topRedoBtn.IsEnabled = inlineCanvas.CanRedo;
                _topUndoBtn.Content = inlineCanvas.UndoCount > 0
                    ? $"↶ Draw Undo ({inlineCanvas.UndoCount})"
                    : "↶ Draw Undo";
                _topRedoBtn.Content = inlineCanvas.RedoCount > 0
                    ? $"↷ Draw Redo ({inlineCanvas.RedoCount})"
                    : "↷ Draw Redo";
                return;
            }

            _topUndoBtn.IsEnabled = _activeTab.TextUndo.CanUndo;
            _topRedoBtn.IsEnabled = _activeTab.TextUndo.CanRedo;
            _topUndoBtn.Content = _activeTab.TextUndo.UndoCount > 0
                ? $"↶ Undo ({_activeTab.TextUndo.UndoCount})"
                : "↶ Undo";
            _topRedoBtn.Content = _activeTab.TextUndo.RedoCount > 0
                ? $"↷ Redo ({_activeTab.TextUndo.RedoCount})"
                : "↷ Redo";
        }

        private void OpenOrCreateWikiLink(string target)
        {
            var resolved = _vault.ResolveWikiLink(target);
            if (resolved is null)
                resolved = _vault.CreateNote(target);

            RefreshVaultTree();
            OpenFileInTab(resolved.FullPath);
        }

        private void CloseOpenTabsMatchingPath(string fullPath)
        {
            string folderPrefix = Path.GetFullPath(fullPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var matchingTabs = _openTabs
                .Where(t => string.Equals(t.FilePath, fullPath, StringComparison.OrdinalIgnoreCase) ||
                            t.FilePath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var tab in matchingTabs)
            {
                tab.IsDirty = false;
                CloseTab(tab);
            }
        }



        /// <summary>
        /// Replaces the inner body of a fenced Markdown code block starting at <paramref name="openFenceLineZeroBased"/>.
        /// </summary>
        public static string ReplaceFencedBlockBody(string markdown, int openFenceLineZeroBased, string newBody)
        {
            string lineEnding = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = markdown.Replace("\r\n", "\n").Split('\n').ToList();

            if (openFenceLineZeroBased < 0 || openFenceLineZeroBased >= lines.Count)
                return markdown;

            int closeIdx = -1;
            for (int i = openFenceLineZeroBased + 1; i < lines.Count; i++)
            {
                if (lines[i].Trim().StartsWith("```", StringComparison.Ordinal))
                {
                    closeIdx = i;
                    break;
                }
            }

            if (closeIdx < 0)
                return markdown;

            int removeCount = closeIdx - (openFenceLineZeroBased + 1);
            if (removeCount > 0)
                lines.RemoveRange(openFenceLineZeroBased + 1, removeCount);

            var bodyLines = newBody.Replace("\r\n", "\n").Split('\n');
            lines.InsertRange(openFenceLineZeroBased + 1, bodyLines);

            return string.Join(lineEnding, lines);
        }

        /// <summary>
        /// Toggles a Markdown task list checkbox (`[ ]` &lt;-&gt; `[x]`) on the specified 0-based line.
        /// </summary>
        public static string ToggleTaskCheckboxLine(string markdown, int lineZeroBased, bool isChecked)
        {
            string lineEnding = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            if (lineZeroBased < 0 || lineZeroBased >= lines.Length)
                return markdown;

            string line = lines[lineZeroBased];
            if (isChecked)
                lines[lineZeroBased] = line.Replace("[ ]", "[x]", StringComparison.Ordinal);
            else
                lines[lineZeroBased] = line.Replace("[x]", "[ ]", StringComparison.OrdinalIgnoreCase);

            return string.Join(lineEnding, lines);
        }

        private static (int Line, int Column) GetLineAndColumn(string text, int caretIndex)
        {
            int line = 1;
            int col = 1;
            for (int i = 0; i < caretIndex && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    col = 1;
                }
                else if (text[i] != '\r')
                {
                    col++;
                }
            }
            return (line, col);
        }

        private static int GetOffsetForLine(string text, int oneBasedLine)
        {
            if (oneBasedLine <= 1) return 0;
            int line = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    if (line == oneBasedLine)
                        return i + 1;
                }
            }
            return text.Length;
        }
    }
}
