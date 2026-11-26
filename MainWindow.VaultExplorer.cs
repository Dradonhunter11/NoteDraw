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
        private void RefreshVaultTree()
        {
            _vaultNameLabel.Text = $"Vault: {Path.GetFileName(_vault.VaultPath)}";
            _vaultListStack.Children.Clear();
            _visibleVaultEntryOrder.Clear();
            _vaultRowVisuals.Clear();
            _selectedVaultPaths.RemoveWhere(p => !File.Exists(p) && !Directory.Exists(p));
            _clipboardVaultPaths.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));

            var entries = _vault.GetEntries(_vaultSearchBox.Text);
            if (entries.Count == 0)
            {
                _vaultListStack.Children.Add(new TextBlock
                {
                    Text = "No matching files.",
                    FontSize = 12,
                    Opacity = 0.65,
                    Margin = new Thickness(6, 4)
                });
                return;
            }

            bool isSearching = !string.IsNullOrWhiteSpace(_vaultSearchBox.Text);
            string vaultPrefix = Path.GetFullPath(_vault.VaultPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            int? collapsedDepth = null;

            foreach (var entry in entries)
            {
                if (collapsedDepth.HasValue)
                {
                    if (entry.Depth > collapsedDepth.Value)
                        continue;
                    collapsedDepth = null;
                }

                _visibleVaultEntryOrder.Add(entry.FullPath);

                if (entry.Kind == VaultEntryKind.Folder)
                {
                    bool isCollapsed = !isSearching && _collapsedVaultFolders.Contains(entry.FullPath);
                    _vaultListStack.Children.Add(BuildVaultFolderRow(entry, isCollapsed, vaultPrefix));
                    if (isCollapsed)
                        collapsedDepth = entry.Depth;
                    continue;
                }

                _vaultListStack.Children.Add(BuildVaultFileRow(entry));
            }

            UpdateVaultSelectionVisuals();
        }

        private Border BuildVaultFolderRow(VaultEntry entry, bool isCollapsed, string vaultPrefix)
        {
            string chevron = isCollapsed ? "▶" : "▼";
            string folderPath = entry.FullPath;

            var toggleBtn = new Button
            {
                Content = $"{chevron} 📁 {entry.Name}",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(6, 4),
                CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent,
                Foreground = ForegroundBrush()
            };
            toggleBtn.Click += (_, _) =>
            {
                if (string.Equals(_suppressNextVaultClickPath, folderPath, StringComparison.OrdinalIgnoreCase))
                {
                    _suppressNextVaultClickPath = null;
                    return;
                }
                _isVaultExplorerFocused = true;
                _selectedVaultPaths.Clear();
                _selectedVaultPaths.Add(folderPath);
                _lastClickedVaultPath = folderPath;
                if (!_collapsedVaultFolders.Remove(folderPath))
                    _collapsedVaultFolders.Add(folderPath);
                RefreshVaultTree();
            };

            var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            folderRow.Children.Add(toggleBtn);

            if (entry.FullPath.StartsWith(vaultPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string subfolderRel = entry.RelativePath;
                var addNoteBtn = new Button
                {
                    Content = "+",
                    FontSize = 12,
                    FontWeight = FontWeight.Bold,
                    Padding = new Thickness(6, 2),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0)
                };
                ToolTip.SetTip(addNoteBtn, $"New note in {entry.Name}");
                addNoteBtn.Click += (_, _) => PromptCreateNoteInFolder(subfolderRel);
                Grid.SetColumn(addNoteBtn, 1);
                folderRow.Children.Add(addNoteBtn);

                var delFolderBtn = new Button
                {
                    Content = "🗑",
                    FontSize = 11,
                    Padding = new Thickness(5, 3),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0)
                };
                ToolTip.SetTip(delFolderBtn, $"Delete folder {entry.Name}");
                delFolderBtn.Click += (_, _) => DeleteVaultFile(folderPath);
                Grid.SetColumn(delFolderBtn, 2);
                folderRow.Children.Add(delFolderBtn);
            }

            var folderBorder = new Border
            {
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Margin = new Thickness(entry.Depth * 12, 2, 0, 1),
                Child = folderRow
            };
            WireVaultRowInteractions(folderBorder, toggleBtn, entry.FullPath, entry.Kind);
            return folderBorder;
        }

        private Border BuildVaultFileRow(VaultEntry entry)
        {
            string filePath = entry.FullPath;
            var openBtn = new Button
            {
                Content = $"{ResolveVaultEntryIcon(entry.Kind)} {entry.Name}",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                FontSize = 12.5,
                Padding = new Thickness(8, 5),
                CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent,
                Foreground = ForegroundBrush()
            };
            openBtn.Click += (_, _) =>
            {
                if (string.Equals(_suppressNextVaultClickPath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    _suppressNextVaultClickPath = null;
                    return;
                }
                _isVaultExplorerFocused = true;
                _selectedVaultPaths.Clear();
                _selectedVaultPaths.Add(filePath);
                _lastClickedVaultPath = filePath;
                OpenFileInTab(filePath);
            };

            if (entry.Kind == VaultEntryKind.ImageFile)
            {
                string imageFullPath = entry.FullPath;
                ToolTip.SetTip(openBtn, $"Click to preview • {PlatformShortcuts.PrimaryModifierLabel}/Shift+Click multi-select • Drag into document");
                openBtn.AddHandler(PointerPressedEvent, (_, pe) =>
                {
                    if (pe.GetCurrentPoint(openBtn).Properties.IsLeftButtonPressed &&
                        !PlatformShortcuts.HasPrimaryModifier(pe.KeyModifiers) &&
                        !pe.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        _dragCandidateImagePath = imageFullPath;
                        _dragStartWindowPoint = pe.GetPosition(this);
                        _isDraggingVaultImage = false;
                    }
                }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            }

            var delBtn = new Button
            {
                Content = "🗑",
                FontSize = 11,
                Padding = new Thickness(5, 3),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            ToolTip.SetTip(delBtn, $"Delete {entry.Name}");
            delBtn.Click += (_, _) => DeleteVaultFile(filePath);

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(openBtn);
            Grid.SetColumn(delBtn, 1);
            row.Children.Add(delBtn);

            var rowBorder = new Border
            {
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Margin = new Thickness(entry.Depth * 12, 0, 0, 0),
                Child = row
            };
            WireVaultRowInteractions(rowBorder, openBtn, entry.FullPath, entry.Kind);
            return rowBorder;
        }

        private static string ResolveVaultEntryIcon(VaultEntryKind kind) => kind switch
        {
            VaultEntryKind.DrawingFile => "🎨",
            VaultEntryKind.ImageFile => "🖼",
            _ => "📄"
        };

        private void WireVaultRowInteractions(Border rowBorder, Button mainButton, string entryPath, VaultEntryKind entryKind)
        {
            _vaultRowVisuals[entryPath] = (rowBorder, mainButton, entryKind);
            rowBorder.ContextMenu = CreateVaultContextMenu(entryPath, entryKind);
            rowBorder.AddHandler(PointerPressedEvent, (_, pe) =>
            {
                _isVaultExplorerFocused = true;
                var props = pe.GetCurrentPoint(rowBorder).Properties;
                if (props.IsRightButtonPressed)
                {
                    if (!_selectedVaultPaths.Contains(entryPath))
                    {
                        _selectedVaultPaths.Clear();
                        _selectedVaultPaths.Add(entryPath);
                        _lastClickedVaultPath = entryPath;
                        UpdateVaultSelectionVisuals();
                    }
                    return;
                }

                if (props.IsLeftButtonPressed &&
                    (PlatformShortcuts.HasPrimaryModifier(pe.KeyModifiers) || pe.KeyModifiers.HasFlag(KeyModifiers.Shift)))
                {
                    _suppressNextVaultClickPath = entryPath;
                    SelectVaultEntry(
                        entryPath,
                        ctrlHeld: PlatformShortcuts.HasPrimaryModifier(pe.KeyModifiers),
                        shiftHeld: pe.KeyModifiers.HasFlag(KeyModifiers.Shift));
                    pe.Handled = true;
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        private void UpdateVaultSelectionVisuals()
        {
            IBrush accent = AccentBrush();
            IBrush fg = ForegroundBrush();
            Color accentColor = accent is ISolidColorBrush solidAccent
                ? solidAccent.Color
                : Color.Parse("#0078D4");
            var multiSelectBg = new SolidColorBrush(Color.FromArgb(_isDarkTheme ? (byte)75 : (byte)45, accentColor.R, accentColor.G, accentColor.B));

            foreach (var (path, (rowBorder, mainButton, _)) in _vaultRowVisuals)
            {
                bool isActive = _activeTab is not null &&
                                string.Equals(_activeTab.FilePath, path, StringComparison.OrdinalIgnoreCase);
                bool isSelected = _selectedVaultPaths.Contains(path);
                bool isCut = _clipboardIsCut &&
                             _clipboardVaultPaths.Contains(path, StringComparer.OrdinalIgnoreCase);

                rowBorder.Opacity = isCut ? 0.50 : 1.0;
                if (isActive)
                {
                    rowBorder.Background = accent;
                    rowBorder.BorderBrush = isSelected
                        ? (_isDarkTheme ? Brushes.White : Brushes.Black)
                        : accent;
                    mainButton.Foreground = Brushes.White;
                }
                else if (isSelected)
                {
                    rowBorder.Background = multiSelectBg;
                    rowBorder.BorderBrush = accent;
                    mainButton.Foreground = fg;
                }
                else
                {
                    rowBorder.Background = Brushes.Transparent;
                    rowBorder.BorderBrush = Brushes.Transparent;
                    mainButton.Foreground = fg;
                }
            }
        }

        /// <summary>
        /// Updates Vault Explorer multi-selection using Windows/VS Code click semantics
        /// (<paramref name="ctrlHeld"/> toggles membership, <paramref name="shiftHeld"/> selects a visible range).
        /// </summary>
        public void SelectVaultEntry(string fullPath, bool ctrlHeld = false, bool shiftHeld = false)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return;

            string normalized = Path.GetFullPath(fullPath);
            _isVaultExplorerFocused = true;

            if (shiftHeld && _visibleVaultEntryOrder.Count > 0)
            {
                int targetIdx = _visibleVaultEntryOrder.FindIndex(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase));
                int anchorIdx = _lastClickedVaultPath is not null
                    ? _visibleVaultEntryOrder.FindIndex(p => string.Equals(p, _lastClickedVaultPath, StringComparison.OrdinalIgnoreCase))
                    : 0;
                if (anchorIdx < 0) anchorIdx = 0;

                if (targetIdx >= 0)
                {
                    if (!ctrlHeld)
                        _selectedVaultPaths.Clear();

                    int start = Math.Min(anchorIdx, targetIdx);
                    int end = Math.Max(anchorIdx, targetIdx);
                    for (int i = start; i <= end; i++)
                        _selectedVaultPaths.Add(_visibleVaultEntryOrder[i]);
                }
                else if (!ctrlHeld)
                {
                    _selectedVaultPaths.Clear();
                    _selectedVaultPaths.Add(normalized);
                    _lastClickedVaultPath = normalized;
                }
                else
                {
                    _selectedVaultPaths.Add(normalized);
                }
            }
            else if (ctrlHeld)
            {
                if (!_selectedVaultPaths.Remove(normalized))
                    _selectedVaultPaths.Add(normalized);
                _lastClickedVaultPath = normalized;
            }
            else
            {
                _selectedVaultPaths.Clear();
                _selectedVaultPaths.Add(normalized);
                _lastClickedVaultPath = normalized;
            }

            UpdateVaultSelectionVisuals();
            UpdateVaultSelectionStatusText();
        }

        /// <summary>
        /// Selects all visible non-folder files in the Vault Explorer.
        /// </summary>
        public void SelectAllVaultFiles()
        {
            _isVaultExplorerFocused = true;
            _selectedVaultPaths.Clear();
            foreach (string path in _visibleVaultEntryOrder)
            {
                if (File.Exists(path))
                {
                    _selectedVaultPaths.Add(path);
                    _lastClickedVaultPath ??= path;
                }
            }
            UpdateVaultSelectionVisuals();
            UpdateVaultSelectionStatusText();
        }

        private void UpdateVaultSelectionStatusText()
        {
            if (_selectedVaultPaths.Count > 1)
                _statusSaveText.Text = $"✓ {_selectedVaultPaths.Count} items selected in Vault Explorer";
            else if (_selectedVaultPaths.Count == 1)
                _statusSaveText.Text = $"Selected: {Path.GetFileName(_selectedVaultPaths.First())}";
        }

        private bool IsProtectedExternalRoot(string fullPath)
        {
            string norm = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normImg = Path.GetFullPath(_assets.ImageFolderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normCanvas = Path.GetFullPath(_assets.CanvasFolderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(norm, normImg, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, normCanvas, StringComparison.OrdinalIgnoreCase);
        }

        private List<string> GetEffectiveSelectedVaultPaths()
        {
            var valid = _selectedVaultPaths
                .Where(p => (File.Exists(p) || Directory.Exists(p)) && !IsProtectedExternalRoot(p))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (valid.Count == 0 && _activeTab is not null && File.Exists(_activeTab.FilePath) && _isVaultExplorerFocused)
                valid.Add(Path.GetFullPath(_activeTab.FilePath));

            return valid;
        }

        /// <summary>
        /// Copies the currently selected Vault Explorer files/folders onto the in-app file clipboard.
        /// </summary>
        public void CopySelectedVaultFiles()
        {
            var items = GetEffectiveSelectedVaultPaths();
            if (items.Count == 0)
                return;

            _clipboardVaultPaths.Clear();
            _clipboardVaultPaths.AddRange(items);
            _clipboardIsCut = false;
            UpdateVaultSelectionVisuals();
            _statusSaveText.Text = $"📋 Copied {items.Count} item(s)";
        }

        /// <summary>
        /// Cuts the currently selected Vault Explorer files/folders onto the in-app file clipboard.
        /// </summary>
        public void CutSelectedVaultFiles()
        {
            var items = GetEffectiveSelectedVaultPaths();
            if (items.Count == 0)
                return;

            _clipboardVaultPaths.Clear();
            _clipboardVaultPaths.AddRange(items);
            _clipboardIsCut = true;
            UpdateVaultSelectionVisuals();
            _statusSaveText.Text = $"✂ Cut {items.Count} item(s)";
        }

        /// <summary>
        /// Pastes cut or copied Vault Explorer files/folders into <paramref name="targetDirectoryFullPath"/> (or active vault root).
        /// </summary>
        public IReadOnlyList<string> PasteVaultFiles(string? targetDirectoryFullPath = null)
        {
            if (_clipboardVaultPaths.Count == 0)
                return Array.Empty<string>();

            string destDir = string.IsNullOrWhiteSpace(targetDirectoryFullPath)
                ? _vault.VaultPath
                : Path.GetFullPath(targetDirectoryFullPath);

            bool isMove = _clipboardIsCut;
            var pasted = _vault.PasteEntries(_clipboardVaultPaths, destDir, isMove, out var movedMappings);

            if (isMove && movedMappings.Count > 0)
            {
                RemapOpenTabsAfterMove(movedMappings);
                _clipboardVaultPaths.Clear();
                _clipboardIsCut = false;
            }

            if (pasted.Count > 0)
            {
                _selectedVaultPaths.Clear();
                foreach (string created in pasted)
                    _selectedVaultPaths.Add(created);
                _lastClickedVaultPath = pasted[^1];
            }

            RefreshTabStrip();
            RefreshVaultTree();
            UpdateStatusBar();
            _statusSaveText.Text = pasted.Count > 0
                ? $"✓ {(isMove ? "Moved" : "Pasted")} {pasted.Count} item(s)"
                : "No valid items pasted";
            return pasted;
        }

        private void RemapOpenTabsAfterMove(IReadOnlyList<(string OldPath, string NewPath)> movedMappings)
        {
            foreach (var (oldPath, newPath) in movedMappings)
            {
                string oldPrefix = oldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                   + Path.DirectorySeparatorChar;
                string newBase = newPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                foreach (var tab in _openTabs)
                {
                    if (string.Equals(tab.FilePath, oldPath, StringComparison.OrdinalIgnoreCase))
                    {
                        tab.FilePath = newPath;
                    }
                    else if (tab.FilePath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        tab.FilePath = Path.Combine(newBase, tab.FilePath[oldPrefix.Length..]);
                    }
                }

                if (_collapsedVaultFolders.Remove(oldPath))
                    _collapsedVaultFolders.Add(newPath);
            }

            if (_activeTab is { IsDrawing: false, IsImage: false })
                _previewHeaderTitle.Text = $"UNIFIED EDITOR & PREVIEW — {_activeTab.Title}";
            else if (_activeTab is { IsDrawing: true })
                _previewHeaderTitle.Text = $"VECTOR DRAWING CANVAS — {_activeTab.Title}";
        }

        /// <summary>
        /// Deletes all currently selected files/folders in Vault Explorer and closes any open tabs referencing them.
        /// </summary>
        public void DeleteSelectedVaultFiles()
        {
            var targets = GetEffectiveSelectedVaultPaths();
            if (targets.Count == 0)
                return;

            // Delete ancestor folders before descendants so redundant child deletes don't repeat.
            var effective = targets
                .Where(p => !targets.Any(other =>
                    !string.Equals(other, p, StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(other) &&
                    VaultService.IsDescendantOf(p, other)))
                .ToList();

            foreach (string fullPath in effective)
            {
                CloseOpenTabsMatchingPath(fullPath);
                _collapsedVaultFolders.Remove(fullPath);
                _vault.DeleteEntry(fullPath);
                _clipboardVaultPaths.RemoveAll(c =>
                    string.Equals(c, fullPath, StringComparison.OrdinalIgnoreCase) ||
                    VaultService.IsDescendantOf(c, fullPath));
            }

            if (_clipboardVaultPaths.Count == 0)
                _clipboardIsCut = false;

            _selectedVaultPaths.Clear();
            _lastClickedVaultPath = null;
            RefreshVaultTree();
            UpdateStatusBar();
            _statusSaveText.Text = $"🗑 Deleted {effective.Count} item(s)";
        }

        private string GetDefaultPasteTargetDirectory()
        {
            string? reference = _lastClickedVaultPath;
            if (string.IsNullOrWhiteSpace(reference) || !_selectedVaultPaths.Contains(reference))
                reference = _selectedVaultPaths.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(reference))
            {
                if (Directory.Exists(reference))
                {
                    bool selfOrAncestorClipboard = _clipboardVaultPaths.Any(c =>
                        string.Equals(c, reference, StringComparison.OrdinalIgnoreCase) ||
                        VaultService.IsDescendantOf(reference, c));
                    return selfOrAncestorClipboard
                        ? (Path.GetDirectoryName(reference) ?? _vault.VaultPath)
                        : reference;
                }
                if (File.Exists(reference))
                    return Path.GetDirectoryName(reference) ?? _vault.VaultPath;
            }

            return _vault.VaultPath;
        }

        private string ResolveContextPasteTargetDirectory(string? entryFullPath, VaultEntryKind? entryKind)
        {
            if (string.IsNullOrWhiteSpace(entryFullPath))
                return _vault.VaultPath;

            if (entryKind == VaultEntryKind.Folder)
            {
                bool selfOrAncestorClipboard = _clipboardVaultPaths.Any(c =>
                    string.Equals(c, entryFullPath, StringComparison.OrdinalIgnoreCase) ||
                    VaultService.IsDescendantOf(entryFullPath, c));
                return selfOrAncestorClipboard
                    ? (Path.GetDirectoryName(entryFullPath) ?? _vault.VaultPath)
                    : entryFullPath;
            }

            return Path.GetDirectoryName(entryFullPath) ?? _vault.VaultPath;
        }

        internal ContextMenu CreateVaultContextMenu(string? entryFullPath, VaultEntryKind? entryKind)
        {
            var menu = new ContextMenu();
            PopulateVaultContextMenu(menu, entryFullPath, entryKind);
            menu.Opening += (_, _) => PopulateVaultContextMenu(menu, entryFullPath, entryKind);
            return menu;
        }

        private void PopulateVaultContextMenu(ContextMenu menu, string? entryFullPath, VaultEntryKind? entryKind)
        {
            menu.Items.Clear();

            var selected = GetEffectiveSelectedVaultPaths();
            bool hasSelection = selected.Count > 0;
            bool hasClipboard = _clipboardVaultPaths.Any(p => File.Exists(p) || Directory.Exists(p));
            string pasteTarget = ResolveContextPasteTargetDirectory(entryFullPath, entryKind);

            var cutItem = new MenuItem
            {
                Header = selected.Count > 1 ? $"✂ Cut ({selected.Count} items)    {PlatformShortcuts.Format("X")}" : $"✂ Cut    {PlatformShortcuts.Format("X")}",
                IsEnabled = hasSelection
            };
            cutItem.Click += (_, _) => CutSelectedVaultFiles();
            menu.Items.Add(cutItem);

            var copyItem = new MenuItem
            {
                Header = selected.Count > 1 ? $"📋 Copy ({selected.Count} items)    {PlatformShortcuts.Format("C")}" : $"📋 Copy    {PlatformShortcuts.Format("C")}",
                IsEnabled = hasSelection
            };
            copyItem.Click += (_, _) => CopySelectedVaultFiles();
            menu.Items.Add(copyItem);

            int validClipCount = _clipboardVaultPaths.Count(p => File.Exists(p) || Directory.Exists(p));
            var pasteItem = new MenuItem
            {
                Header = validClipCount > 1 ? $"📥 Paste ({validClipCount} items)    {PlatformShortcuts.Format("V")}" : $"📥 Paste    {PlatformShortcuts.Format("V")}",
                IsEnabled = hasClipboard
            };
            pasteItem.Click += (_, _) => PasteVaultFiles(pasteTarget);
            menu.Items.Add(pasteItem);

            menu.Items.Add(new Separator());

            var selectAllItem = new MenuItem { Header = $"☑ Select All Files    {PlatformShortcuts.Format("A")}" };
            selectAllItem.Click += (_, _) => SelectAllVaultFiles();
            menu.Items.Add(selectAllItem);

            if (entryFullPath is null)
            {
                var newNoteItem = new MenuItem { Header = "+ New Note" };
                newNoteItem.Click += (_, _) => PromptCreateNote();
                menu.Items.Add(newNoteItem);

                var newFolderItem = new MenuItem { Header = "+ New Folder" };
                newFolderItem.Click += (_, _) => PromptCreateFolder();
                menu.Items.Add(newFolderItem);
            }

            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem
            {
                Header = selected.Count > 1 ? $"🗑 Delete ({selected.Count} items)    Del" : "🗑 Delete    Del",
                IsEnabled = hasSelection
            };
            deleteItem.Click += (_, _) => DeleteSelectedVaultFiles();
            menu.Items.Add(deleteItem);
        }


        private void DeleteVaultFile(string fullPath)
        {
            CloseOpenTabsMatchingPath(fullPath);
            _collapsedVaultFolders.Remove(fullPath);
            _selectedVaultPaths.Remove(fullPath);
            _clipboardVaultPaths.RemoveAll(c =>
                string.Equals(c, fullPath, StringComparison.OrdinalIgnoreCase) ||
                VaultService.IsDescendantOf(c, fullPath));
            _vault.DeleteEntry(fullPath);
            RefreshVaultTree();
        }

        private void RegisterAssetFoldersInVault()
        {
            if (Directory.Exists(_assets.CanvasFolderPath) &&
                !VaultService.IsDescendantOf(_assets.CanvasFolderPath, _vault.VaultPath))
            {
                _vault.RegisterExternalFolder("canvas", _assets.CanvasFolderPath);
            }
            if (Directory.Exists(_assets.ImageFolderPath) &&
                !VaultService.IsDescendantOf(_assets.ImageFolderPath, _vault.VaultPath))
            {
                _vault.RegisterExternalFolder("image", _assets.ImageFolderPath);
            }
        }

        public bool OpenVaultAtPath(string folderPath, bool persistAsLastVault = true)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return false;

            string fullTarget = Path.GetFullPath(folderPath);
            if (_autoSaveEnabled)
                SaveAllDirtyTabs();

            // Unload all active tabs, preview histories, selections, and clipboard state
            // before loading the new vault so two vaults can never remain open or mixed.
            _openTabs.Clear();
            _selectedVaultPaths.Clear();
            _clipboardVaultPaths.Clear();
            _clipboardIsCut = false;
            _lastClickedVaultPath = null;
            _collapsedVaultFolders.Clear();
            _markdownPreview.ClearInlineDrawingHistories();
            SetActiveTab(null);

            bool isDefaultSampleVault = VaultService.IsDefaultVaultPath(fullTarget);
            _vault.OpenVault(fullTarget);
            _vault.EnsureVaultInitialized(seedSampleIfEmpty: isDefaultSampleVault);
            _assets.SetActiveVaultPath(_vault.VaultPath);
            if (isDefaultSampleVault)
                _assets.EnsureFoldersExist();
            RegisterAssetFoldersInVault();

            _themeConfig.Load(_vault.VaultPath);
            if (persistAsLastVault)
            {
                _themeConfig.Config.LastVaultPath = _vault.VaultPath;
                _themeConfig.SaveOptions();
            }
            _autoSaveEnabled = _themeConfig.Config.AutoSave;
            _autoSaveMenuItem.Header = GetAutoSaveMenuHeader();
            ApplyTheme(_themeConfig.IsDark, persistToConfig: false);
            RefreshVaultTree();
            RefreshGitStatus();
            OpenStarterDocument();
            return true;
        }

        private async System.Threading.Tasks.Task OpenVaultFolderAsync()
        {
            if (_isOpeningVaultPicker)
                return;

            _isOpeningVaultPicker = true;
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select NoteDraw Vault Folder",
                    AllowMultiple = false
                });
                if (folders.Count == 0) return;

                string? path = folders[0].TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(path)) return;

                OpenVaultAtPath(path, persistAsLastVault: true);
            }
            finally
            {
                _isOpeningVaultPicker = false;
            }
        }
    }
}
