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
    public partial class MainWindow : Window
    {
        private readonly VaultService _vault;
        private readonly ThemeConfigService _themeConfig = new();
        private readonly DocumentAssetService _assets = new();
        private readonly ViewLayoutState _viewState = new();
        private readonly GitService _git = new();
        private Bitmap? _inAppCopiedBitmap;
        private readonly List<OpenDocumentTab> _openTabs = new();
        private readonly List<Button> _accentButtons = new();
        private readonly HashSet<string> _collapsedVaultFolders = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _selectedVaultPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _visibleVaultEntryOrder = new();
        private readonly List<string> _clipboardVaultPaths = new();
        private readonly Dictionary<string, (Border RowContainer, Button MainButton, VaultEntryKind Kind)> _vaultRowVisuals = new(StringComparer.OrdinalIgnoreCase);
        private bool _clipboardIsCut;
        private string? _lastClickedVaultPath;
        private string? _suppressNextVaultClickPath;
        private bool _isVaultExplorerFocused;
        private bool _isOpeningVaultPicker;
        private OpenDocumentTab? _activeTab;
        private RibbonTabKind _activeRibbonTab = RibbonTabKind.DrawingTools;
        private bool _isDarkTheme = true;
        private bool _autoSaveEnabled = true;
        private bool _suppressEditorTextChanged;
        private bool _suppressPreviewRebuild;
        private bool _preferDrawingUndo;
        private bool _showRawSourceInUnifiedWindow;
        private bool _isConfirmedExit;
        private string? _dragCandidateImagePath;
        private Point _dragStartWindowPoint;
        private bool _isDraggingVaultImage;

        // Top bar & ribbon controls
        private readonly Border _topBarBorder = new();
        private readonly Menu _topMenuBar = new();
        private readonly MenuItem _autoSaveMenuItem = new();
        private readonly TextBlock _vaultNameLabel = new();
        private readonly Button _topUndoBtn = new();
        private readonly Button _topRedoBtn = new();
        private readonly Button _themeBtn = new();
        private readonly Border _ribbonBorder = new();
        private readonly Dictionary<RibbonTabKind, Button> _ribbonTabButtons = new();
        private readonly Border _ribbonContentHost = new();
        private readonly Dictionary<WorkspaceViewKind, MenuItem> _viewToggleMenuItems = new();

        // Sidebar views
        private readonly Grid _mainBodyGrid = new();
        private readonly Grid _sidebarGrid = new();
        private readonly Border _vaultPanel = new();
        private readonly TextBox _vaultSearchBox = new();
        private readonly StackPanel _vaultListStack = new();
        private readonly Border _outlinePanel = new();
        private readonly StackPanel _outlineStack = new();
        private readonly WrapPanel _tagsWrap = new();

        // Center tabs & split views
        private readonly Border _tabsBarBorder = new();
        private readonly StackPanel _tabsStrip = new();
        private readonly Grid _workspaceSplitGrid = new();
        private readonly Grid _leftEditorColumnGrid = new();
        private readonly Border _editorPanel = new();
        private readonly FoldableEditorControl _foldableEditor = new();
        private TextBox _editorTextBox => _foldableEditor.InnerTextBox;
        private readonly TextBlock _editorCursorLabel = new();

        // Live LaTeX typing preview view
        private readonly Border _liveLatexPanel = new();
        private readonly TextBlock _liveLatexBadge = new();
        private readonly LatexMathControl _liveLatexControl = new();
        private readonly TextBlock _liveLatexDiag = new();
        private readonly TextBox _liveLatexScratchpad = new();
        private bool _syncingScratchpad;

        // Unified Editor + Preview + Standalone Drawing view
        private readonly Border _previewPanel = new();
        private readonly Grid _previewPanelGrid = new();
        private readonly TextBlock _previewHeaderTitle = new();
        private readonly Button _unifiedModeToggleBtn = new();
        private readonly ContentControl _previewContentHost = new();
        private readonly MarkdownPreviewControl _markdownPreview = new();
        private readonly DrawingWorkspaceControl _standaloneDrawing = new(compactInlineMode: false, isDarkTheme: true);

        // Status bar view
        private readonly Border _statusBarBorder = new();
        private readonly TextBlock _statusSaveText = new();
        private readonly TextBlock _statusGitText = new();
        private readonly TextBlock _statusViewsText = new();
        private readonly TextBlock _statusCaretText = new();
        private readonly TextBlock _statusLatexText = new();
        private readonly TextBlock _statusCountsText = new();

        // Modal / inline overlays (Syntax Guide Sub-Menu, Quick Switcher, Prompt, Exit & Git)
        private readonly SyntaxGuideSubMenuControl _floatingSyntaxGuidePopup = new(maxListHeight: 470);
        private readonly Border _syntaxGuideOverlay = new();
        private readonly Border _quickSwitcherOverlay = new();
        private readonly TextBox _quickSwitcherInput = new();
        private readonly StackPanel _quickSwitcherResults = new();
        private readonly Border _promptOverlay = new();
        private readonly TextBlock _promptTitle = new();
        private readonly TextBox _promptInput = new();
        private Action<string>? _promptCallback;
        private readonly Border _unsavedExitOverlay = new();
        private readonly TextBlock _unsavedExitMessageText = new();
        private readonly Border _gitOverlay = new();
        private readonly TextBlock _gitBranchBadge = new();
        private readonly TextBlock _gitRemoteLabel = new();
        private readonly TextBlock _gitRibbonStatusLabel = new();
        private readonly TextBox _gitCommitMessageBox = new();
        private readonly StackPanel _gitChangesStack = new();
        private readonly StackPanel _gitCommitsStack = new();
        private readonly TextBox _gitOutputBox = new();
        private GitRepositoryStatus _lastGitStatus = new();

        public bool AutoSaveEnabled
        {
            get => _autoSaveEnabled;
            set => SetAutoSaveEnabled(value);
        }

        public bool HasUnsavedDocuments => _openTabs.Any(t => t.IsDirty && !t.IsImage);
        public bool IsUnsavedExitMessageBoxOpen => _unsavedExitOverlay.IsVisible;
        public bool ShouldPromptOnExit() => !_autoSaveEnabled && HasUnsavedDocuments;
        public IReadOnlyCollection<string> SelectedVaultPaths => _selectedVaultPaths;
        public IReadOnlyList<string> VaultClipboardPaths => _clipboardVaultPaths;
        internal IReadOnlyList<string> OpenTabFilePaths => _openTabs.Select(t => t.FilePath).ToList();
        internal OpenDocumentTab? ActiveDocumentTab => _activeTab;
        internal DrawingWorkspaceControl StandaloneDrawingWorkspace => _standaloneDrawing;
        internal MarkdownPreviewControl PreviewControl => _markdownPreview;
        internal void OpenVaultFileInTab(string fullPath) => OpenFileInTab(fullPath);
        internal bool CloseOpenTab(string fullPath)
        {
            var tab = _openTabs.FirstOrDefault(t => string.Equals(t.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (tab is null) return false;
            CloseTab(tab);
            return true;
        }
        internal DocumentAssetService AssetService => _assets;
        internal Menu TopMenuBar => _topMenuBar;
        internal IReadOnlyCollection<string> RibbonTabLabels =>
            _ribbonTabButtons.Values.Select(b => b.Content?.ToString() ?? string.Empty).ToList();
        internal void SelectRibbonTab(RibbonTabKind tab)
        {
            _activeRibbonTab = tab;
            RefreshRibbonContent();
        }
        internal string CurrentVaultPath => _vault.VaultPath;
        public void InsertCanvasInDocument() => InsertNewDrawingBlockAtCaret();

        public MainWindow() : this(vaultPathOverride: null)
        {
        }

        internal MainWindow(string? vaultPathOverride)
        {
            string? savedLastPath = string.IsNullOrWhiteSpace(vaultPathOverride)
                ? ThemeConfigService.GetSavedLastVaultPath()
                : null;
            string initialVaultPath = !string.IsNullOrWhiteSpace(vaultPathOverride)
                ? Path.GetFullPath(vaultPathOverride)
                : !string.IsNullOrWhiteSpace(savedLastPath)
                    ? Path.GetFullPath(savedLastPath)
                    : VaultService.GetDefaultVaultPath();
            bool isDefaultSampleVault = VaultService.IsDefaultVaultPath(initialVaultPath);
            bool seedSampleIfEmpty = isDefaultSampleVault || !string.IsNullOrWhiteSpace(vaultPathOverride);

            _vault = new VaultService(initialVaultPath, seedSampleIfEmpty);
            InitializeComponent();
            _assets.SetActiveVaultPath(_vault.VaultPath);
            if (isDefaultSampleVault)
                _assets.EnsureFoldersExist();
            RegisterAssetFoldersInVault();
            _themeConfig.Load(_vault.VaultPath);
            _isDarkTheme = _themeConfig.IsDark;
            _autoSaveEnabled = _themeConfig.Config.AutoSave;
            if (string.IsNullOrWhiteSpace(vaultPathOverride))
            {
                _themeConfig.Config.LastVaultPath = _vault.VaultPath;
                _themeConfig.SaveOptions();
            }

            BuildWorkspaceLayout();
            WireEvents();
            ApplyTheme(_isDarkTheme, persistToConfig: false);
            RefreshRibbonContent();
            RefreshViewLayout();
            RefreshVaultTree();
            RefreshGitStatus();
            OpenStarterDocument();
        }

        private void BuildWorkspaceLayout()
        {
            var outerGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto")
            };

            // 1. Top Header Bar
            var topHeader = BuildTopBar();
            Grid.SetRow(topHeader, 0);
            outerGrid.Children.Add(topHeader);

            // 2. Top Tabbed Toolbar Ribbon (Edit & Format | View | LaTeX Assistant | Drawing Tools)
            var ribbon = BuildRibbonBar();
            Grid.SetRow(ribbon, 1);
            outerGrid.Children.Add(ribbon);

            // 3. Main Body (Sidebar + Center Workspace)
            _mainBodyGrid.ColumnDefinitions = new ColumnDefinitions("270,*");

            _sidebarGrid.RowDefinitions = new RowDefinitions("*,Auto");
            BuildVaultExplorerView();
            BuildOutlineAndTagsView();
            Grid.SetRow(_vaultPanel, 0);
            Grid.SetRow(_outlinePanel, 1);
            _sidebarGrid.Children.Add(_vaultPanel);
            _sidebarGrid.Children.Add(_outlinePanel);
            Grid.SetColumn(_sidebarGrid, 0);
            _mainBodyGrid.Children.Add(_sidebarGrid);

            // Center Workspace: Tabs bar + Editor/LiveLaTeX + Preview/StandaloneCanvas
            var centerGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*")
            };

            _tabsBarBorder.Padding = new Thickness(8, 4, 8, 2);
            _tabsBarBorder.BorderThickness = new Thickness(0, 0, 0, 1);
            _tabsStrip.Orientation = Orientation.Horizontal;
            _tabsStrip.Spacing = 4;
            _tabsStrip.Margin = new Thickness(0, 0, 4, 6);
            _tabsBarBorder.Child = new ScrollViewer
            {
                AllowAutoHide = false,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = _tabsStrip
            };
            Grid.SetRow(_tabsBarBorder, 0);
            centerGrid.Children.Add(_tabsBarBorder);

            _workspaceSplitGrid.ColumnDefinitions = new ColumnDefinitions("0,*");
            _leftEditorColumnGrid.IsVisible = false;

            BuildLiveLatexPreviewView();
            BuildPreviewView();

            Grid.SetColumn(_leftEditorColumnGrid, 0);
            Grid.SetColumn(_previewPanel, 1);
            _workspaceSplitGrid.Children.Add(_leftEditorColumnGrid);
            _workspaceSplitGrid.Children.Add(_previewPanel);

            Grid.SetRow(_workspaceSplitGrid, 1);
            centerGrid.Children.Add(_workspaceSplitGrid);

            Grid.SetColumn(centerGrid, 1);
            _mainBodyGrid.Children.Add(centerGrid);

            Grid.SetRow(_mainBodyGrid, 2);
            outerGrid.Children.Add(_mainBodyGrid);

            // 4. Bottom Status Bar (Closable)
            BuildStatusBarView();
            Grid.SetRow(_statusBarBorder, 3);
            outerGrid.Children.Add(_statusBarBorder);

            // Overlays
            BuildSyntaxGuideOverlay();
            BuildQuickSwitcherOverlay();
            BuildPromptOverlay();
            BuildUnsavedExitMessageBoxOverlay();
            BuildGitOverlay();

            var rootContainer = new Grid();
            rootContainer.Children.Add(outerGrid);
            rootContainer.Children.Add(_syntaxGuideOverlay);
            rootContainer.Children.Add(_quickSwitcherOverlay);
            rootContainer.Children.Add(_promptOverlay);
            rootContainer.Children.Add(_unsavedExitOverlay);
            rootContainer.Children.Add(_gitOverlay);

            RootHost.Children.Add(rootContainer);
        }

        private Control BuildTopBar()
        {
            static MenuItem MakeMenuItem(string header, Action onClick)
            {
                var item = new MenuItem { Header = header };
                item.Click += (_, _) => onClick();
                return item;
            }

            var fileMenu = new MenuItem { Header = "File" };
            fileMenu.Items.Add(MakeMenuItem("Open Vault Folder...", async () => await OpenVaultFolderAsync()));
            fileMenu.Items.Add(MakeMenuItem($"New Note ({PlatformShortcuts.Format("N")})", PromptCreateNote));
            fileMenu.Items.Add(MakeMenuItem("New Folder...", PromptCreateFolder));
            fileMenu.Items.Add(MakeMenuItem($"New Standalone Drawing ({PlatformShortcuts.Format("Shift+D")})", PromptCreateDrawing));
            fileMenu.Items.Add(new Separator());
            fileMenu.Items.Add(MakeMenuItem($"Save ({PlatformShortcuts.Format("S")})", SaveActiveTab));
            fileMenu.Items.Add(MakeMenuItem($"Save All ({PlatformShortcuts.Format("Shift+S")})", SaveAllDirtyTabs));
            fileMenu.Items.Add(MakeMenuItem($"Quick Switcher ({PlatformShortcuts.Format("P")})", OpenQuickSwitcher));
            fileMenu.Items.Add(new Separator());
            fileMenu.Items.Add(MakeMenuItem("Exit", Close));

            var editInsertMenu = new MenuItem { Header = "Edit & Insert" };
            editInsertMenu.Items.Add(MakeMenuItem($"↶ Undo ({PlatformShortcuts.Format("Z")})", PerformUndo));
            editInsertMenu.Items.Add(MakeMenuItem($"↷ Redo ({PlatformShortcuts.Format("Y")})", PerformRedo));
            editInsertMenu.Items.Add(new Separator());
            editInsertMenu.Items.Add(MakeMenuItem("+ Insert Canvas in Document", InsertNewDrawingBlockAtCaret));
            editInsertMenu.Items.Add(MakeMenuItem("</ > Insert Code Block", InsertCodeBlockAtCaret));
            editInsertMenu.Items.Add(MakeMenuItem($"📋 Paste Image ({PlatformShortcuts.Format("V")})", async () => await PasteImageIntoDocumentAsync(allowFilePickerFallback: true)));

            var viewModeMenu = new MenuItem { Header = "View & Mode" };
            viewModeMenu.Items.Add(MakeMenuItem("✨ Unified Live View", () => SetUnifiedWindowMode(showRawSource: false)));
            viewModeMenu.Items.Add(MakeMenuItem("📄 Raw Source Mode (H1–H5 Fold)", () => SetUnifiedWindowMode(showRawSource: true)));
            viewModeMenu.Items.Add(new Separator());

            _viewToggleMenuItems.Clear();
            var workspaceViewsSubMenu = new MenuItem { Header = "View" };
            foreach (WorkspaceViewKind view in Enum.GetValues<WorkspaceViewKind>())
            {
                var targetView = view;
                var item = new MenuItem();
                item.Click += (_, _) =>
                {
                    bool next = _viewState.ToggleView(targetView);
                    if (next && targetView == WorkspaceViewKind.EditorPane)
                        _showRawSourceInUnifiedWindow = true;
                    else if (next && targetView == WorkspaceViewKind.PreviewPane)
                        _showRawSourceInUnifiedWindow = false;
                    RefreshViewLayout();
                };
                _viewToggleMenuItems[targetView] = item;
                workspaceViewsSubMenu.Items.Add(item);
            }
            UpdateViewToggleLabels();
            viewModeMenu.Items.Add(workspaceViewsSubMenu);
            viewModeMenu.Items.Add(MakeMenuItem("Reset All Views", () => _viewState.ResetAllViews()));

            var optionsMenu = new MenuItem { Header = "Options" };
            _autoSaveMenuItem.Header = GetAutoSaveMenuHeader();
            _autoSaveMenuItem.Click += (_, _) => SetAutoSaveEnabled(!_autoSaveEnabled);
            optionsMenu.Items.Add(_autoSaveMenuItem);

            var gitMenu = new MenuItem { Header = "Git" };
            gitMenu.Items.Add(MakeMenuItem($"🔀 Git Source Control Panel... ({PlatformShortcuts.Format("Shift+G")})", OpenGitOverlay));
            gitMenu.Items.Add(new Separator());
            gitMenu.Items.Add(MakeMenuItem("✓ Commit All Changes...", PromptGitCommitAll));
            gitMenu.Items.Add(MakeMenuItem("⚡ Commit & Push (Sync)", PromptGitCommitAndPush));
            gitMenu.Items.Add(MakeMenuItem("⬇ Git Pull", RunGitPull));
            gitMenu.Items.Add(MakeMenuItem("⬆ Git Push", RunGitPush));
            gitMenu.Items.Add(new Separator());
            gitMenu.Items.Add(MakeMenuItem("🌱 Initialize Git Repo in Vault", RunGitInit));
            gitMenu.Items.Add(MakeMenuItem("🌿 Switch / Create Branch...", PromptGitBranch));
            gitMenu.Items.Add(MakeMenuItem("🔗 Set Remote Origin URL...", PromptGitRemote));

            var themeMenu = new MenuItem { Header = "Theme" };
            themeMenu.Items.Add(MakeMenuItem("Switch Dark / Light Mode", () => ApplyTheme(!_isDarkTheme, persistToConfig: true)));
            themeMenu.Items.Add(new Separator());
            themeMenu.Items.Add(MakeMenuItem("Edit dark.json", OpenDarkThemeConfigInTab));
            themeMenu.Items.Add(MakeMenuItem("Edit light.json", OpenLightThemeConfigInTab));
            themeMenu.Items.Add(MakeMenuItem("Load Custom Theme (.json)...", async () => await LoadCustomThemeFileAsync()));

            _topMenuBar.Items.Clear();
            _topMenuBar.Background = Brushes.Transparent;
            _topMenuBar.FontSize = 12.5;
            _topMenuBar.VerticalAlignment = VerticalAlignment.Center;
            _topMenuBar.Items.Add(fileMenu);
            _topMenuBar.Items.Add(editInsertMenu);
            _topMenuBar.Items.Add(viewModeMenu);
            _topMenuBar.Items.Add(optionsMenu);
            _topMenuBar.Items.Add(gitMenu);
            _topMenuBar.Items.Add(themeMenu);

            var toolbarWrap = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            void AddWrapped(Control ctrl, double rightMargin = 6)
            {
                ctrl.Margin = new Thickness(0, 2, rightMargin, 2);
                ctrl.VerticalAlignment = VerticalAlignment.Center;
                toolbarWrap.Children.Add(ctrl);
            }

            AddWrapped(new TextBlock
            {
                Text = "NoteDraw",
                FontSize = 16,
                FontWeight = FontWeight.Bold
            }, rightMargin: 8);

            AddWrapped(_topMenuBar, rightMargin: 8);

            _vaultNameLabel.FontSize = 12;
            AddWrapped(_vaultNameLabel, rightMargin: 10);

            AddWrapped(CreatePillButton("+ Note", PromptCreateNote, accent: true));
            AddWrapped(CreatePillButton("+ Canvas in Doc", InsertNewDrawingBlockAtCaret, accent: true));
            AddWrapped(CreatePillButton("📋 Paste Image", async () => await PasteImageIntoDocumentAsync(allowFilePickerFallback: true)));
            AddWrapped(CreatePillButton("Save", SaveActiveTab));

            _topUndoBtn.Content = "↶ Undo";
            _topUndoBtn.FontSize = 12;
            _topUndoBtn.FontWeight = FontWeight.SemiBold;
            _topUndoBtn.Padding = new Thickness(10, 5);
            _topUndoBtn.CornerRadius = new CornerRadius(6);
            _topUndoBtn.Click += (_, _) => PerformUndo();
            ToolTip.SetTip(_topUndoBtn, $"Undo ({PlatformShortcuts.Format("Z")})");
            AddWrapped(_topUndoBtn);

            _topRedoBtn.Content = "↷ Redo";
            _topRedoBtn.FontSize = 12;
            _topRedoBtn.FontWeight = FontWeight.SemiBold;
            _topRedoBtn.Padding = new Thickness(10, 5);
            _topRedoBtn.CornerRadius = new CornerRadius(6);
            _topRedoBtn.Click += (_, _) => PerformRedo();
            ToolTip.SetTip(_topRedoBtn, $"Redo ({PlatformShortcuts.Format("Y")} / {PlatformShortcuts.Format("Shift+Z")})");
            AddWrapped(_topRedoBtn);

            _themeBtn.Content = "Light Mode";
            _themeBtn.FontSize = 12;
            _themeBtn.Padding = new Thickness(10, 5);
            _themeBtn.CornerRadius = new CornerRadius(6);
            _themeBtn.Click += (_, _) => ApplyTheme(!_isDarkTheme, persistToConfig: true);
            AddWrapped(_themeBtn);

            var helpBtn = CreatePillButton("?", ToggleSyntaxGuidePopup);
            helpBtn.FontWeight = FontWeight.Bold;
            helpBtn.FontSize = 13;
            helpBtn.MinWidth = 28;
            helpBtn.Padding = new Thickness(9, 4);
            ToolTip.SetTip(helpBtn, "Markdown & LaTeX Syntax Guide (F1)");
            AddWrapped(helpBtn, rightMargin: 0);

            _topBarBorder.Padding = new Thickness(12, 5);
            _topBarBorder.BorderThickness = new Thickness(0, 0, 0, 1);
            _topBarBorder.Child = toolbarWrap;
            return _topBarBorder;
        }

        private Control BuildRibbonBar()
        {
            var tabsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(10, 5, 10, 4)
            };

            AddRibbonTabButton(tabsRow, RibbonTabKind.DrawingTools, "Drawing Tools");
            AddRibbonTabButton(tabsRow, RibbonTabKind.Git, "🔀 Git");

            _ribbonContentHost.Padding = new Thickness(10, 5, 10, 6);
            _ribbonContentHost.BorderThickness = new Thickness(0, 1, 0, 0);

            var stack = new StackPanel
            {
                Children = { tabsRow, _ribbonContentHost }
            };

            _ribbonBorder.BorderThickness = new Thickness(0, 0, 0, 1);
            _ribbonBorder.Child = stack;
            return _ribbonBorder;
        }

        private void AddRibbonTabButton(Panel parent, RibbonTabKind kind, string label)
        {
            var btn = new Button
            {
                Content = label,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(12, 4),
                CornerRadius = new CornerRadius(6)
            };
            btn.Click += (_, _) =>
            {
                _activeRibbonTab = kind;
                RefreshRibbonContent();
            };
            _ribbonTabButtons[kind] = btn;
            parent.Children.Add(btn);
        }

        private void RefreshRibbonContent()
        {
            // Git tab stays accessible even if FormattingToolbar is closed
            bool showSubToolbar = _activeRibbonTab == RibbonTabKind.Git ||
                                  _viewState.IsVisible(WorkspaceViewKind.FormattingToolbar);
            _ribbonContentHost.IsVisible = showSubToolbar;

            foreach (var (kind, btn) in _ribbonTabButtons)
            {
                if (kind == _activeRibbonTab)
                {
                    btn.Background = AccentBrush();
                    btn.Foreground = Brushes.White;
                }
                else
                {
                    btn.ClearValue(BackgroundProperty);
                    btn.ClearValue(Button.ForegroundProperty);
                }
            }

            if (!showSubToolbar)
                return;

            Control content = _activeRibbonTab switch
            {
                RibbonTabKind.DrawingTools => BuildDrawingToolsRibbon(),
                RibbonTabKind.Git => BuildGitRibbon(),
                _ => new Border()
            };

            if (_activeRibbonTab == RibbonTabKind.Git)
            {
                _ribbonContentHost.Child = content;
                return;
            }

            // Include a × close button on the Drawing Tools context sub-toolbar
            var closeToolbarBtn = CreateCloseViewButton(WorkspaceViewKind.FormattingToolbar, "Close sub-toolbar");
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(content);
            Grid.SetColumn(closeToolbarBtn, 1);
            row.Children.Add(closeToolbarBtn);
            _ribbonContentHost.Child = row;
        }

        private Control BuildDrawingToolsRibbon()
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            wrap.Children.Add(CreateToolbarBtn("↶ Undo Drawing", () => PerformDrawingUndo(), accent: true));
            wrap.Children.Add(CreateToolbarBtn("↷ Redo Drawing", () => PerformDrawingRedo(), accent: true));
            wrap.Children.Add(CreateVerticalDivider());
            wrap.Children.Add(CreateToolbarBtn("Insert Inline ```drawing Block", InsertNewDrawingBlockAtCaret, accent: true));
            wrap.Children.Add(CreateToolbarBtn("New Standalone .ndraw File", PromptCreateDrawing, accent: true));
            wrap.Children.Add(CreateToolbarBtn("Embed Existing .ndraw", () =>
            {
                var firstDrawing = _vault.GetEntries().FirstOrDefault(e => e.Kind == VaultEntryKind.DrawingFile);
                string target = firstDrawing?.Name ?? "Sketchpad Demo.ndraw";
                InsertUtilitySnippet($"\n![[{target}]]\n");
            }));
            wrap.Children.Add(CreateVerticalDivider());
            wrap.Children.Add(CreateToolbarBtn("Export Active Drawing PNG", async () =>
            {
                if (_activeTab?.IsDrawing == true)
                    await _standaloneDrawing.ExportImageAsync(isSvg: false);
            }));
            wrap.Children.Add(CreateToolbarBtn("Export Active Drawing SVG", async () =>
            {
                if (_activeTab?.IsDrawing == true)
                    await _standaloneDrawing.ExportImageAsync(isSvg: true);
            }));
            return wrap;
        }

        private Control BuildGitRibbon()
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            wrap.Children.Add(CreateToolbarBtn("🔀 Open Git Panel", OpenGitOverlay, accent: true));
            wrap.Children.Add(CreateToolbarBtn("🌱 Init Repo", RunGitInit));
            wrap.Children.Add(CreateVerticalDivider());
            wrap.Children.Add(CreateToolbarBtn("✓ Commit All", PromptGitCommitAll, accent: true));
            wrap.Children.Add(CreateToolbarBtn("⚡ Commit & Push", PromptGitCommitAndPush, accent: true));
            wrap.Children.Add(CreateToolbarBtn("⬇ Pull", RunGitPull));
            wrap.Children.Add(CreateToolbarBtn("⬆ Push", RunGitPush));
            wrap.Children.Add(CreateVerticalDivider());
            wrap.Children.Add(CreateToolbarBtn("🌿 Branch", PromptGitBranch));
            wrap.Children.Add(CreateToolbarBtn("🔗 Remote", PromptGitRemote));
            wrap.Children.Add(CreateToolbarBtn("↻ Refresh", () => RefreshGitStatus()));
            _gitRibbonStatusLabel.FontSize = 11.5;
            _gitRibbonStatusLabel.VerticalAlignment = VerticalAlignment.Center;
            _gitRibbonStatusLabel.Margin = new Thickness(10, 0, 0, 0);
            _gitRibbonStatusLabel.Opacity = 0.85;
            if (_gitRibbonStatusLabel.Parent is Panel oldParent)
                oldParent.Children.Remove(_gitRibbonStatusLabel);
            wrap.Children.Add(_gitRibbonStatusLabel);
            return wrap;
        }

        private void BuildVaultExplorerView()
        {
            _vaultSearchBox.PlaceholderText = "Filter files...";
            _vaultSearchBox.FontSize = 12;
            _vaultSearchBox.Margin = new Thickness(0, 0, 0, 6);
            _vaultSearchBox.TextChanged += (_, _) => RefreshVaultTree();

            _vaultListStack.Spacing = 2;
            _vaultListStack.Margin = new Thickness(0, 0, 6, 8);
            _vaultListStack.Background = Brushes.Transparent;

            var content = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*")
            };
            content.Children.Add(_vaultSearchBox);
            var scroller = new ScrollViewer
            {
                AllowAutoHide = false,
                Background = Brushes.Transparent,
                Content = _vaultListStack,
                ContextMenu = CreateVaultContextMenu(entryFullPath: null, entryKind: null)
            };
            Grid.SetRow(scroller, 1);
            content.Children.Add(scroller);

            var bodyGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*")
            };
            var header = CreateViewHeader(
                "VAULT EXPLORER",
                WorkspaceViewKind.VaultExplorer,
                extraAction: ("+ Note", PromptCreateNote),
                secondaryAction: ("+ Folder", PromptCreateFolder));
            bodyGrid.Children.Add(header);

            var innerWrap = new Border { Padding = new Thickness(8, 6), Child = content };
            Grid.SetRow(innerWrap, 1);
            bodyGrid.Children.Add(innerWrap);

            _vaultPanel.Focusable = true;
            _vaultPanel.BorderThickness = new Thickness(0, 0, 1, 1);
            _vaultPanel.Child = bodyGrid;
        }

        private void BuildOutlineAndTagsView()
        {
            _outlineStack.Spacing = 2;
            _tagsWrap.Orientation = Orientation.Horizontal;

            var body = new StackPanel
            {
                Spacing = 8,
                Margin = new Thickness(8, 6),
                Children =
                {
                    new ScrollViewer { MaxHeight = 160, Content = _outlineStack },
                    new TextBlock { Text = "Tags", FontSize = 11, FontWeight = FontWeight.SemiBold, Opacity = 0.75 },
                    _tagsWrap
                }
            };

            var container = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            container.Children.Add(CreateViewHeader("OUTLINE & TAGS", WorkspaceViewKind.OutlineTags));
            Grid.SetRow(body, 1);
            container.Children.Add(body);

            _outlinePanel.BorderThickness = new Thickness(0, 0, 1, 0);
            _outlinePanel.Child = container;
        }

        private void BuildLiveLatexPreviewView()
        {
            _liveLatexBadge.FontSize = 11;
            _liveLatexBadge.FontWeight = FontWeight.SemiBold;
            _liveLatexBadge.VerticalAlignment = VerticalAlignment.Center;

            _liveLatexControl.FontSize = 20;
            _liveLatexControl.IsDisplayMode = true;
            _liveLatexControl.MinHeight = 56;
            _liveLatexControl.HorizontalAlignment = HorizontalAlignment.Center;

            _liveLatexDiag.FontSize = 11;
            _liveLatexDiag.IsVisible = false;

            _liveLatexScratchpad.PlaceholderText = "LaTeX quick scratchpad (e.g. \\frac{\\partial u}{\\partial t} = \\alpha \\nabla^2 u) — previews live here";
            _liveLatexScratchpad.FontSize = 12;
            _liveLatexScratchpad.FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace");

            var insertFromScratchBtn = CreateToolbarBtn("Insert at Caret", () =>
            {
                string tex = _liveLatexScratchpad.Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(tex))
                    InsertUtilitySnippet($"\n```latex\n{tex}\n```\n");
            }, accent: true);

            var scratchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
            scratchRow.Children.Add(_liveLatexScratchpad);
            Grid.SetColumn(insertFromScratchBtn, 1);
            scratchRow.Children.Add(insertFromScratchBtn);

            var previewScroll = new ScrollViewer
            {
                MaxHeight = 140,
                MinHeight = 64,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = _liveLatexControl
            };

            var content = new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(12, 8),
                Children = { _liveLatexBadge, previewScroll, _liveLatexDiag, scratchRow }
            };

            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            grid.Children.Add(CreateViewHeader("LIVE LATEX CODE PREVIEW", WorkspaceViewKind.LiveLatexPreview));
            Grid.SetRow(content, 1);
            grid.Children.Add(content);

            _liveLatexPanel.BorderThickness = new Thickness(0, 1, 0, 0);
            _liveLatexPanel.Child = grid;
        }

        private void BuildPreviewView()
        {
            _previewHeaderTitle.Text = "UNIFIED EDITOR & PREVIEW";
            _previewHeaderTitle.FontSize = 11;
            _previewHeaderTitle.FontWeight = FontWeight.Bold;
            _previewHeaderTitle.VerticalAlignment = VerticalAlignment.Center;

            _editorCursorLabel.FontSize = 11;
            _editorCursorLabel.Opacity = 0.75;
            _editorCursorLabel.VerticalAlignment = VerticalAlignment.Center;
            _editorCursorLabel.Margin = new Thickness(0, 0, 6, 0);

            _unifiedModeToggleBtn.Content = "📄 Raw Source Editor (H1–H5 Fold)";
            _unifiedModeToggleBtn.FontSize = 11.5;
            _unifiedModeToggleBtn.Padding = new Thickness(8, 2);
            _unifiedModeToggleBtn.CornerRadius = new CornerRadius(5);
            _unifiedModeToggleBtn.Click += (_, _) => ToggleUnifiedWindowMode();

            var rightControls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _editorCursorLabel, _unifiedModeToggleBtn }
            };

            var header = CreateViewHeaderCustom(
                _previewHeaderTitle,
                WorkspaceViewKind.PreviewPane,
                rightBadge: rightControls,
                onCloseOverride: CloseUnifiedPane);

            _previewPanelGrid.RowDefinitions = new RowDefinitions("Auto,*,Auto");
            _previewPanelGrid.Children.Add(header);
            Grid.SetRow(_previewContentHost, 1);
            _previewPanelGrid.Children.Add(_previewContentHost);
            Grid.SetRow(_liveLatexPanel, 2);
            _previewPanelGrid.Children.Add(_liveLatexPanel);

            _previewContentHost.Content = _markdownPreview;
            _previewPanel.Child = _previewPanelGrid;
        }

        private void ToggleUnifiedWindowMode()
            => SetUnifiedWindowMode(!_showRawSourceInUnifiedWindow);

        private void SetUnifiedWindowMode(bool showRawSource)
        {
            _showRawSourceInUnifiedWindow = showRawSource;
            _viewState.OpenView(showRawSource ? WorkspaceViewKind.EditorPane : WorkspaceViewKind.PreviewPane);
            RefreshViewLayout();
            if (showRawSource && _activeTab is { IsDrawing: false })
                _editorTextBox.Focus();
        }

        private void CloseUnifiedPane()
        {
            _viewState.CloseView(WorkspaceViewKind.PreviewPane);
            _viewState.CloseView(WorkspaceViewKind.EditorPane);
        }

        private void BuildStatusBarView()
        {
            _statusGitText.Cursor = new Cursor(StandardCursorType.Hand);
            _statusGitText.PointerPressed += (_, _) => OpenGitOverlay();
            ToolTip.SetTip(_statusGitText, $"Open Git Source Control Panel ({PlatformShortcuts.Format("Shift+G")})");

            var left = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _statusSaveText, _statusGitText, _statusViewsText, _statusCaretText, _statusLatexText }
            };

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _statusCountsText, CreateCloseViewButton(WorkspaceViewKind.StatusBar, "Close status bar") }
            };

            foreach (var tb in new[] { _statusSaveText, _statusGitText, _statusViewsText, _statusCaretText, _statusLatexText, _statusCountsText })
                tb.FontSize = 11.5;

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(left);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            _statusBarBorder.Padding = new Thickness(12, 4);
            _statusBarBorder.BorderThickness = new Thickness(0, 1, 0, 0);
            _statusBarBorder.Child = grid;
        }


        private void RefreshViewLayout()
        {
            bool showVault = _viewState.IsVisible(WorkspaceViewKind.VaultExplorer);
            bool showOutline = _viewState.IsVisible(WorkspaceViewKind.OutlineTags);
            bool showSidebar = showVault || showOutline;

            _vaultPanel.IsVisible = showVault;
            _outlinePanel.IsVisible = showOutline;
            _sidebarGrid.IsVisible = showSidebar;

            if (showSidebar)
            {
                _mainBodyGrid.ColumnDefinitions[0].Width = new GridLength(270);
                _sidebarGrid.RowDefinitions[0].Height = showVault ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                _sidebarGrid.RowDefinitions[1].Height = showOutline ? GridLength.Auto : new GridLength(0);
            }
            else
            {
                _mainBodyGrid.ColumnDefinitions[0].Width = new GridLength(0);
            }

            bool isDrawingTab = _activeTab?.IsDrawing == true;
            bool isImageTab = _activeTab?.IsImage == true;
            bool editorOpen = _viewState.IsVisible(WorkspaceViewKind.EditorPane);
            bool previewOpen = _viewState.IsVisible(WorkspaceViewKind.PreviewPane);
            bool showUnifiedWindow = (isDrawingTab || isImageTab) ? previewOpen : (previewOpen || editorOpen);
            bool showRawSource = !isDrawingTab && !isImageTab && (_showRawSourceInUnifiedWindow || (!previewOpen && editorOpen));
            bool showLiveLatex = !isDrawingTab && !isImageTab && _viewState.IsVisible(WorkspaceViewKind.LiveLatexPreview);

            _editorPanel.IsVisible = false;
            _leftEditorColumnGrid.IsVisible = false;
            _workspaceSplitGrid.ColumnDefinitions[0].Width = new GridLength(0);
            _workspaceSplitGrid.ColumnDefinitions[1].Width = showUnifiedWindow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

            _previewPanel.IsVisible = showUnifiedWindow;
            _liveLatexPanel.IsVisible = showLiveLatex;
            if (_previewPanelGrid.RowDefinitions.Count >= 3)
                _previewPanelGrid.RowDefinitions[2].Height = showLiveLatex ? GridLength.Auto : new GridLength(0);

            if (isDrawingTab)
            {
                _previewContentHost.Content = _standaloneDrawing;
                _unifiedModeToggleBtn.IsVisible = false;
                _editorCursorLabel.IsVisible = false;
            }
            else if (isImageTab && _activeTab is not null)
            {
                _previewContentHost.Content = BuildStandaloneImageViewer(_activeTab.FilePath);
                _unifiedModeToggleBtn.IsVisible = false;
                _editorCursorLabel.IsVisible = false;
            }
            else if (showRawSource)
            {
                _previewContentHost.Content = _foldableEditor;
                _unifiedModeToggleBtn.IsVisible = true;
                _unifiedModeToggleBtn.Content = "✨ Unified Live Editor & Preview";
                _editorCursorLabel.IsVisible = true;
            }
            else
            {
                _previewContentHost.Content = _markdownPreview;
                _unifiedModeToggleBtn.IsVisible = true;
                _unifiedModeToggleBtn.Content = "📄 Raw Source Editor (H1–H5 Fold)";
                _editorCursorLabel.IsVisible = false;
            }

            _statusBarBorder.IsVisible = _viewState.IsVisible(WorkspaceViewKind.StatusBar);

            RefreshRibbonContent();
            UpdateViewToggleLabels();
            UpdateStatusBar();
        }

        private void UpdateViewToggleLabels()
        {
            foreach (var (view, item) in _viewToggleMenuItems)
            {
                bool open = _viewState.IsVisible(view);
                item.Header = $"{(open ? "[✓]" : "[ ]")} {ViewLayoutState.GetDisplayName(view)}";
            }
        }


        private void ApplyTheme(bool isDark, bool persistToConfig = true)
        {
            _isDarkTheme = isDark;
            _themeConfig.Config.ActiveTheme = isDark ? "Dark" : "Light";
            var palette = _themeConfig.CurrentPalette;
            if (persistToConfig && !string.IsNullOrWhiteSpace(_themeConfig.ActiveConfigPath) && !File.Exists(_themeConfig.ActiveConfigPath))
                _themeConfig.SavePaletteToFile(palette, _themeConfig.ActiveConfigPath);

            if (Application.Current is not null)
                Application.Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;

            _themeBtn.Content = isDark ? "Light Mode" : "Dark Mode";

            var chromeBg = ThemeConfigService.ParseBrush(palette.ChromeBackground, isDark ? "#1E1E1E" : "#F3F3F3");
            var panelBg = ThemeConfigService.ParseBrush(palette.PanelBackground, isDark ? "#252526" : "#FFFFFF");
            var headerBg = ThemeConfigService.ParseBrush(palette.HeaderBackground, isDark ? "#2D2D2D" : "#E8E8E8");
            var border = ThemeConfigService.ParseBrush(palette.BorderColor, isDark ? "#3C3C3C" : "#D4D4D4");
            var accent = AccentBrush();
            var text = ForegroundBrush();

            Background = panelBg;
            _topBarBorder.Background = chromeBg;
            _topBarBorder.BorderBrush = border;
            _ribbonBorder.Background = chromeBg;
            _ribbonBorder.BorderBrush = border;
            _ribbonContentHost.BorderBrush = border;
            _tabsBarBorder.Background = chromeBg;
            _tabsBarBorder.BorderBrush = border;
            _statusBarBorder.Background = chromeBg;
            _statusBarBorder.BorderBrush = border;

            _vaultPanel.Background = chromeBg;
            _vaultPanel.BorderBrush = border;
            _outlinePanel.Background = chromeBg;
            _outlinePanel.BorderBrush = border;

            _editorPanel.Background = panelBg;
            _editorPanel.BorderBrush = border;
            _foldableEditor.ApplyThemePalette(palette, isDark, _themeConfig.Config.EditorFontSize, _themeConfig.Config.EditorFontFamily);

            _liveLatexPanel.Background = headerBg;
            _liveLatexPanel.BorderBrush = border;
            _liveLatexControl.Foreground = text;
            _liveLatexDiag.Foreground = ThemeConfigService.ParseBrush(palette.WarningColor, isDark ? "#FCE100" : "#9D5D00");

            _previewPanel.Background = panelBg;
            _markdownPreview.ApplyThemePalette(palette, isDark, _themeConfig.Config.PreviewFontSize);
            _standaloneDrawing.ApplyThemePalette(palette, isDark);
            _floatingSyntaxGuidePopup.ApplyThemePalette(palette, isDark);

            if (_quickSwitcherOverlay.Child is Border qsCard)
            {
                qsCard.Background = panelBg;
                qsCard.BorderBrush = accent;
            }
            if (_promptOverlay.Child is Border pCard)
            {
                pCard.Background = panelBg;
                pCard.BorderBrush = accent;
            }
            if (_unsavedExitOverlay.Child is Border ueCard)
            {
                ueCard.Background = panelBg;
                ueCard.BorderBrush = accent;
            }
            if (_gitOverlay.Child is Border gitCard)
            {
                gitCard.Background = panelBg;
                gitCard.BorderBrush = accent;
            }

            foreach (var accentBtn in _accentButtons)
            {
                accentBtn.Background = accent;
                accentBtn.Foreground = Brushes.White;
            }

            RefreshRibbonContent();
            RefreshTabStrip();
            RefreshVaultTree();
            if (_activeTab is not null && !_activeTab.IsDrawing)
                RefreshOutlineAndTags(_activeTab.Content);
        }

        private Border CreateViewHeader(string title, WorkspaceViewKind view, (string Label, Action Action)? extraAction = null, Control? rightBadge = null, (string Label, Action Action)? secondaryAction = null)
        {
            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            return CreateViewHeaderCustom(titleBlock, view, extraAction, rightBadge, secondaryAction: secondaryAction);
        }

        private Border CreateViewHeaderCustom(Control titleControl, WorkspaceViewKind view, (string Label, Action Action)? extraAction = null, Control? rightBadge = null, Action? onCloseOverride = null, (string Label, Action Action)? secondaryAction = null)
        {
            var rightStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (rightBadge is not null)
                rightStack.Children.Add(rightBadge);

            if (secondaryAction.HasValue)
            {
                var secBtn = CreateToolbarBtn(secondaryAction.Value.Label, secondaryAction.Value.Action);
                secBtn.Padding = new Thickness(6, 2);
                rightStack.Children.Add(secBtn);
            }

            if (extraAction.HasValue)
            {
                var extraBtn = CreateToolbarBtn(extraAction.Value.Label, extraAction.Value.Action);
                extraBtn.Padding = new Thickness(6, 2);
                rightStack.Children.Add(extraBtn);
            }

            rightStack.Children.Add(CreateCloseViewButton(view, $"Close {ViewLayoutState.GetDisplayName(view)} (restore from View & Mode → View)", onCloseOverride));

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(titleControl);
            Grid.SetColumn(rightStack, 1);
            grid.Children.Add(rightStack);

            return new Border
            {
                Padding = new Thickness(10, 5),
                Background = new SolidColorBrush(Color.FromArgb(26, 148, 163, 184)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(45, 148, 163, 184)),
                Child = grid
            };
        }

        private Button CreateCloseViewButton(WorkspaceViewKind view, string tooltip, Action? onCloseOverride = null)
        {
            var btn = new Button
            {
                Content = "×",
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Padding = new Thickness(6, 1),
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            ToolTip.SetTip(btn, tooltip);
            btn.Click += (_, _) =>
            {
                if (onCloseOverride is not null)
                    onCloseOverride();
                else
                    _viewState.CloseView(view);
            };
            return btn;
        }

        private Button CreatePillButton(string label, Action onClick, bool accent = false)
        {
            var btn = new Button
            {
                Content = label,
                FontSize = 12,
                Padding = new Thickness(10, 5),
                CornerRadius = new CornerRadius(6)
            };
            if (accent)
            {
                btn.Background = AccentBrush();
                btn.Foreground = Brushes.White;
                _accentButtons.Add(btn);
            }
            btn.Click += (_, _) => onClick();
            return btn;
        }

        private Button CreateToolbarBtn(string label, Action onClick, bool accent = false)
        {
            var btn = new Button
            {
                Content = label,
                FontSize = 11.5,
                Padding = new Thickness(8, 3),
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(5)
            };
            if (accent)
            {
                btn.Background = AccentBrush();
                btn.Foreground = Brushes.White;
                _accentButtons.Add(btn);
            }
            btn.Click += (_, _) => onClick();
            return btn;
        }

        private static Border CreateVerticalDivider() => new()
        {
            Width = 1,
            Height = 20,
            Margin = new Thickness(6, 2),
            Background = new SolidColorBrush(Color.FromArgb(80, 128, 128, 128)),
            VerticalAlignment = VerticalAlignment.Center
        };

        private IBrush AccentBrush()
            => ThemeConfigService.ParseBrush(_themeConfig.CurrentPalette.AccentColor, _isDarkTheme ? "#0078D4" : "#0067C0");

        private IBrush InactiveTabBrush()
            => ThemeConfigService.ParseBrush(_themeConfig.CurrentPalette.CardBackground, _isDarkTheme ? "#2A2D2E" : "#E8E8E8");

        private IBrush ForegroundBrush()
            => ThemeConfigService.ParseBrush(_themeConfig.CurrentPalette.TextColor, _isDarkTheme ? "#E0E0E0" : "#1E1E1E");

        private IBrush MutedBrush()
            => ThemeConfigService.ParseBrush(_themeConfig.CurrentPalette.MutedTextColor, _isDarkTheme ? "#9E9E9E" : "#616161");
    }
}
