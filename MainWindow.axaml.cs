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
using NoteDraw.UI.Overlays;
using NoteDraw.UI.Panels;

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

        // Center tabs & split views
        private readonly Border _tabsBarBorder = new();
        private readonly StackPanel _tabsStrip = new();
        private readonly Grid _workspaceSplitGrid = new();
        private readonly Grid _leftEditorColumnGrid = new();
        private readonly Border _editorPanel = new();
        private readonly FoldableEditorControl _foldableEditor = new();
        private TextBox _editorTextBox => _foldableEditor.InnerTextBox;
        private readonly TextBlock _editorCursorLabel = new();

        // Unified Editor + Preview + Standalone Drawing view
        private readonly Border _previewPanel = new();
        private readonly Grid _previewPanelGrid = new();
        private readonly TextBlock _previewHeaderTitle = new();
        private readonly Button _unifiedModeToggleBtn = new();
        private readonly ContentControl _previewContentHost = new();
        private readonly MarkdownPreviewControl _markdownPreview = new();
        private readonly DrawingWorkspaceControl _standaloneDrawing = new(compactInlineMode: false, isDarkTheme: true);
        private readonly TextBlock _gitRibbonStatusLabel = new();

        // Mediator & Phase 1 UI colleagues
        private WorkspaceMediator _mediator = null!;
        private OutlineAndTagsPanel _outlineAndTagsPanel = null!;
        private LiveLatexPreviewPanel _liveLatexPreviewPanel = null!;
        private StatusBarPanel _statusBarPanel = null!;

        public bool AutoSaveEnabled
        {
            get => _autoSaveEnabled;
            set => SetAutoSaveEnabled(value);
        }

        public bool HasUnsavedDocuments => _openTabs.Any(t => t.IsDirty && !t.IsImage);
        public bool IsUnsavedExitMessageBoxOpen => _mediator.IsUnsavedExitOpen;
        public void OpenGitOverlay() => _mediator.OpenGitOverlay();
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

            InitializeMediatorAndColleagues();
            BuildWorkspaceLayout();
            WireEvents();
            ApplyTheme(_isDarkTheme, persistToConfig: false);
            RefreshRibbonContent();
            RefreshViewLayout();
            RefreshVaultTree();
            _mediator.RefreshGitStatus();
            OpenStarterDocument();
        }

        private void InitializeMediatorAndColleagues()
        {
            var callbacks = new WorkspaceHostCallbacks
            {
                GetCurrentVaultPath = () => _vault.VaultPath,
                GetVaultEntries = filter => _vault.GetEntries(filter),
                GetAutoSaveEnabled = () => _autoSaveEnabled,
                GetHasUnsavedDocuments = () => HasUnsavedDocuments,
                GetCurrentPalette = () => _themeConfig.CurrentPalette,
                GetIsDarkTheme = () => _isDarkTheme,
                OpenFileInTab = OpenFileInTab,
                JumpEditorToLine = JumpEditorToLine,
                FilterVaultByTag = tag =>
                {
                    _viewState.OpenView(WorkspaceViewKind.VaultExplorer);
                    _vaultSearchBox.Text = tag.TrimStart('#');
                },
                InsertLatexSnippet = tex => InsertUtilitySnippet($"\n```latex\n{tex}\n```\n"),
                CloseView = view => _viewState.CloseView(view),
                RefreshVaultTree = RefreshVaultTree,
                SaveAllDirtyTabs = SaveAllDirtyTabs,
                ApplyUnsavedExitDecision = ApplyUnsavedExitDecision,
                OnGitStatusUpdated = UpdateGitRibbonStatusLabel,
                NotifyNonVaultPanelFocused = () => _isVaultExplorerFocused = false
            };

            _mediator = new WorkspaceMediator(callbacks);
            _statusBarPanel = new StatusBarPanel(_mediator);
            _outlineAndTagsPanel = new OutlineAndTagsPanel(_mediator);
            _liveLatexPreviewPanel = new LiveLatexPreviewPanel(_mediator);

            _mediator.RegisterPanels(_statusBarPanel, _outlineAndTagsPanel, _liveLatexPreviewPanel);
            _mediator.RegisterOverlays(
                new UnsavedExitOverlay(_mediator),
                new PromptDialogOverlay(_mediator),
                new QuickSwitcherOverlay(_mediator),
                new GitOverlayPanel(_mediator, _git),
                new SyntaxGuideOverlay());
        }

        private void UpdateGitRibbonStatusLabel(GitRepositoryStatus status)
        {
            if (!status.IsGitAvailable)
            {
                _gitRibbonStatusLabel.Text = "Git CLI not detected on PATH";
                return;
            }

            if (!status.IsRepository)
            {
                _gitRibbonStatusLabel.Text = "Not a Git repo yet (click Init Repo)";
                return;
            }

            int changedCount = status.Changes.Count;
            _gitRibbonStatusLabel.Text = changedCount > 0
                ? $"🌿 {status.BranchName} ({changedCount} changed)"
                : $"🌿 {status.BranchName} ✓";
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
            Grid.SetRow(_vaultPanel, 0);
            Grid.SetRow(_outlineAndTagsPanel, 1);
            _sidebarGrid.Children.Add(_vaultPanel);
            _sidebarGrid.Children.Add(_outlineAndTagsPanel);
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
            Grid.SetRow(_statusBarPanel, 3);
            outerGrid.Children.Add(_statusBarPanel);

            var rootContainer = new Grid();
            rootContainer.Children.Add(outerGrid);
            foreach (var overlay in _mediator.OverlayControls)
                rootContainer.Children.Add(overlay);

            RootHost.Children.Add(rootContainer);
        }

        private static MenuItem MakeTopMenuItem(string header, Action onClick)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => onClick();
            return item;
        }

        private Control BuildTopBar()
        {
            _topMenuBar.Items.Clear();
            _topMenuBar.Background = Brushes.Transparent;
            _topMenuBar.FontSize = 12.5;
            _topMenuBar.VerticalAlignment = VerticalAlignment.Center;
            _topMenuBar.Items.Add(BuildFileMenu());
            _topMenuBar.Items.Add(BuildEditAndInsertMenu());
            _topMenuBar.Items.Add(BuildViewAndModeMenu());
            _topMenuBar.Items.Add(BuildOptionsMenu());
            _topMenuBar.Items.Add(BuildGitMenu());
            _topMenuBar.Items.Add(BuildThemeMenu());

            var toolbarWrap = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            AddTopQuickActionButtons(toolbarWrap);

            _topBarBorder.Padding = new Thickness(12, 5);
            _topBarBorder.BorderThickness = new Thickness(0, 0, 0, 1);
            _topBarBorder.Child = toolbarWrap;
            return _topBarBorder;
        }

        private MenuItem BuildFileMenu()
        {
            var fileMenu = new MenuItem { Header = "File" };
            fileMenu.Items.Add(MakeTopMenuItem("Open Vault Folder...", async () => await OpenVaultFolderAsync()));
            fileMenu.Items.Add(MakeTopMenuItem($"New Note ({PlatformShortcuts.Format("N")})", PromptCreateNote));
            fileMenu.Items.Add(MakeTopMenuItem("New Folder...", PromptCreateFolder));
            fileMenu.Items.Add(MakeTopMenuItem($"New Standalone Drawing ({PlatformShortcuts.Format("Shift+D")})", PromptCreateDrawing));
            fileMenu.Items.Add(new Separator());
            fileMenu.Items.Add(MakeTopMenuItem($"Save ({PlatformShortcuts.Format("S")})", SaveActiveTab));
            fileMenu.Items.Add(MakeTopMenuItem($"Save All ({PlatformShortcuts.Format("Shift+S")})", SaveAllDirtyTabs));
            fileMenu.Items.Add(MakeTopMenuItem($"Quick Switcher ({PlatformShortcuts.Format("P")})", () => _mediator.OpenQuickSwitcher()));
            fileMenu.Items.Add(new Separator());
            fileMenu.Items.Add(MakeTopMenuItem("Exit", Close));
            return fileMenu;
        }

        private MenuItem BuildEditAndInsertMenu()
        {
            var editInsertMenu = new MenuItem { Header = "Edit & Insert" };
            editInsertMenu.Items.Add(MakeTopMenuItem($"↶ Undo ({PlatformShortcuts.Format("Z")})", PerformUndo));
            editInsertMenu.Items.Add(MakeTopMenuItem($"↷ Redo ({PlatformShortcuts.Format("Y")})", PerformRedo));
            editInsertMenu.Items.Add(new Separator());
            editInsertMenu.Items.Add(MakeTopMenuItem("+ Insert Canvas in Document", InsertNewDrawingBlockAtCaret));
            editInsertMenu.Items.Add(MakeTopMenuItem("</ > Insert Code Block", InsertCodeBlockAtCaret));
            editInsertMenu.Items.Add(MakeTopMenuItem($"📋 Paste Image ({PlatformShortcuts.Format("V")})", async () => await PasteImageIntoDocumentAsync(allowFilePickerFallback: true)));
            return editInsertMenu;
        }

        private MenuItem BuildViewAndModeMenu()
        {
            var viewModeMenu = new MenuItem { Header = "View & Mode" };
            viewModeMenu.Items.Add(MakeTopMenuItem("✨ Unified Live View", () => SetUnifiedWindowMode(showRawSource: false)));
            viewModeMenu.Items.Add(MakeTopMenuItem("📄 Raw Source Mode (H1–H5 Fold)", () => SetUnifiedWindowMode(showRawSource: true)));
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
            viewModeMenu.Items.Add(MakeTopMenuItem("Reset All Views", () => _viewState.ResetAllViews()));
            return viewModeMenu;
        }

        private MenuItem BuildOptionsMenu()
        {
            var optionsMenu = new MenuItem { Header = "Options" };
            _autoSaveMenuItem.Header = GetAutoSaveMenuHeader();
            _autoSaveMenuItem.Click += (_, _) => SetAutoSaveEnabled(!_autoSaveEnabled);
            optionsMenu.Items.Add(_autoSaveMenuItem);
            return optionsMenu;
        }

        private MenuItem BuildGitMenu()
        {
            var gitMenu = new MenuItem { Header = "Git" };
            gitMenu.Items.Add(MakeTopMenuItem($"🔀 Git Source Control Panel... ({PlatformShortcuts.Format("Shift+G")})", () => _mediator.OpenGitOverlay()));
            gitMenu.Items.Add(new Separator());
            gitMenu.Items.Add(MakeTopMenuItem("✓ Commit All Changes...", () => _mediator.PromptGitCommitAll()));
            gitMenu.Items.Add(MakeTopMenuItem("⚡ Commit & Push (Sync)", () => _mediator.PromptGitCommitAndPush()));
            gitMenu.Items.Add(MakeTopMenuItem("⬇ Git Pull", () => _mediator.RunGitPull()));
            gitMenu.Items.Add(MakeTopMenuItem("⬆ Git Push", () => _mediator.RunGitPush()));
            gitMenu.Items.Add(new Separator());
            gitMenu.Items.Add(MakeTopMenuItem("🌱 Initialize Git Repo in Vault", () => _mediator.RunGitInit()));
            gitMenu.Items.Add(MakeTopMenuItem("🌿 Switch / Create Branch...", () => _mediator.PromptGitBranch()));
            gitMenu.Items.Add(MakeTopMenuItem("🔗 Set Remote Origin URL...", () => _mediator.PromptGitRemote()));
            return gitMenu;
        }

        private MenuItem BuildThemeMenu()
        {
            var themeMenu = new MenuItem { Header = "Theme" };
            themeMenu.Items.Add(MakeTopMenuItem("Switch Dark / Light Mode", () => ApplyTheme(!_isDarkTheme, persistToConfig: true)));
            themeMenu.Items.Add(new Separator());
            themeMenu.Items.Add(MakeTopMenuItem("Edit dark.json", OpenDarkThemeConfigInTab));
            themeMenu.Items.Add(MakeTopMenuItem("Edit light.json", OpenLightThemeConfigInTab));
            themeMenu.Items.Add(MakeTopMenuItem("Load Custom Theme (.json)...", async () => await LoadCustomThemeFileAsync()));
            return themeMenu;
        }

        private void AddTopQuickActionButtons(WrapPanel toolbarWrap)
        {
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

            var helpBtn = CreatePillButton("?", () => _mediator.ToggleSyntaxGuide());
            helpBtn.FontWeight = FontWeight.Bold;
            helpBtn.FontSize = 13;
            helpBtn.MinWidth = 28;
            helpBtn.Padding = new Thickness(9, 4);
            ToolTip.SetTip(helpBtn, "Markdown & LaTeX Syntax Guide (F1)");
            AddWrapped(helpBtn, rightMargin: 0);
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
            wrap.Children.Add(CreateToolbarBtn("🔀 Open Git Panel", () => _mediator.OpenGitOverlay(), accent: true));
            wrap.Children.Add(CreateToolbarBtn("🌱 Init Repo", () => _mediator.RunGitInit()));
            wrap.Children.Add(CreateVerticalDivider());
            wrap.Children.Add(CreateToolbarBtn("✓ Commit All", () => _mediator.PromptGitCommitAll(), accent: true));
            wrap.Children.Add(CreateToolbarBtn("⚡ Commit & Push", () => _mediator.PromptGitCommitAndPush(), accent: true));
            wrap.Children.Add(CreateToolbarBtn("⬇ Pull", () => _mediator.RunGitPull()));
            wrap.Children.Add(CreateToolbarBtn("⬆ Push", () => _mediator.RunGitPush()));
            wrap.Children.Add(CreateVerticalDivider());
            wrap.Children.Add(CreateToolbarBtn("🌿 Branch", () => _mediator.PromptGitBranch()));
            wrap.Children.Add(CreateToolbarBtn("🔗 Remote", () => _mediator.PromptGitRemote()));
            wrap.Children.Add(CreateToolbarBtn("↻ Refresh", () => _mediator.RefreshGitStatus()));
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
            Grid.SetRow(_liveLatexPreviewPanel, 2);
            _previewPanelGrid.Children.Add(_liveLatexPreviewPanel);

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

        private void RefreshViewLayout()
        {
            bool showVault = _viewState.IsVisible(WorkspaceViewKind.VaultExplorer);
            bool showOutline = _viewState.IsVisible(WorkspaceViewKind.OutlineTags);
            bool showSidebar = showVault || showOutline;

            _vaultPanel.IsVisible = showVault;
            _outlineAndTagsPanel.IsVisible = showOutline;
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
            _liveLatexPreviewPanel.IsVisible = showLiveLatex;
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

            _statusBarPanel.IsVisible = _viewState.IsVisible(WorkspaceViewKind.StatusBar);

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
            var border = ThemeConfigService.ParseBrush(palette.BorderColor, isDark ? "#3C3C3C" : "#D4D4D4");
            var accent = AccentBrush();

            Background = panelBg;
            _topBarBorder.Background = chromeBg;
            _topBarBorder.BorderBrush = border;
            _ribbonBorder.Background = chromeBg;
            _ribbonBorder.BorderBrush = border;
            _ribbonContentHost.BorderBrush = border;
            _tabsBarBorder.Background = chromeBg;
            _tabsBarBorder.BorderBrush = border;

            _vaultPanel.Background = chromeBg;
            _vaultPanel.BorderBrush = border;

            _editorPanel.Background = panelBg;
            _editorPanel.BorderBrush = border;
            _foldableEditor.ApplyThemePalette(palette, isDark, _themeConfig.Config.EditorFontSize, _themeConfig.Config.EditorFontFamily);

            _previewPanel.Background = panelBg;
            _markdownPreview.ApplyThemePalette(palette, isDark, _themeConfig.Config.PreviewFontSize);
            _standaloneDrawing.ApplyThemePalette(palette, isDark);

            foreach (var accentBtn in _accentButtons)
            {
                accentBtn.Background = accent;
                accentBtn.Foreground = Brushes.White;
            }

            _mediator.NotifyThemeChanged(palette, isDark);
            RefreshRibbonContent();
            RefreshTabStrip();
            RefreshVaultTree();
        }

        private Border CreateViewHeader(string title, WorkspaceViewKind view, (string Label, Action Action)? extraAction = null, Control? rightBadge = null, (string Label, Action Action)? secondaryAction = null)
            => WorkspaceUiFactory.CreateViewHeader(
                title,
                view,
                _viewState.CloseView,
                extraAction,
                rightBadge,
                secondaryAction,
                AccentBrush(),
                _accentButtons);

        private Border CreateViewHeaderCustom(Control titleControl, WorkspaceViewKind view, (string Label, Action Action)? extraAction = null, Control? rightBadge = null, Action? onCloseOverride = null, (string Label, Action Action)? secondaryAction = null)
            => WorkspaceUiFactory.CreateViewHeaderCustom(
                titleControl,
                view,
                _viewState.CloseView,
                extraAction,
                rightBadge,
                onCloseOverride,
                secondaryAction,
                AccentBrush(),
                _accentButtons);

        private Button CreateCloseViewButton(WorkspaceViewKind view, string tooltip, Action? onCloseOverride = null)
            => WorkspaceUiFactory.CreateCloseViewButton(view, tooltip, _viewState.CloseView, onCloseOverride);

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
            => WorkspaceUiFactory.CreateToolbarButton(label, onClick, accent, AccentBrush(), _accentButtons);

        private static Border CreateVerticalDivider() => WorkspaceUiFactory.CreateVerticalDivider();

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
