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
    internal sealed class OpenDocumentTab
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title => Path.GetFileName(FilePath);
        public bool IsDrawing => FilePath.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase);
        public bool IsImage => MainWindow.IsSupportedImageExtension(FilePath);
        public string Content { get; set; } = string.Empty;
        public bool IsDirty { get; set; }
        public int CaretIndex { get; set; }
        public string? SourceMarkdownFilePath { get; set; }
        public int? SourceInlineFenceLineZeroBased { get; set; }
        public TextUndoManager TextUndo { get; } = new();
    }

    internal enum RibbonTabKind
    {
        DrawingTools,
        Git
    }

    public enum UnsavedExitDecision
    {
        Save,
        Discard,
        Cancel
    }

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
            fileMenu.Items.Add(MakeMenuItem("New Note (Ctrl+N)", PromptCreateNote));
            fileMenu.Items.Add(MakeMenuItem("New Folder...", PromptCreateFolder));
            fileMenu.Items.Add(MakeMenuItem("New Standalone Drawing (Ctrl+Shift+D)", PromptCreateDrawing));
            fileMenu.Items.Add(new Separator());
            fileMenu.Items.Add(MakeMenuItem("Save (Ctrl+S)", SaveActiveTab));
            fileMenu.Items.Add(MakeMenuItem("Save All (Ctrl+Shift+S)", SaveAllDirtyTabs));
            fileMenu.Items.Add(MakeMenuItem("Quick Switcher (Ctrl+P)", OpenQuickSwitcher));
            fileMenu.Items.Add(new Separator());
            fileMenu.Items.Add(MakeMenuItem("Exit", Close));

            var editInsertMenu = new MenuItem { Header = "Edit & Insert" };
            editInsertMenu.Items.Add(MakeMenuItem("↶ Undo (Ctrl+Z)", PerformUndo));
            editInsertMenu.Items.Add(MakeMenuItem("↷ Redo (Ctrl+Y)", PerformRedo));
            editInsertMenu.Items.Add(new Separator());
            editInsertMenu.Items.Add(MakeMenuItem("+ Insert Canvas in Document", InsertNewDrawingBlockAtCaret));
            editInsertMenu.Items.Add(MakeMenuItem("📋 Paste Image (Ctrl+V)", async () => await PasteImageIntoDocumentAsync(allowFilePickerFallback: true)));

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
            gitMenu.Items.Add(MakeMenuItem("🔀 Git Source Control Panel... (Ctrl+Shift+G)", OpenGitOverlay));
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
            ToolTip.SetTip(_topUndoBtn, "Undo (Ctrl+Z)");
            AddWrapped(_topUndoBtn);

            _topRedoBtn.Content = "↷ Redo";
            _topRedoBtn.FontSize = 12;
            _topRedoBtn.FontWeight = FontWeight.SemiBold;
            _topRedoBtn.Padding = new Thickness(10, 5);
            _topRedoBtn.CornerRadius = new CornerRadius(6);
            _topRedoBtn.Click += (_, _) => PerformRedo();
            ToolTip.SetTip(_topRedoBtn, "Redo (Ctrl+Y / Ctrl+Shift+Z)");
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
            ToolTip.SetTip(_statusGitText, "Open Git Source Control Panel (Ctrl+Shift+G)");

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

        private void BuildSyntaxGuideOverlay()
        {
            _floatingSyntaxGuidePopup.Width = 680;
            _floatingSyntaxGuidePopup.MaxWidth = 740;
            _floatingSyntaxGuidePopup.HorizontalAlignment = HorizontalAlignment.Right;
            _floatingSyntaxGuidePopup.VerticalAlignment = VerticalAlignment.Top;
            _floatingSyntaxGuidePopup.Margin = new Thickness(0, 56, 20, 0);
            _floatingSyntaxGuidePopup.CloseRequested += (_, _) => _syntaxGuideOverlay.IsVisible = false;

            _syntaxGuideOverlay.IsVisible = false;
            _syntaxGuideOverlay.Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
            _syntaxGuideOverlay.Child = _floatingSyntaxGuidePopup;
            _syntaxGuideOverlay.PointerPressed += (_, e) =>
            {
                if (ReferenceEquals(e.Source, _syntaxGuideOverlay))
                {
                    _syntaxGuideOverlay.IsVisible = false;
                    e.Handled = true;
                }
            };
        }

        private void ShowSyntaxGuidePopup()
        {
            _syntaxGuideOverlay.IsVisible = true;
            _floatingSyntaxGuidePopup.FocusSearchBox();
        }

        private void ToggleSyntaxGuidePopup()
        {
            if (_syntaxGuideOverlay.IsVisible)
                _syntaxGuideOverlay.IsVisible = false;
            else
                ShowSyntaxGuidePopup();
        }

        private void BuildQuickSwitcherOverlay()
        {
            _quickSwitcherInput.PlaceholderText = "Jump to note or drawing by name... (Esc to close)";
            _quickSwitcherInput.FontSize = 13.5;
            _quickSwitcherInput.TextChanged += (_, _) => PopulateQuickSwitcherResults();
            _quickSwitcherInput.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    _quickSwitcherOverlay.IsVisible = false;
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    var firstBtn = _quickSwitcherResults.Children.OfType<Button>().FirstOrDefault();
                    firstBtn?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    e.Handled = true;
                }
            };

            _quickSwitcherResults.Spacing = 4;

            var card = new Border
            {
                Width = 520,
                MaxHeight = 420,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#3B82F6")),
                Padding = new Thickness(14),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 72, 0, 0),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Quick Switcher", FontSize = 14, FontWeight = FontWeight.Bold },
                        _quickSwitcherInput,
                        new ScrollViewer { MaxHeight = 290, Content = _quickSwitcherResults },
                        new Button
                        {
                            Content = "Close (Esc)",
                            HorizontalAlignment = HorizontalAlignment.Right,
                            FontSize = 11
                        }.Also(b => b.Click += (_, _) => _quickSwitcherOverlay.IsVisible = false)
                    }
                }
            };

            _quickSwitcherOverlay.IsVisible = false;
            _quickSwitcherOverlay.Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
            _quickSwitcherOverlay.Child = card;
        }

        private void BuildPromptOverlay()
        {
            _promptTitle.FontSize = 14;
            _promptTitle.FontWeight = FontWeight.Bold;
            _promptInput.FontSize = 13;
            _promptInput.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    SubmitPrompt();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    _promptOverlay.IsVisible = false;
                    e.Handled = true;
                }
            };

            var card = new Border
            {
                Width = 420,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#3B82F6")),
                Padding = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        _promptTitle,
                        _promptInput,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                CreateToolbarBtn("Cancel", () => _promptOverlay.IsVisible = false),
                                CreateToolbarBtn("Confirm", SubmitPrompt, accent: true)
                            }
                        }
                    }
                }
            };

            _promptOverlay.IsVisible = false;
            _promptOverlay.Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
            _promptOverlay.Child = card;
        }

        private void BuildUnsavedExitMessageBoxOverlay()
        {
            _unsavedExitMessageText.FontSize = 13;
            _unsavedExitMessageText.TextWrapping = TextWrapping.Wrap;

            var saveBtn = CreateToolbarBtn("Save Unsaved Work", () => ApplyUnsavedExitDecision(UnsavedExitDecision.Save, closeWindow: true), accent: true);
            saveBtn.Padding = new Thickness(12, 6);

            var discardBtn = CreateToolbarBtn("Discard Unsaved Work", () => ApplyUnsavedExitDecision(UnsavedExitDecision.Discard, closeWindow: true));
            discardBtn.Padding = new Thickness(12, 6);

            var cancelBtn = CreateToolbarBtn("Cancel", () => ApplyUnsavedExitDecision(UnsavedExitDecision.Cancel, closeWindow: false));
            cancelBtn.Padding = new Thickness(12, 6);

            var card = new Border
            {
                Width = 480,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#0078D4")),
                Padding = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Unsaved Work — NoteDraw",
                            FontSize = 15,
                            FontWeight = FontWeight.Bold
                        },
                        _unsavedExitMessageText,
                        new WrapPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Children =
                            {
                                saveBtn.Also(b => b.Margin = new Thickness(0, 0, 8, 4)),
                                discardBtn.Also(b => b.Margin = new Thickness(0, 0, 8, 4)),
                                cancelBtn.Also(b => b.Margin = new Thickness(0, 0, 0, 4))
                            }
                        }
                    }
                }
            };

            _unsavedExitOverlay.IsVisible = false;
            _unsavedExitOverlay.Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
            _unsavedExitOverlay.Child = card;
        }

        private void BuildGitOverlay()
        {
            _gitBranchBadge.FontSize = 12;
            _gitBranchBadge.FontWeight = FontWeight.SemiBold;
            _gitBranchBadge.VerticalAlignment = VerticalAlignment.Center;

            _gitRemoteLabel.FontSize = 11.5;
            _gitRemoteLabel.Opacity = 0.8;
            _gitRemoteLabel.VerticalAlignment = VerticalAlignment.Center;

            _gitCommitMessageBox.PlaceholderText = "Commit message (e.g. Update vault notes)...";
            _gitCommitMessageBox.FontSize = 12.5;
            _gitCommitMessageBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    RunGitCommit(_gitCommitMessageBox.Text);
                    e.Handled = true;
                }
            };

            _gitChangesStack.Spacing = 4;
            _gitCommitsStack.Spacing = 3;

            _gitOutputBox.IsReadOnly = true;
            _gitOutputBox.AcceptsReturn = true;
            _gitOutputBox.TextWrapping = TextWrapping.Wrap;
            _gitOutputBox.FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace");
            _gitOutputBox.FontSize = 11.5;
            _gitOutputBox.MinHeight = 100;
            _gitOutputBox.MaxHeight = 160;
            _gitOutputBox.Text = "Git ready. Initialize a repository or commit vault changes below.";

            var closeBtn = CreateToolbarBtn("Close (Esc)", () => _gitOverlay.IsVisible = false);
            var refreshBtn = CreateToolbarBtn("↻ Refresh", () => RefreshGitStatus());
            var initBtn = CreateToolbarBtn("🌱 Init Repo", RunGitInit);
            var branchBtn = CreateToolbarBtn("🌿 Branch", PromptGitBranch);
            var remoteBtn = CreateToolbarBtn("🔗 Remote", PromptGitRemote);

            var topHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            topHeader.Children.Add(new TextBlock
            {
                Text = "🔀 Git Source Control",
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var headerActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { initBtn, branchBtn, remoteBtn, refreshBtn, closeBtn }
            };
            Grid.SetColumn(headerActions, 1);
            topHeader.Children.Add(headerActions);

            var statusMetaBar = new WrapPanel { Orientation = Orientation.Horizontal };
            statusMetaBar.Children.Add(_gitBranchBadge.Also(b => b.Margin = new Thickness(0, 0, 14, 0)));
            statusMetaBar.Children.Add(_gitRemoteLabel);

            var commitAllBtn = CreateToolbarBtn("✓ Commit All", () => RunGitCommit(_gitCommitMessageBox.Text), accent: true);
            var commitPushBtn = CreateToolbarBtn("⚡ Commit & Push", () => RunGitCommitAndPush(_gitCommitMessageBox.Text), accent: true);
            var pullBtn = CreateToolbarBtn("⬇ Pull", RunGitPull);
            var pushBtn = CreateToolbarBtn("⬆ Push", RunGitPush);

            var commitActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children = { commitAllBtn, commitPushBtn, pullBtn, pushBtn }
            };
            var commitRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 8
            };
            commitRow.Children.Add(_gitCommitMessageBox);
            Grid.SetColumn(commitActions, 1);
            commitRow.Children.Add(commitActions);

            var changesPanel = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = "Changed Files", FontSize = 12, FontWeight = FontWeight.SemiBold },
                    new ScrollViewer { Height = 150, Content = _gitChangesStack }
                }
            };

            var commitsPanel = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = "Recent Commits", FontSize = 12, FontWeight = FontWeight.SemiBold },
                    new ScrollViewer { Height = 150, Content = _gitCommitsStack }
                }
            };

            var splitColumns = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                ColumnSpacing = 12
            };
            splitColumns.Children.Add(changesPanel);
            Grid.SetColumn(commitsPanel, 1);
            splitColumns.Children.Add(commitsPanel);

            var card = new Border
            {
                Width = 760,
                MaxWidth = 860,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#0078D4")),
                Padding = new Thickness(18),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        topHeader,
                        statusMetaBar,
                        commitRow,
                        splitColumns,
                        new TextBlock { Text = "Git Output & Diff", FontSize = 11.5, FontWeight = FontWeight.SemiBold },
                        _gitOutputBox
                    }
                }
            };

            _gitOverlay.IsVisible = false;
            _gitOverlay.Background = new SolidColorBrush(Color.FromArgb(145, 0, 0, 0));
            _gitOverlay.Child = card;
            _gitOverlay.PointerPressed += (_, e) =>
            {
                if (ReferenceEquals(e.Source, _gitOverlay))
                {
                    _gitOverlay.IsVisible = false;
                    e.Handled = true;
                }
            };
        }

        public void OpenGitOverlay()
        {
            RefreshGitStatus();
            _gitOverlay.IsVisible = true;
            _gitCommitMessageBox.Focus();
        }

        private void RefreshGitStatus(string? outputOverride = null)
        {
            _lastGitStatus = _git.GetStatus(_vault.VaultPath);
            if (!_lastGitStatus.IsGitAvailable)
            {
                _statusGitText.Text = "Git: CLI missing";
                _gitRibbonStatusLabel.Text = "Git CLI not detected on PATH";
            }
            else if (!_lastGitStatus.IsRepository)
            {
                _statusGitText.Text = "Git: No repo";
                _gitRibbonStatusLabel.Text = "Not a Git repo yet (click Init Repo)";
            }
            else
            {
                int changedCount = _lastGitStatus.Changes.Count;
                string badge = changedCount > 0
                    ? $"🌿 {_lastGitStatus.BranchName} ({changedCount} changed)"
                    : $"🌿 {_lastGitStatus.BranchName} ✓";
                _statusGitText.Text = badge;
                _gitRibbonStatusLabel.Text = badge;
            }

            if (_gitOverlay.IsVisible || outputOverride is not null)
                RefreshGitOverlayContent(outputOverride);
        }

        private void RefreshGitOverlayContent(string? outputOverride = null)
        {
            if (!_lastGitStatus.IsGitAvailable)
            {
                _gitBranchBadge.Text = "Git CLI not found on PATH";
                _gitRemoteLabel.Text = string.Empty;
            }
            else if (!_lastGitStatus.IsRepository)
            {
                _gitBranchBadge.Text = $"No Git repository in {_vault.VaultPath}";
                _gitRemoteLabel.Text = "Click 'Init Repo' to start tracking this vault.";
            }
            else
            {
                _gitBranchBadge.Text = $"🌿 Branch: {_lastGitStatus.BranchName}";
                _gitRemoteLabel.Text = string.IsNullOrWhiteSpace(_lastGitStatus.RemoteUrl)
                    ? "Remote: (none configured)"
                    : $"Remote: {_lastGitStatus.RemoteUrl}";
            }

            _gitChangesStack.Children.Clear();
            if (_lastGitStatus.Changes.Count == 0)
            {
                _gitChangesStack.Children.Add(new TextBlock
                {
                    Text = _lastGitStatus.IsRepository ? "Working tree clean — no pending changes." : "No repository initialized.",
                    FontSize = 11.5,
                    Opacity = 0.7
                });
            }
            else
            {
                foreach (var change in _lastGitStatus.Changes)
                {
                    var captured = change;
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 5 };
                    row.Children.Add(new TextBlock
                    {
                        Text = $"[{captured.StatusCode}]",
                        FontSize = 11,
                        FontWeight = FontWeight.Bold,
                        Foreground = AccentBrush(),
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    var nameTb = new TextBlock
                    {
                        Text = captured.RelativePath,
                        FontSize = 11.5,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    ToolTip.SetTip(nameTb, $"{captured.StatusDescription}: {captured.RelativePath}");
                    Grid.SetColumn(nameTb, 1);
                    row.Children.Add(nameTb);

                    var diffBtn = CreateToolbarBtn("Diff", () =>
                    {
                        _gitOutputBox.Text = _git.GetDiff(_vault.VaultPath, captured.RelativePath);
                    });
                    diffBtn.Padding = new Thickness(5, 1);
                    Grid.SetColumn(diffBtn, 2);
                    row.Children.Add(diffBtn);

                    string fullFile = Path.Combine(_vault.VaultPath, captured.RelativePath);
                    if (File.Exists(fullFile) &&
                        (fullFile.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                         fullFile.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase) ||
                         IsSupportedImageExtension(fullFile)))
                    {
                        var openBtn = CreateToolbarBtn("Open", () =>
                        {
                            _gitOverlay.IsVisible = false;
                            OpenFileInTab(fullFile);
                        });
                        openBtn.Padding = new Thickness(5, 1);
                        Grid.SetColumn(openBtn, 3);
                        row.Children.Add(openBtn);
                    }

                    _gitChangesStack.Children.Add(row);
                }
            }

            _gitCommitsStack.Children.Clear();
            if (_lastGitStatus.RecentCommits.Count == 0)
            {
                _gitCommitsStack.Children.Add(new TextBlock
                {
                    Text = "No commits yet.",
                    FontSize = 11.5,
                    Opacity = 0.7
                });
            }
            else
            {
                foreach (var commit in _lastGitStatus.RecentCommits)
                {
                    var stack = new StackPanel { Spacing = 1, Margin = new Thickness(0, 1, 0, 4) };
                    stack.Children.Add(new TextBlock
                    {
                        Text = $"{commit.ShortHash} • {commit.Subject}",
                        FontSize = 11.5,
                        FontWeight = FontWeight.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    stack.Children.Add(new TextBlock
                    {
                        Text = $"{commit.Author} • {commit.RelativeTime}",
                        FontSize = 10.5,
                        Opacity = 0.7
                    });
                    _gitCommitsStack.Children.Add(stack);
                }
            }

            if (outputOverride is not null)
                _gitOutputBox.Text = outputOverride;
        }

        private void RunGitInit()
        {
            var res = _git.InitRepository(_vault.VaultPath);
            RefreshGitStatus(res.Output);
            _statusSaveText.Text = res.Success ? "✓ Git repository initialized" : "⚠ Git init failed";
        }

        private void PromptGitCommitAll()
        {
            ShowPrompt("Git Commit All Changes", $"Update notes ({DateTime.Now:yyyy-MM-dd HH:mm})", msg =>
            {
                RunGitCommit(msg);
            });
        }

        private void PromptGitCommitAndPush()
        {
            ShowPrompt("Git Commit & Push (Sync)", $"Sync notes ({DateTime.Now:yyyy-MM-dd HH:mm})", msg =>
            {
                RunGitCommitAndPush(msg);
            });
        }

        private void RunGitCommit(string? message)
        {
            if (_autoSaveEnabled || HasUnsavedDocuments)
                SaveAllDirtyTabs();

            var res = _git.StageAndCommitAll(_vault.VaultPath, message ?? string.Empty);
            if (res.Success)
                _gitCommitMessageBox.Text = string.Empty;
            RefreshGitStatus(res.Output);
            _statusSaveText.Text = res.Success ? "✓ Git commit succeeded" : "⚠ Git commit issue (see Git panel)";
        }

        private void RunGitCommitAndPush(string? message)
        {
            if (_autoSaveEnabled || HasUnsavedDocuments)
                SaveAllDirtyTabs();

            var res = _git.CommitAndPush(_vault.VaultPath, message ?? string.Empty);
            if (res.Success)
                _gitCommitMessageBox.Text = string.Empty;
            RefreshGitStatus(res.Output);
            _statusSaveText.Text = res.Success ? "✓ Git commit & push succeeded" : "⚠ Git sync issue (see Git panel)";
        }

        private void RunGitPull()
        {
            var res = _git.Pull(_vault.VaultPath);
            RefreshVaultTree();
            RefreshGitStatus(res.Output);
            _statusSaveText.Text = res.Success ? "✓ Git pull completed" : "⚠ Git pull failed (see Git panel)";
        }

        private void RunGitPush()
        {
            var res = _git.Push(_vault.VaultPath);
            RefreshGitStatus(res.Output);
            _statusSaveText.Text = res.Success ? "✓ Git push completed" : "⚠ Git push failed (see Git panel)";
        }

        private void PromptGitBranch()
        {
            string defaultBranch = string.IsNullOrWhiteSpace(_lastGitStatus.BranchName) ? "main" : _lastGitStatus.BranchName;
            ShowPrompt("Switch or Create Git Branch", defaultBranch, branch =>
            {
                var res = _git.SwitchOrCreateBranch(_vault.VaultPath, branch, createNew: false);
                RefreshGitStatus(res.Output);
                _statusSaveText.Text = res.Success ? $"✓ Branch: {branch}" : "⚠ Branch switch failed";
            });
        }

        private void PromptGitRemote()
        {
            string currentRemote = string.IsNullOrWhiteSpace(_lastGitStatus.RemoteUrl)
                ? "https://github.com/user/vault-notes.git"
                : _lastGitStatus.RemoteUrl;
            ShowPrompt("Set Git 'origin' Remote URL", currentRemote, url =>
            {
                var res = _git.SetRemoteOrigin(_vault.VaultPath, url);
                RefreshGitStatus(res.Output);
                _statusSaveText.Text = res.Success ? "✓ Git remote updated" : "⚠ Failed to set remote";
            });
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

            string listing = dirtyNames.Count > 0
                ? string.Join("\n", dirtyNames)
                : "• Active document";

            _unsavedExitMessageText.Text =
                $"You have unsaved changes (Auto-Save is turned off):\n\n{listing}\n\nDo you want to save your changes before exiting?";
            _unsavedExitOverlay.IsVisible = true;
        }

        public void ApplyUnsavedExitDecision(UnsavedExitDecision decision, bool closeWindow = false)
        {
            switch (decision)
            {
                case UnsavedExitDecision.Save:
                    SaveAllDirtyTabs();
                    _unsavedExitOverlay.IsVisible = false;
                    _isConfirmedExit = true;
                    if (closeWindow)
                        Close();
                    break;

                case UnsavedExitDecision.Discard:
                    foreach (var tab in _openTabs)
                        tab.IsDirty = false;
                    _unsavedExitOverlay.IsVisible = false;
                    _isConfirmedExit = true;
                    RefreshTabStrip();
                    UpdateStatusBar();
                    if (closeWindow)
                        Close();
                    break;

                case UnsavedExitDecision.Cancel:
                default:
                    _unsavedExitOverlay.IsVisible = false;
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

        private void WireEvents()
        {
            _viewState.Changed += (_, _) => RefreshViewLayout();

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

            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
                return;

            // Allow small utility inputs to keep standard single-line editing shortcuts
            if (_vaultSearchBox.IsFocused || _quickSwitcherInput.IsFocused || _promptInput.IsFocused || _liveLatexScratchpad.IsFocused || _gitCommitMessageBox.IsFocused)
                return;

            if (!isTextInputFocused &&
                !hasModalOpen &&
                _isVaultExplorerFocused &&
                !e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
                !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                if (e.Key == Key.C && _selectedVaultPaths.Count > 0)
                {
                    CopySelectedVaultFiles();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.X && _selectedVaultPaths.Count > 0)
                {
                    CutSelectedVaultFiles();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.V && _clipboardVaultPaths.Count > 0)
                {
                    PasteVaultFiles(GetDefaultPasteTargetDirectory());
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.A)
                {
                    SelectAllVaultFiles();
                    e.Handled = true;
                    return;
                }
            }

            if (!isTextInputFocused &&
                !hasModalOpen &&
                !e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
                !e.KeyModifiers.HasFlag(KeyModifiers.Alt) &&
                GetActiveCanvasWorkspaceForClipboard() is { } activeCanvasWs)
            {
                if (e.Key == Key.C)
                {
                    _ = activeCanvasWs.CopyCanvasToClipboardAsync();
                    _statusSaveText.Text = "✓ Canvas copied to clipboard";
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.V)
                {
                    _ = activeCanvasWs.PasteImageFromClipboardAsync(activeCanvasWs.Canvas.HoverCanvasPosition, allowFilePickerFallback: false);
                    _statusSaveText.Text = "✓ Pasted image into canvas";
                    e.Handled = true;
                    return;
                }
            }

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

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
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

                    var folderRow = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto")
                    };
                    folderRow.Children.Add(toggleBtn);

                    bool isVaultSubfolder = entry.FullPath.StartsWith(vaultPrefix, StringComparison.OrdinalIgnoreCase);
                    if (isVaultSubfolder)
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
                    _vaultListStack.Children.Add(folderBorder);
                    if (isCollapsed)
                        collapsedDepth = entry.Depth;
                    continue;
                }

                string filePath = entry.FullPath;
                string icon = entry.Kind switch
                {
                    VaultEntryKind.DrawingFile => "🎨",
                    VaultEntryKind.ImageFile => "🖼",
                    _ => "📄"
                };
                var openBtn = new Button
                {
                    Content = $"{icon} {entry.Name}",
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
                    ToolTip.SetTip(openBtn, "Click to preview • Ctrl/Shift+Click multi-select • Drag into document");
                    openBtn.AddHandler(PointerPressedEvent, (_, pe) =>
                    {
                        if (pe.GetCurrentPoint(openBtn).Properties.IsLeftButtonPressed &&
                            !pe.KeyModifiers.HasFlag(KeyModifiers.Control) &&
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

                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto")
                };
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
                _vaultListStack.Children.Add(rowBorder);
            }

            UpdateVaultSelectionVisuals();
        }

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
                    (pe.KeyModifiers.HasFlag(KeyModifiers.Control) || pe.KeyModifiers.HasFlag(KeyModifiers.Shift)))
                {
                    _suppressNextVaultClickPath = entryPath;
                    SelectVaultEntry(
                        entryPath,
                        ctrlHeld: pe.KeyModifiers.HasFlag(KeyModifiers.Control),
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
                Header = selected.Count > 1 ? $"✂ Cut ({selected.Count} items)    Ctrl+X" : "✂ Cut    Ctrl+X",
                IsEnabled = hasSelection
            };
            cutItem.Click += (_, _) => CutSelectedVaultFiles();
            menu.Items.Add(cutItem);

            var copyItem = new MenuItem
            {
                Header = selected.Count > 1 ? $"📋 Copy ({selected.Count} items)    Ctrl+C" : "📋 Copy    Ctrl+C",
                IsEnabled = hasSelection
            };
            copyItem.Click += (_, _) => CopySelectedVaultFiles();
            menu.Items.Add(copyItem);

            int validClipCount = _clipboardVaultPaths.Count(p => File.Exists(p) || Directory.Exists(p));
            var pasteItem = new MenuItem
            {
                Header = validClipCount > 1 ? $"📥 Paste ({validClipCount} items)    Ctrl+V" : "📥 Paste    Ctrl+V",
                IsEnabled = hasClipboard
            };
            pasteItem.Click += (_, _) => PasteVaultFiles(pasteTarget);
            menu.Items.Add(pasteItem);

            menu.Items.Add(new Separator());

            var selectAllItem = new MenuItem { Header = "☑ Select All Files    Ctrl+A" };
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
                _statusSaveText.Text = $"✓ Opened canvas in separate tab: {Path.GetFileName(savedPath)}";
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
                _statusSaveText.Text = $"✓ Opened inline canvas in separate tab: {Path.GetFileName(savedPath)}";
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
                RefreshOutlineAndTags(string.Empty);
                UpdateLiveLatexFromCursor();
                RefreshViewLayout();
                return;
            }

            if (tab.IsDrawing)
            {
                _preferDrawingUndo = true;
                _previewHeaderTitle.Text = $"VECTOR DRAWING CANVAS — {tab.Title}";
                _standaloneDrawing.LoadDocument(DrawingDocument.FromJson(tab.Content));
                RefreshOutlineAndTags(string.Empty);
            }
            else if (tab.IsImage)
            {
                _preferDrawingUndo = false;
                _previewHeaderTitle.Text = $"IMAGE PREVIEW — {tab.Title}";
                RefreshOutlineAndTags(string.Empty);
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
                RefreshOutlineAndTags(tab.Content);
                UpdateLiveLatexFromCursor();
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
            RefreshGitStatus();

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

        private void RefreshOutlineAndTags(string markdown)
        {
            _outlineStack.Children.Clear();
            var outline = VaultService.ExtractOutline(markdown);
            if (outline.Count == 0)
            {
                _outlineStack.Children.Add(new TextBlock
                {
                    Text = "No headings in note.",
                    FontSize = 11.5,
                    Opacity = 0.65
                });
            }
            else
            {
                foreach (var item in outline)
                {
                    var itemBtn = new Button
                    {
                        Content = item.Title,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        FontSize = item.Level == 1 ? 12.5 : 11.5,
                        FontWeight = item.Level == 1 ? FontWeight.SemiBold : FontWeight.Normal,
                        Padding = new Thickness(6 + (item.Level - 1) * 10, 3),
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0)
                    };
                    int targetLine = item.LineNumber;
                    itemBtn.Click += (_, _) => JumpEditorToLine(targetLine);
                    _outlineStack.Children.Add(itemBtn);
                }
            }

            _tagsWrap.Children.Clear();
            var tags = VaultService.ExtractTags(markdown);
            foreach (string tag in tags)
            {
                string t = tag;
                var tagBtn = new Button
                {
                    Content = t,
                    FontSize = 11,
                    Padding = new Thickness(6, 2),
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(9),
                    Background = ThemeConfigService.ParseBrush(_themeConfig.CurrentPalette.TagTintColor, _isDarkTheme ? "#233B23" : "#DFF6DD"),
                    Foreground = ThemeConfigService.ParseBrush(_themeConfig.CurrentPalette.TagColor, _isDarkTheme ? "#6CCB5F" : "#107C10")
                };
                tagBtn.Click += (_, _) =>
                {
                    _viewState.OpenView(WorkspaceViewKind.VaultExplorer);
                    _vaultSearchBox.Text = t.TrimStart('#');
                };
                _tagsWrap.Children.Add(tagBtn);
            }
        }

        private void UpdateLiveLatexFromCursor()
        {
            if (_activeTab is null || _activeTab.IsDrawing)
            {
                _statusLatexText.Text = "LaTeX: Drawing mode";
                return;
            }

            string text = _editorTextBox.Text ?? string.Empty;
            int caret = Math.Clamp(_editorTextBox.CaretIndex, 0, text.Length);
            var ctx = LatexLiveDetector.DetectAtCursor(text, caret);

            if (ctx.IsFound)
            {
                string closedNote = ctx.IsClosed ? "" : " • typing...";
                string badge = $"Active: {ctx.BlockKind} (Lines {ctx.StartLine}–{ctx.EndLine}){closedNote}";
                RenderLiveLatexBox(ctx.LatexCode, badge, ctx.IsDisplayMode, ctx.IsClosed);

                _syncingScratchpad = true;
                _liveLatexScratchpad.Text = ctx.LatexCode;
                _syncingScratchpad = false;
                _statusLatexText.Text = $"LaTeX: {ctx.BlockKind}";
            }
            else
            {
                _liveLatexBadge.Text = "Move cursor inside ```latex, ```math, $$...$$, or $...$ to live-preview while typing:";
                if (string.IsNullOrWhiteSpace(_liveLatexScratchpad.Text))
                {
                    _liveLatexControl.LatexSource = "\\int_0^\\infty e^{-x^2}\\,dx = \\frac{\\sqrt{\\pi}}{2}";
                    _liveLatexControl.IsDisplayMode = true;
                    _liveLatexDiag.IsVisible = false;
                }
                _statusLatexText.Text = "LaTeX: Ready";
            }
        }

        private void RenderLiveLatexBox(string latexCode, string badgeText, bool isDisplayMode, bool isClosed)
        {
            _liveLatexBadge.Text = badgeText;
            _liveLatexControl.IsDisplayMode = isDisplayMode;
            _liveLatexControl.LatexSource = latexCode;

            string diag = _liveLatexControl.DiagnosticsText;
            if (!isClosed && string.IsNullOrEmpty(diag))
                diag = "Live preview active — waiting for closing delimiter";

            _liveLatexDiag.Text = diag;
            _liveLatexDiag.IsVisible = !string.IsNullOrEmpty(diag);
        }

        private void UpdateStatusBar()
        {
            if (_activeTab is null)
            {
                _statusSaveText.Text = "No document open";
                _statusCountsText.Text = "";
                _statusCaretText.Text = "";
            }
            else
            {
                _statusSaveText.Text = _activeTab.IsDirty
                    ? "● Unsaved changes (Ctrl+S)"
                    : (_autoSaveEnabled ? "✓ Auto-saved" : "✓ Saved");
                if (_activeTab.IsDrawing)
                {
                    int strokeCount = _standaloneDrawing.Canvas.Document.Strokes.Count;
                    _statusCountsText.Text = $"Strokes: {strokeCount}";
                    _statusCaretText.Text = "Canvas Mode";
                }
                else
                {
                    string text = _activeTab.Content ?? string.Empty;
                    int words = string.IsNullOrWhiteSpace(text)
                        ? 0
                        : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                    _statusCountsText.Text = $"{words} words • {text.Length} chars";

                    if (_showRawSourceInUnifiedWindow)
                    {
                        string rawText = _editorTextBox.Text ?? string.Empty;
                        int caret = Math.Clamp(_editorTextBox.CaretIndex, 0, rawText.Length);
                        var (ln, col) = GetLineAndColumn(rawText, caret);
                        _editorCursorLabel.Text = $"Ln {ln}, Col {col}";
                        _statusCaretText.Text = $"Ln {ln}, Col {col}";
                    }
                    else if (_markdownPreview.ActiveCursorLineZeroBased < 0)
                    {
                        _statusCaretText.Text = "Live Editor";
                    }
                }
            }

            _statusViewsText.Text = $"Views: {_viewState.VisibleCount}/{_viewState.TotalCount} (View & Mode → View)";
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
            UpdateLiveLatexFromCursor();
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
            _statusSaveText.Text = $"✓ Canvas saved to canvas/{saved.FileName}";
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
            _statusSaveText.Text = "✓ Image copied to clipboard";
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
                    _statusSaveText.Text = "✓ Pasted image into canvas";
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
                    _statusSaveText.Text = $"✓ Pasted image saved to image/{savedFileName}";
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

            RefreshOutlineAndTags(updated);
            UpdateLiveLatexFromCursor();
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

        private void PromptCreateNote()
        {
            ShowPrompt("Create New Markdown Note", "New Note", title =>
            {
                var entry = _vault.CreateNote(title);
                RefreshVaultTree();
                OpenFileInTab(entry.FullPath);
            });
        }

        private void PromptCreateFolder()
        {
            ShowPrompt("Create New Vault Folder", "New Folder", folderName =>
            {
                _vault.CreateFolder(folderName);
                RefreshVaultTree();
            });
        }

        private void PromptCreateNoteInFolder(string subfolderRelative)
        {
            ShowPrompt($"Create New Note in '{subfolderRelative}'", "New Note", title =>
            {
                var entry = _vault.CreateNote(title, subfolderRelative: subfolderRelative);
                _collapsedVaultFolders.Remove(Path.GetFullPath(Path.Combine(_vault.VaultPath, subfolderRelative)));
                RefreshVaultTree();
                OpenFileInTab(entry.FullPath);
            });
        }

        private void PromptCreateDrawing()
        {
            ShowPrompt("Create New Drawing (.ndraw)", "New Sketch", title =>
            {
                var entry = _vault.CreateDrawing(title);
                RefreshVaultTree();
                OpenFileInTab(entry.FullPath);
            });
        }

        private void ShowPrompt(string title, string defaultText, Action<string> onSubmit)
        {
            _promptTitle.Text = title;
            _promptInput.Text = defaultText;
            _promptCallback = onSubmit;
            _promptOverlay.IsVisible = true;
            _promptInput.Focus();
            _promptInput.SelectAll();
        }

        private void SubmitPrompt()
        {
            string value = _promptInput.Text?.Trim() ?? string.Empty;
            _promptOverlay.IsVisible = false;
            if (!string.IsNullOrEmpty(value))
                _promptCallback?.Invoke(value);
        }

        private void OpenQuickSwitcher()
        {
            _quickSwitcherInput.Text = string.Empty;
            PopulateQuickSwitcherResults();
            _quickSwitcherOverlay.IsVisible = true;
            _quickSwitcherInput.Focus();
        }

        private void PopulateQuickSwitcherResults()
        {
            _quickSwitcherResults.Children.Clear();
            var entries = _vault.GetEntries(_quickSwitcherInput.Text)
                .Where(e => e.Kind != VaultEntryKind.Folder)
                .Take(15);

            foreach (var entry in entries)
            {
                string icon = entry.Kind == VaultEntryKind.DrawingFile ? "🎨" : "📄";
                var btn = new Button
                {
                    Content = $"{icon} {entry.RelativePath}",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    FontSize = 13,
                    Padding = new Thickness(10, 7),
                    CornerRadius = new CornerRadius(6)
                };
                btn.Click += (_, _) =>
                {
                    _quickSwitcherOverlay.IsVisible = false;
                    OpenFileInTab(entry.FullPath);
                };
                _quickSwitcherResults.Children.Add(btn);
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

    internal static class ControlExtensions
    {
        public static T Also<T>(this T obj, Action<T> action)
        {
            action(obj);
            return obj;
        }
    }
}
