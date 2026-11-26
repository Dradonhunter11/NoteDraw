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

namespace NoteDraw
{
    public partial class MainWindow
    {
        private void WireEvents()
        {
            _viewState.Changed += (_, _) => RefreshViewLayout();

            WireEditorEvents();
            WirePreviewEvents();
            WireDrawingEvents();
            WirePanelFocusAndTunnelEvents();
        }

        private void WireEditorEvents()
        {
            _editorTextBox.GotFocus += (_, _) =>
            {
                _isVaultExplorerFocused = false;
                _preferDrawingUndo = false;
                UpdateUndoRedoButtons();
            };

            _editorTextBox.PointerPressed += (_, _) =>
            {
                _isVaultExplorerFocused = false;
                _preferDrawingUndo = false;
                UpdateUndoRedoButtons();
            };

            _editorTextBox.TextChanged += (_, _) =>
            {
                if (_suppressEditorTextChanged || _activeTab is null || _activeTab.IsDrawing)
                    return;

                _preferDrawingUndo = false;
                string prevText = _activeTab.Content;
                int prevCaret = _activeTab.CaretIndex;
                string newText = _foldableEditor.FullText;
                int newCaret = _editorTextBox.CaretIndex;

                // Folding/unfolding changes visible text without altering the full document
                if (string.Equals(prevText, newText, StringComparison.Ordinal))
                {
                    _activeTab.CaretIndex = newCaret;
                    UpdateLiveLatexFromCursor();
                    UpdateStatusBar();
                    return;
                }

                _activeTab.TextUndo.RecordEdit(prevText, prevCaret, newText, newCaret);
                _activeTab.Content = newText;
                _activeTab.IsDirty = true;
                _activeTab.CaretIndex = newCaret;
                if (_autoSaveEnabled)
                    SaveTabToDisk(_activeTab);

                if (!_suppressPreviewRebuild)
                    _markdownPreview.RenderMarkdown(_activeTab.Content);

                RefreshOutlineAndTags(_activeTab.Content);
                UpdateLiveLatexFromCursor();
                UpdateUndoRedoButtons();
                UpdateStatusBar();
                RefreshTabStrip();
            };

            _editorTextBox.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBox.CaretIndexProperty)
                {
                    if (_activeTab is not null && !_activeTab.IsDrawing)
                        _activeTab.CaretIndex = _editorTextBox.CaretIndex;
                    UpdateLiveLatexFromCursor();
                    UpdateStatusBar();
                }
            };

            _liveLatexScratchpad.TextChanged += (_, _) =>
            {
                if (_syncingScratchpad) return;
                string code = _liveLatexScratchpad.Text ?? string.Empty;
                RenderLiveLatexBox(code, "Scratchpad Live Preview", isDisplayMode: true, isClosed: true);
            };
        }

        private void WirePreviewEvents()
        {
            _markdownPreview.ResolveEmbeddedDrawing = target =>
            {
                string resolvedCanvas = _assets.ResolveCanvasPath(target, _vault.VaultPath);
                var openTab = _openTabs.FirstOrDefault(t =>
                    t.IsDrawing && string.Equals(t.FilePath, resolvedCanvas, StringComparison.OrdinalIgnoreCase));
                if (openTab is not null)
                    return DrawingDocument.FromJson(openTab.Content);
                return _assets.TryLoadExistingCanvas(target, _vault.VaultPath);
            };

            _markdownPreview.ResolveVaultImagePath = target =>
                _assets.ResolveImagePath(
                    target,
                    _activeTab is not null ? Path.GetDirectoryName(_activeTab.FilePath) : null,
                    _vault.VaultPath);

            _markdownPreview.SaveInlineCanvasToFolder = (openFenceLine, doc) =>
                _assets.SaveInlineCanvasToCanvasFolder(_activeTab?.Title, openFenceLine, doc);

            _markdownPreview.CreateSavedDocumentCanvas = doc =>
            {
                _assets.SetActiveVaultPath(_vault.VaultPath);
                var created = _assets.CreateSavedCanvas(doc);
                RefreshVaultTree();
                return created;
            };

            _markdownPreview.TryHandleImagePasteCallback = lineZeroBased =>
                TryHandleImagePasteKey(lineZeroBased);

            _markdownPreview.ImageFileDroppedAtLine += (_, drop) =>
                DropImageFileIntoDocumentAtLine(drop.ImageFilePath, drop.TargetLineZeroBased);

            DragDrop.SetAllowDrop(_foldableEditor, true);
            _foldableEditor.AddHandler(DragDrop.DragOverEvent, (_, de) =>
            {
                if (MarkdownPreviewControl.TryExtractDroppedImageFilePath(de.DataTransfer) is not null ||
                    de.DataTransfer.Contains(DataFormat.File))
                {
                    de.DragEffects = DragDropEffects.Copy;
                    de.Handled = true;
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _foldableEditor.AddHandler(DragDrop.DropEvent, (_, de) =>
            {
                string? path = MarkdownPreviewControl.TryExtractDroppedImageFilePath(de.DataTransfer);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    int estLine = EstimateRawEditorLineAtPoint(de.GetPosition(_foldableEditor));
                    DropImageFileIntoDocumentAtLine(path, estLine);
                    de.DragEffects = DragDropEffects.Copy;
                    de.Handled = true;
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

            _markdownPreview.CopyImageRequested += async (_, bmp) =>
                await CopyBitmapToClipboardAsync(bmp);

            _markdownPreview.InlineDrawingUpdated += (_, args) =>
            {
                if (_activeTab is null || _activeTab.IsDrawing) return;
                _assets.SetActiveVaultPath(_vault.VaultPath);
                string updated = ReplaceFencedBlockBody(_activeTab.Content, args.OpenFenceLineZeroBased, args.UpdatedJson);
                _activeTab.Content = updated;
                _activeTab.IsDirty = true;
                SaveTabToDisk(_activeTab);

                _assets.SaveInlineCanvasToCanvasFolder(
                    _activeTab.Title,
                    args.OpenFenceLineZeroBased,
                    DrawingDocument.FromJson(args.UpdatedJson));

                _suppressEditorTextChanged = true;
                _suppressPreviewRebuild = true;
                try
                {
                    int savedCaret = _editorTextBox.CaretIndex;
                    _foldableEditor.LoadText(updated, savedCaret);
                }
                finally
                {
                    _suppressPreviewRebuild = false;
                    _suppressEditorTextChanged = false;
                }

                _preferDrawingUndo = true;
                UpdateUndoRedoButtons();
                UpdateStatusBar();
                RefreshTabStrip();
                RefreshVaultTree();
            };

            _markdownPreview.DocumentEditedFromPreview += (_, updatedMarkdown) =>
            {
                if (_activeTab is null || _activeTab.IsDrawing) return;
                ApplyMarkdownContentUpdate(updatedMarkdown, rebuildPreview: false, atomicUndo: false);
            };

            _markdownPreview.CursorPositionChanged += (_, pos) =>
            {
                if (!_showRawSourceInUnifiedWindow && _activeTab is { IsDrawing: false })
                    _statusCaretText.Text = $"Ln {pos.LineOneBased}, Col {pos.ColumnOneBased}";
            };

            _markdownPreview.ExternalDrawingUpdated += (_, args) =>
            {
                _assets.SetActiveVaultPath(_vault.VaultPath);
                string savedPath = _assets.SaveCanvas(args.TargetName, args.UpdatedJson, _vault.VaultPath);
                var open = _openTabs.FirstOrDefault(t => string.Equals(t.FilePath, savedPath, StringComparison.OrdinalIgnoreCase));
                if (open is not null)
                {
                    open.Content = args.UpdatedJson;
                    open.IsDirty = false;
                }
                if (_activeTab is { IsDrawing: false, IsImage: false })
                {
                    SaveTabToDisk(_activeTab);
                    RefreshTabStrip();
                    UpdateStatusBar();
                }
                _preferDrawingUndo = true;
                UpdateUndoRedoButtons();
                RefreshVaultTree();
                _statusSaveText.Text = $"✓ Canvas saved: {Path.GetFileName(savedPath)}";
            };

            _markdownPreview.TaskItemToggled += (_, args) =>
            {
                if (_activeTab is null || _activeTab.IsDrawing) return;
                string updated = ToggleTaskCheckboxLine(_activeTab.Content, args.LineZeroBased, args.IsChecked);
                ApplyMarkdownContentUpdate(updated, rebuildPreview: true);
            };

            _markdownPreview.WikiLinkClicked += (_, target) => OpenOrCreateWikiLink(target);
            _markdownPreview.TagClicked += (_, tag) =>
            {
                _viewState.OpenView(WorkspaceViewKind.VaultExplorer);
                _vaultSearchBox.Text = tag.TrimStart('#');
            };
            _markdownPreview.LineFocusRequested += (_, lineOneBased) => JumpEditorToLine(lineOneBased);
            _markdownPreview.InlineCanvasActivated += (_, _) =>
            {
                _preferDrawingUndo = true;
                UpdateUndoRedoButtons();
            };
            _markdownPreview.OpenCanvasInSeparateTabRequested += (_, args) =>
                OpenCanvasInSeparateTab(args.TargetName, args.InlineOpenFenceLineZeroBased, args.Document);
        }

        private void WireDrawingEvents()
        {
            _standaloneDrawing.Canvas.HistoryChanged += (_, _) => UpdateUndoRedoButtons();
            _standaloneDrawing.CanvasBitmapCopied += (_, bmp) =>
            {
                _inAppCopiedBitmap = bmp;
                _statusSaveText.Text = "✓ Canvas copied to clipboard";
            };
            _standaloneDrawing.DocumentChanged += (_, _) =>
            {
                _preferDrawingUndo = true;
                if (_activeTab is not null && _activeTab.IsDrawing)
                {
                    _activeTab.Content = _standaloneDrawing.Canvas.Document.ToJson();
                    _activeTab.IsDirty = true;
                    if (_autoSaveEnabled)
                        SaveTabToDisk(_activeTab);

                    string fileName = Path.GetFileName(_activeTab.FilePath);
                    _markdownPreview.UpdateCachedEmbeddedDrawing(fileName, _standaloneDrawing.Canvas.Document);
                    _markdownPreview.UpdateCachedEmbeddedDrawing($"canvas/{fileName}", _standaloneDrawing.Canvas.Document);

                    if (!string.IsNullOrWhiteSpace(_activeTab.SourceMarkdownFilePath) &&
                        _activeTab.SourceInlineFenceLineZeroBased is int srcLine)
                    {
                        var mdTab = _openTabs.FirstOrDefault(t =>
                            string.Equals(t.FilePath, _activeTab.SourceMarkdownFilePath, StringComparison.OrdinalIgnoreCase));
                        if (mdTab is not null)
                        {
                            mdTab.Content = ReplaceFencedBlockBody(mdTab.Content, srcLine, _activeTab.Content);
                            mdTab.IsDirty = true;
                            if (_autoSaveEnabled)
                                SaveTabToDisk(mdTab);
                        }
                    }

                    UpdateUndoRedoButtons();
                    UpdateStatusBar();
                    RefreshTabStrip();
                }
            };
        }

        private void WirePanelFocusAndTunnelEvents()
        {
            _vaultPanel.AddHandler(PointerPressedEvent, (_, _) => _isVaultExplorerFocused = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _previewPanel.AddHandler(PointerPressedEvent, (_, _) => _isVaultExplorerFocused = false, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _outlinePanel.AddHandler(PointerPressedEvent, (_, _) => _isVaultExplorerFocused = false, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _tabsBarBorder.AddHandler(PointerPressedEvent, (_, _) => _isVaultExplorerFocused = false, Avalonia.Interactivity.RoutingStrategies.Tunnel);

            AddHandler(PointerMovedEvent, OnWindowTunnelPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            AddHandler(PointerReleasedEvent, OnWindowTunnelPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            AddHandler(KeyDownEvent, OnWindowTunnelKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            KeyDown += OnWindowKeyDown;
            Closing += OnMainWindowClosing;
        }

        private void OnWindowTunnelPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_dragCandidateImagePath is null)
                return;

            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _dragCandidateImagePath = null;
                _isDraggingVaultImage = false;
                return;
            }

            var cur = e.GetPosition(this);
            if (!_isDraggingVaultImage &&
                (Math.Abs(cur.X - _dragStartWindowPoint.X) > 6 || Math.Abs(cur.Y - _dragStartWindowPoint.Y) > 6))
            {
                _isDraggingVaultImage = true;
            }

            if (_isDraggingVaultImage)
                _statusSaveText.Text = $"🖼 Dragging {Path.GetFileName(_dragCandidateImagePath)} — release on target document line";
        }

        private void OnWindowTunnelPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_dragCandidateImagePath is null)
                return;

            string draggedPath = _dragCandidateImagePath;
            bool wasDragging = _isDraggingVaultImage;
            _dragCandidateImagePath = null;
            _isDraggingVaultImage = false;

            if (!wasDragging)
                return;

            var ptInPreviewPanel = e.GetPosition(_previewPanel);
            if (new Rect(_previewPanel.Bounds.Size).Contains(ptInPreviewPanel) &&
                _activeTab is { IsDrawing: false, IsImage: false })
            {
                int targetLine = _showRawSourceInUnifiedWindow
                    ? EstimateRawEditorLineAtPoint(e.GetPosition(_foldableEditor))
                    : _markdownPreview.GetDocumentLineAtPoint(e.GetPosition(_markdownPreview));
                DropImageFileIntoDocumentAtLine(draggedPath, targetLine);
                e.Handled = true;
            }
            else
            {
                UpdateStatusBar();
            }
        }

        private int EstimateRawEditorLineAtPoint(Point ptInEditor)
        {
            double lineHeight = Math.Max(16.0, _editorTextBox.FontSize * 1.45);
            int totalLines = (_foldableEditor.FullText ?? string.Empty).Replace("\r\n", "\n").Split('\n').Length;
            int est = (int)Math.Floor(Math.Max(0, ptInEditor.Y - 10.0) / lineHeight);
            return Math.Clamp(est, 0, Math.Max(0, totalLines));
        }

        public bool DropImageFileIntoDocumentAtLine(string sourceImageFilePath, int targetLineZeroBased)
        {
            if (_activeTab is null || _activeTab.IsDrawing || _activeTab.IsImage)
                return false;
            if (string.IsNullOrWhiteSpace(sourceImageFilePath) || !File.Exists(sourceImageFilePath))
                return false;

            var imported = _assets.ImportImageFile(sourceImageFilePath);
            string next = MarkdownPreviewControl.InsertImageMarkdownAtLine(
                _activeTab.Content ?? string.Empty,
                imported.MarkdownLink,
                targetLineZeroBased,
                out int insertedLine);

            ApplyMarkdownContentUpdate(next, rebuildPreview: true);
            if (!_showRawSourceInUnifiedWindow)
                _markdownPreview.ActivatePictureAdjacentCursor(insertedLine, beforePicture: false);

            RefreshVaultTree();
            _statusSaveText.Text = $"✓ Dropped {imported.FileName} at line {insertedLine + 1}";
            return true;
        }

        private void OnWindowTunnelKeyDown(object? sender, KeyEventArgs e)
        {
            bool isTextInputFocused =
                TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox ||
                _vaultSearchBox.IsFocused ||
                _quickSwitcherInput.IsFocused ||
                _promptInput.IsFocused ||
                _liveLatexScratchpad.IsFocused ||
                _gitCommitMessageBox.IsFocused;

            bool hasModalOpen =
                _unsavedExitOverlay.IsVisible ||
                _gitOverlay.IsVisible ||
                _syntaxGuideOverlay.IsVisible ||
                _quickSwitcherOverlay.IsVisible ||
                _promptOverlay.IsVisible;

            if (!isTextInputFocused &&
                !hasModalOpen &&
                _isVaultExplorerFocused &&
                e.Key == Key.Delete &&
                e.KeyModifiers == KeyModifiers.None &&
                _selectedVaultPaths.Count > 0)
            {
                DeleteSelectedVaultFiles();
                e.Handled = true;
                return;
            }

            if (!PlatformShortcuts.HasPrimaryModifier(e.KeyModifiers))
                return;

            // Allow small utility inputs to keep standard single-line editing shortcuts
            if (_vaultSearchBox.IsFocused || _quickSwitcherInput.IsFocused || _promptInput.IsFocused || _liveLatexScratchpad.IsFocused || _gitCommitMessageBox.IsFocused)
                return;

            if (TryHandleVaultExplorerShortcut(e, isTextInputFocused, hasModalOpen))
                return;

            if (TryHandleActiveCanvasClipboardShortcut(e, isTextInputFocused, hasModalOpen))
                return;

            TryHandleGlobalUndoRedoOrPasteShortcut(e);
        }

        private bool TryHandleVaultExplorerShortcut(KeyEventArgs e, bool isTextInputFocused, bool hasModalOpen)
        {
            if (isTextInputFocused ||
                hasModalOpen ||
                !_isVaultExplorerFocused ||
                e.KeyModifiers.HasFlag(KeyModifiers.Shift) ||
                e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                return false;
            }

            if (e.Key == Key.C && _selectedVaultPaths.Count > 0)
            {
                CopySelectedVaultFiles();
                e.Handled = true;
                return true;
            }
            if (e.Key == Key.X && _selectedVaultPaths.Count > 0)
            {
                CutSelectedVaultFiles();
                e.Handled = true;
                return true;
            }
            if (e.Key == Key.V && _clipboardVaultPaths.Count > 0)
            {
                PasteVaultFiles(GetDefaultPasteTargetDirectory());
                e.Handled = true;
                return true;
            }
            if (e.Key == Key.A)
            {
                SelectAllVaultFiles();
                e.Handled = true;
                return true;
            }
            return false;
        }

        private bool TryHandleActiveCanvasClipboardShortcut(KeyEventArgs e, bool isTextInputFocused, bool hasModalOpen)
        {
            if (isTextInputFocused ||
                hasModalOpen ||
                e.KeyModifiers.HasFlag(KeyModifiers.Shift) ||
                e.KeyModifiers.HasFlag(KeyModifiers.Alt) ||
                GetActiveCanvasWorkspaceForClipboard() is not { } activeCanvasWs)
            {
                return false;
            }

            if (e.Key == Key.C)
            {
                _ = activeCanvasWs.CopyCanvasToClipboardAsync();
                _statusSaveText.Text = "✓ Canvas copied to clipboard";
                e.Handled = true;
                return true;
            }
            if (e.Key == Key.V)
            {
                _ = activeCanvasWs.PasteImageFromClipboardAsync(activeCanvasWs.Canvas.HoverCanvasPosition, allowFilePickerFallback: false);
                _statusSaveText.Text = "✓ Pasted image into canvas";
                e.Handled = true;
                return true;
            }
            return false;
        }

        private void TryHandleGlobalUndoRedoOrPasteShortcut(KeyEventArgs e)
        {
            if (e.Key == Key.Z && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                PerformUndo();
                e.Handled = true;
            }
            else if (e.Key == Key.Y || (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
            {
                PerformRedo();
                e.Handled = true;
            }
            else if (e.Key == Key.V && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                int? lineHint = _markdownPreview.ActiveCursorLineZeroBased >= 0
                    ? _markdownPreview.ActiveCursorLineZeroBased
                    : null;
                if (TryHandleImagePasteKey(lineHint))
                    e.Handled = true;
            }
        }

        private void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F1)
            {
                ToggleSyntaxGuidePopup();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && _unsavedExitOverlay.IsVisible)
            {
                ApplyUnsavedExitDecision(UnsavedExitDecision.Cancel, closeWindow: false);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && _gitOverlay.IsVisible)
            {
                _gitOverlay.IsVisible = false;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && _syntaxGuideOverlay.IsVisible)
            {
                _syntaxGuideOverlay.IsVisible = false;
                e.Handled = true;
                return;
            }

            if (PlatformShortcuts.HasPrimaryModifier(e.KeyModifiers))
            {
                if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    SaveAllDirtyTabs();
                    e.Handled = true;
                }
                else if (e.Key == Key.S)
                {
                    SaveActiveTab();
                    e.Handled = true;
                }
                else if (e.Key == Key.G && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    OpenGitOverlay();
                    e.Handled = true;
                }
                else if (e.Key == Key.P)
                {
                    OpenQuickSwitcher();
                    e.Handled = true;
                }
                else if (e.Key == Key.N && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    PromptCreateNote();
                    e.Handled = true;
                }
                else if (e.Key == Key.D && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    PromptCreateDrawing();
                    e.Handled = true;
                }
            }
        }
    }
}
