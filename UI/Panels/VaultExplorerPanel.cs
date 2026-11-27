using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NoteDraw.Services;

namespace NoteDraw.UI.Panels
{
    internal sealed class VaultExplorerPanel : Border, IWorkspaceThemeAware
    {
        private readonly IWorkspaceMediator _mediator;
        private readonly VaultService _vault;
        private readonly DocumentAssetService _assets;

        private readonly TextBox _searchBox = new();
        private readonly StackPanel _listStack = new();
        private readonly HashSet<string> _collapsedFolders = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _visibleEntryOrder = new();
        private readonly List<string> _clipboardPaths = new();
        private readonly Dictionary<string, (Border RowContainer, Button MainButton, VaultEntryKind Kind)> _rowVisuals = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Button> _accentButtons = new();

        private ThemePalette _currentPalette = ThemePalette.CreateClassicDark();
        private bool _isDarkTheme = true;
        private bool _clipboardIsCut;
        private string? _lastClickedPath;
        private string? _suppressNextClickPath;
        private bool _isFocused;

        public VaultExplorerPanel(
            IWorkspaceMediator mediator,
            VaultService vault,
            DocumentAssetService assets)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            BuildLayout();
            AddHandler(
                PointerPressedEvent,
                (_, _) => _isFocused = true,
                Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        public IReadOnlyCollection<string> SelectedPaths => _selectedPaths;
        public IReadOnlyList<string> ClipboardPaths => _clipboardPaths;
        public bool IsSearchInputFocused => _searchBox.IsFocused;
        public bool IsVaultExplorerFocused => _isFocused;
        internal string SearchFilterText => _searchBox.Text ?? string.Empty;

        public void ApplySearchFilter(string text)
        {
            _searchBox.Text = text ?? string.Empty;
        }

        public void ClearFocusFlag() => _isFocused = false;

        public void ResetStateForVaultSwitch()
        {
            _selectedPaths.Clear();
            _clipboardPaths.Clear();
            _clipboardIsCut = false;
            _lastClickedPath = null;
            _collapsedFolders.Clear();
        }

        public void RefreshTree()
        {
            _mediator.NotifyVaultNameChanged(Path.GetFileName(_vault.VaultPath));
            _listStack.Children.Clear();
            _visibleEntryOrder.Clear();
            _rowVisuals.Clear();
            _selectedPaths.RemoveWhere(p => !File.Exists(p) && !Directory.Exists(p));
            _clipboardPaths.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));

            var entries = _vault.GetEntries(_searchBox.Text);
            if (entries.Count == 0)
            {
                _listStack.Children.Add(new TextBlock
                {
                    Text = "No matching files.",
                    FontSize = 12,
                    Opacity = 0.65,
                    Margin = new Thickness(6, 4)
                });
                return;
            }

            bool isSearching = !string.IsNullOrWhiteSpace(_searchBox.Text);
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

                _visibleEntryOrder.Add(entry.FullPath);

                if (entry.Kind == VaultEntryKind.Folder)
                {
                    bool isCollapsed = !isSearching && _collapsedFolders.Contains(entry.FullPath);
                    _listStack.Children.Add(BuildFolderRow(entry, isCollapsed, vaultPrefix));
                    if (isCollapsed)
                        collapsedDepth = entry.Depth;
                    continue;
                }

                _listStack.Children.Add(BuildFileRow(entry));
            }

            UpdateSelectionVisuals();
        }

        public void SelectEntry(string fullPath, bool ctrlHeld = false, bool shiftHeld = false)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return;

            string normalized = Path.GetFullPath(fullPath);
            _isFocused = true;

            if (shiftHeld && _visibleEntryOrder.Count > 0)
            {
                ApplyRangeSelection(normalized, ctrlHeld);
            }
            else if (ctrlHeld)
            {
                if (!_selectedPaths.Remove(normalized))
                    _selectedPaths.Add(normalized);
                _lastClickedPath = normalized;
            }
            else
            {
                _selectedPaths.Clear();
                _selectedPaths.Add(normalized);
                _lastClickedPath = normalized;
            }

            UpdateSelectionVisuals();
            UpdateSelectionStatusText();
        }

        public void SelectAllFiles()
        {
            _isFocused = true;
            _selectedPaths.Clear();
            foreach (string path in _visibleEntryOrder)
            {
                if (File.Exists(path))
                {
                    _selectedPaths.Add(path);
                    _lastClickedPath ??= path;
                }
            }
            UpdateSelectionVisuals();
            UpdateSelectionStatusText();
        }

        public void CopySelectedFiles()
        {
            var items = GetEffectiveSelectedPaths();
            if (items.Count == 0)
                return;

            _clipboardPaths.Clear();
            _clipboardPaths.AddRange(items);
            _clipboardIsCut = false;
            UpdateSelectionVisuals();
            _mediator.PostStatusMessage($"📋 Copied {items.Count} item(s)");
        }

        public void CutSelectedFiles()
        {
            var items = GetEffectiveSelectedPaths();
            if (items.Count == 0)
                return;

            _clipboardPaths.Clear();
            _clipboardPaths.AddRange(items);
            _clipboardIsCut = true;
            UpdateSelectionVisuals();
            _mediator.PostStatusMessage($"✂ Cut {items.Count} item(s)");
        }

        public IReadOnlyList<string> PasteFiles(string? targetDirectoryFullPath = null)
        {
            if (_clipboardPaths.Count == 0)
                return Array.Empty<string>();

            string destDir = string.IsNullOrWhiteSpace(targetDirectoryFullPath)
                ? _vault.VaultPath
                : Path.GetFullPath(targetDirectoryFullPath);

            bool isMove = _clipboardIsCut;
            var pasted = _vault.PasteEntries(_clipboardPaths, destDir, isMove, out var movedMappings);

            if (isMove && movedMappings.Count > 0)
            {
                foreach (var (oldPath, newPath) in movedMappings)
                {
                    if (_collapsedFolders.Remove(oldPath))
                        _collapsedFolders.Add(newPath);
                }
                _mediator.RemapOpenTabsAfterMove(movedMappings);
                _clipboardPaths.Clear();
                _clipboardIsCut = false;
            }

            if (pasted.Count > 0)
            {
                _selectedPaths.Clear();
                foreach (string created in pasted)
                    _selectedPaths.Add(created);
                _lastClickedPath = pasted[^1];
            }

            _mediator.RequestRefreshTabStrip();
            RefreshTree();
            _mediator.RequestRefreshStatusBar();
            _mediator.PostStatusMessage(pasted.Count > 0
                ? $"✓ {(isMove ? "Moved" : "Pasted")} {pasted.Count} item(s)"
                : "No valid items pasted");
            return pasted;
        }

        public void DeleteSelectedFiles()
        {
            var targets = GetEffectiveSelectedPaths();
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
                _mediator.CloseOpenTabsMatchingPath(fullPath);
                _collapsedFolders.Remove(fullPath);
                _vault.DeleteEntry(fullPath);
                _clipboardPaths.RemoveAll(c =>
                    string.Equals(c, fullPath, StringComparison.OrdinalIgnoreCase) ||
                    VaultService.IsDescendantOf(c, fullPath));
            }

            if (_clipboardPaths.Count == 0)
                _clipboardIsCut = false;

            _selectedPaths.Clear();
            _lastClickedPath = null;
            RefreshTree();
            _mediator.RequestRefreshStatusBar();
            _mediator.PostStatusMessage($"🗑 Deleted {effective.Count} item(s)");
        }

        public ContextMenu CreateContextMenu(string? entryFullPath, VaultEntryKind? entryKind)
        {
            var menu = new ContextMenu();
            PopulateContextMenu(menu, entryFullPath, entryKind);
            menu.Opening += (_, _) => PopulateContextMenu(menu, entryFullPath, entryKind);
            return menu;
        }

        public void PromptCreateNote()
        {
            _mediator.ShowPrompt("Create New Markdown Note", "New Note", title =>
            {
                var entry = _vault.CreateNote(title);
                RefreshTree();
                _mediator.OpenFile(entry.FullPath);
            });
        }

        public void PromptCreateFolder()
        {
            _mediator.ShowPrompt("Create New Vault Folder", "New Folder", folderName =>
            {
                _vault.CreateFolder(folderName);
                RefreshTree();
            });
        }

        public void PromptCreateDrawing()
        {
            _mediator.ShowPrompt("Create New Drawing (.ndraw)", "New Sketch", title =>
            {
                var entry = _vault.CreateDrawing(title);
                RefreshTree();
                _mediator.OpenFile(entry.FullPath);
            });
        }

        public bool TryHandleKeyDown(KeyEventArgs e, bool isTextInputFocused, bool hasModalOpen)
        {
            if (isTextInputFocused || hasModalOpen || !_isFocused)
                return false;

            if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None && _selectedPaths.Count > 0)
            {
                DeleteSelectedFiles();
                e.Handled = true;
                return true;
            }

            if (!PlatformShortcuts.HasPrimaryModifier(e.KeyModifiers) ||
                e.KeyModifiers.HasFlag(KeyModifiers.Shift) ||
                e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                return false;
            }

            if (e.Key == Key.C && _selectedPaths.Count > 0)
            {
                CopySelectedFiles();
                e.Handled = true;
                return true;
            }
            if (e.Key == Key.X && _selectedPaths.Count > 0)
            {
                CutSelectedFiles();
                e.Handled = true;
                return true;
            }
            if (e.Key == Key.V && _clipboardPaths.Count > 0)
            {
                PasteFiles(GetDefaultPasteTargetDirectory());
                e.Handled = true;
                return true;
            }
            if (e.Key == Key.A)
            {
                SelectAllFiles();
                e.Handled = true;
                return true;
            }
            return false;
        }

        public void ApplyTheme(ThemePalette palette, bool isDark)
        {
            _currentPalette = palette;
            _isDarkTheme = isDark;
            Background = ThemeConfigService.ParseBrush(palette.ChromeBackground, isDark ? "#1E1E1E" : "#F3F3F3");
            BorderBrush = ThemeConfigService.ParseBrush(palette.BorderColor, isDark ? "#3C3C3C" : "#D4D4D4");

            IBrush accent = WorkspaceUiFactory.ResolveAccentBrush(palette, isDark);
            foreach (var btn in _accentButtons)
            {
                btn.Background = accent;
                btn.Foreground = Brushes.White;
            }

            RefreshTree();
        }

        private void BuildLayout()
        {
            _searchBox.PlaceholderText = "Filter files...";
            _searchBox.FontSize = 12;
            _searchBox.Margin = new Thickness(0, 0, 0, 6);
            _searchBox.TextChanged += (_, _) => RefreshTree();

            _listStack.Spacing = 2;
            _listStack.Margin = new Thickness(0, 0, 6, 8);
            _listStack.Background = Brushes.Transparent;

            var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            content.Children.Add(_searchBox);
            var scroller = new ScrollViewer
            {
                AllowAutoHide = false,
                Background = Brushes.Transparent,
                Content = _listStack,
                ContextMenu = CreateContextMenu(entryFullPath: null, entryKind: null)
            };
            Grid.SetRow(scroller, 1);
            content.Children.Add(scroller);

            var bodyGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            var header = WorkspaceUiFactory.CreateViewHeader(
                "VAULT EXPLORER",
                WorkspaceViewKind.VaultExplorer,
                _mediator.CloseView,
                extraAction: ("+ Note", PromptCreateNote),
                secondaryAction: ("+ Folder", PromptCreateFolder),
                accentBrush: _mediator.CurrentAccentBrush,
                accentRegistry: _accentButtons);
            bodyGrid.Children.Add(header);

            var innerWrap = new Border { Padding = new Thickness(8, 6), Child = content };
            Grid.SetRow(innerWrap, 1);
            bodyGrid.Children.Add(innerWrap);

            Focusable = true;
            BorderThickness = new Thickness(0, 0, 1, 1);
            Child = bodyGrid;
        }

        private void ApplyRangeSelection(string normalized, bool ctrlHeld)
        {
            int targetIdx = _visibleEntryOrder.FindIndex(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase));
            int anchorIdx = _lastClickedPath is not null
                ? _visibleEntryOrder.FindIndex(p => string.Equals(p, _lastClickedPath, StringComparison.OrdinalIgnoreCase))
                : 0;
            if (anchorIdx < 0)
                anchorIdx = 0;

            if (targetIdx >= 0)
            {
                if (!ctrlHeld)
                    _selectedPaths.Clear();

                int start = Math.Min(anchorIdx, targetIdx);
                int end = Math.Max(anchorIdx, targetIdx);
                for (int i = start; i <= end; i++)
                    _selectedPaths.Add(_visibleEntryOrder[i]);
            }
            else if (!ctrlHeld)
            {
                _selectedPaths.Clear();
                _selectedPaths.Add(normalized);
                _lastClickedPath = normalized;
            }
            else
            {
                _selectedPaths.Add(normalized);
            }
        }

        private Border BuildFolderRow(VaultEntry entry, bool isCollapsed, string vaultPrefix)
        {
            string chevron = isCollapsed ? "▶" : "▼";
            string folderPath = entry.FullPath;

            var toggleBtn = new Button
            {
                Content = $"{chevron} 📁 {entry.Name}",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(6, 4),
                CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent,
                Foreground = WorkspaceUiFactory.ResolveForegroundBrush(_currentPalette, _isDarkTheme)
            };
            toggleBtn.Click += (_, _) =>
            {
                if (string.Equals(_suppressNextClickPath, folderPath, StringComparison.OrdinalIgnoreCase))
                {
                    _suppressNextClickPath = null;
                    return;
                }
                _isFocused = true;
                _selectedPaths.Clear();
                _selectedPaths.Add(folderPath);
                _lastClickedPath = folderPath;
                if (!_collapsedFolders.Remove(folderPath))
                    _collapsedFolders.Add(folderPath);
                RefreshTree();
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
                delFolderBtn.Click += (_, _) => DeleteSingleEntry(folderPath);
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
            WireRowInteractions(folderBorder, toggleBtn, entry.FullPath, entry.Kind);
            return folderBorder;
        }

        private Border BuildFileRow(VaultEntry entry)
        {
            string filePath = entry.FullPath;
            var openBtn = new Button
            {
                Content = $"{ResolveEntryIcon(entry.Kind)} {entry.Name}",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontSize = 12.5,
                Padding = new Thickness(8, 5),
                CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent,
                Foreground = WorkspaceUiFactory.ResolveForegroundBrush(_currentPalette, _isDarkTheme)
            };
            openBtn.Click += (_, _) =>
            {
                if (string.Equals(_suppressNextClickPath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    _suppressNextClickPath = null;
                    return;
                }
                _isFocused = true;
                _selectedPaths.Clear();
                _selectedPaths.Add(filePath);
                _lastClickedPath = filePath;
                _mediator.OpenFile(filePath);
            };

            if (entry.Kind == VaultEntryKind.ImageFile)
            {
                string imageFullPath = entry.FullPath;
                ToolTip.SetTip(openBtn, $"Click to preview • {PlatformShortcuts.PrimaryModifierLabel}/Shift+Click multi-select • Drag into document");
                openBtn.AddHandler(PointerPressedEvent, (_, pe) =>
                {
                    if (pe.GetCurrentPoint(openBtn).Properties.IsLeftButtonPressed &&
                        !PlatformShortcuts.HasPrimaryModifier(pe.KeyModifiers) &&
                        !pe.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
                        TopLevel.GetTopLevel(this) is { } topLevel)
                    {
                        _mediator.BeginVaultImageDrag(imageFullPath, pe.GetPosition(topLevel));
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
            delBtn.Click += (_, _) => DeleteSingleEntry(filePath);

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
            WireRowInteractions(rowBorder, openBtn, entry.FullPath, entry.Kind);
            return rowBorder;
        }

        private static string ResolveEntryIcon(VaultEntryKind kind) => kind switch
        {
            VaultEntryKind.DrawingFile => "🎨",
            VaultEntryKind.ImageFile => "🖼",
            _ => "📄"
        };

        private void WireRowInteractions(Border rowBorder, Button mainButton, string entryPath, VaultEntryKind entryKind)
        {
            _rowVisuals[entryPath] = (rowBorder, mainButton, entryKind);
            rowBorder.ContextMenu = CreateContextMenu(entryPath, entryKind);
            rowBorder.AddHandler(PointerPressedEvent, (_, pe) =>
            {
                _isFocused = true;
                var props = pe.GetCurrentPoint(rowBorder).Properties;
                if (props.IsRightButtonPressed)
                {
                    if (!_selectedPaths.Contains(entryPath))
                    {
                        _selectedPaths.Clear();
                        _selectedPaths.Add(entryPath);
                        _lastClickedPath = entryPath;
                        UpdateSelectionVisuals();
                    }
                    return;
                }

                if (props.IsLeftButtonPressed &&
                    (PlatformShortcuts.HasPrimaryModifier(pe.KeyModifiers) || pe.KeyModifiers.HasFlag(KeyModifiers.Shift)))
                {
                    _suppressNextClickPath = entryPath;
                    SelectEntry(
                        entryPath,
                        ctrlHeld: PlatformShortcuts.HasPrimaryModifier(pe.KeyModifiers),
                        shiftHeld: pe.KeyModifiers.HasFlag(KeyModifiers.Shift));
                    pe.Handled = true;
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        private void UpdateSelectionVisuals()
        {
            IBrush accent = WorkspaceUiFactory.ResolveAccentBrush(_currentPalette, _isDarkTheme);
            IBrush fg = WorkspaceUiFactory.ResolveForegroundBrush(_currentPalette, _isDarkTheme);
            Color accentColor = accent is ISolidColorBrush solidAccent
                ? solidAccent.Color
                : Color.Parse("#0078D4");
            var multiSelectBg = new SolidColorBrush(Color.FromArgb(_isDarkTheme ? (byte)75 : (byte)45, accentColor.R, accentColor.G, accentColor.B));
            string? activeFilePath = _mediator.ActiveDocumentFilePath;

            foreach (var (path, (rowBorder, mainButton, _)) in _rowVisuals)
            {
                bool isActive = !string.IsNullOrEmpty(activeFilePath) &&
                                string.Equals(activeFilePath, path, StringComparison.OrdinalIgnoreCase);
                bool isSelected = _selectedPaths.Contains(path);
                bool isCut = _clipboardIsCut &&
                             _clipboardPaths.Contains(path, StringComparer.OrdinalIgnoreCase);

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

        private void UpdateSelectionStatusText()
        {
            if (_selectedPaths.Count > 1)
                _mediator.PostStatusMessage($"✓ {_selectedPaths.Count} items selected in Vault Explorer");
            else if (_selectedPaths.Count == 1)
                _mediator.PostStatusMessage($"Selected: {Path.GetFileName(_selectedPaths.First())}");
        }

        private bool IsProtectedExternalRoot(string fullPath)
        {
            string norm = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normImg = Path.GetFullPath(_assets.ImageFolderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normCanvas = Path.GetFullPath(_assets.CanvasFolderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(norm, normImg, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, normCanvas, StringComparison.OrdinalIgnoreCase);
        }

        private List<string> GetEffectiveSelectedPaths()
        {
            var valid = _selectedPaths
                .Where(p => (File.Exists(p) || Directory.Exists(p)) && !IsProtectedExternalRoot(p))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string? activeFile = _mediator.ActiveDocumentFilePath;
            if (valid.Count == 0 && !string.IsNullOrEmpty(activeFile) && File.Exists(activeFile) && _isFocused)
                valid.Add(Path.GetFullPath(activeFile));

            return valid;
        }

        private string GetDefaultPasteTargetDirectory()
        {
            string? reference = _lastClickedPath;
            if (string.IsNullOrWhiteSpace(reference) || !_selectedPaths.Contains(reference))
                reference = _selectedPaths.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(reference))
            {
                if (Directory.Exists(reference))
                {
                    bool selfOrAncestorClipboard = _clipboardPaths.Any(c =>
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
                bool selfOrAncestorClipboard = _clipboardPaths.Any(c =>
                    string.Equals(c, entryFullPath, StringComparison.OrdinalIgnoreCase) ||
                    VaultService.IsDescendantOf(entryFullPath, c));
                return selfOrAncestorClipboard
                    ? (Path.GetDirectoryName(entryFullPath) ?? _vault.VaultPath)
                    : entryFullPath;
            }

            return Path.GetDirectoryName(entryFullPath) ?? _vault.VaultPath;
        }

        private void PopulateContextMenu(ContextMenu menu, string? entryFullPath, VaultEntryKind? entryKind)
        {
            menu.Items.Clear();

            var selected = GetEffectiveSelectedPaths();
            bool hasSelection = selected.Count > 0;
            bool hasClipboard = _clipboardPaths.Any(p => File.Exists(p) || Directory.Exists(p));
            string pasteTarget = ResolveContextPasteTargetDirectory(entryFullPath, entryKind);

            var cutItem = new MenuItem
            {
                Header = selected.Count > 1 ? $"✂ Cut ({selected.Count} items)    {PlatformShortcuts.Format("X")}" : $"✂ Cut    {PlatformShortcuts.Format("X")}",
                IsEnabled = hasSelection
            };
            cutItem.Click += (_, _) => CutSelectedFiles();
            menu.Items.Add(cutItem);

            var copyItem = new MenuItem
            {
                Header = selected.Count > 1 ? $"📋 Copy ({selected.Count} items)    {PlatformShortcuts.Format("C")}" : $"📋 Copy    {PlatformShortcuts.Format("C")}",
                IsEnabled = hasSelection
            };
            copyItem.Click += (_, _) => CopySelectedFiles();
            menu.Items.Add(copyItem);

            int validClipCount = _clipboardPaths.Count(p => File.Exists(p) || Directory.Exists(p));
            var pasteItem = new MenuItem
            {
                Header = validClipCount > 1 ? $"📥 Paste ({validClipCount} items)    {PlatformShortcuts.Format("V")}" : $"📥 Paste    {PlatformShortcuts.Format("V")}",
                IsEnabled = hasClipboard
            };
            pasteItem.Click += (_, _) => PasteFiles(pasteTarget);
            menu.Items.Add(pasteItem);

            menu.Items.Add(new Separator());

            var selectAllItem = new MenuItem { Header = $"☑ Select All Files    {PlatformShortcuts.Format("A")}" };
            selectAllItem.Click += (_, _) => SelectAllFiles();
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
            deleteItem.Click += (_, _) => DeleteSelectedFiles();
            menu.Items.Add(deleteItem);
        }

        private void DeleteSingleEntry(string fullPath)
        {
            _mediator.CloseOpenTabsMatchingPath(fullPath);
            _collapsedFolders.Remove(fullPath);
            _selectedPaths.Remove(fullPath);
            _clipboardPaths.RemoveAll(c =>
                string.Equals(c, fullPath, StringComparison.OrdinalIgnoreCase) ||
                VaultService.IsDescendantOf(c, fullPath));
            _vault.DeleteEntry(fullPath);
            RefreshTree();
        }

        private void PromptCreateNoteInFolder(string subfolderRelative)
        {
            _mediator.ShowPrompt($"Create New Note in '{subfolderRelative}'", "New Note", title =>
            {
                var entry = _vault.CreateNote(title, subfolderRelative: subfolderRelative);
                _collapsedFolders.Remove(Path.GetFullPath(Path.Combine(_vault.VaultPath, subfolderRelative)));
                RefreshTree();
                _mediator.OpenFile(entry.FullPath);
            });
        }
    }
}
