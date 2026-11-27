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
        private bool _isOpeningVaultPicker;
        private OpenDocumentTab? _activeTab;
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

        // Main layout containers
        private readonly Grid _mainBodyGrid = new();
        private readonly Grid _sidebarGrid = new();

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

        // Mediator & UI colleagues
        private WorkspaceMediator _mediator = null!;
        private TopBarPanel _topBarPanel = null!;
        private RibbonBarPanel _ribbonBarPanel = null!;
        private VaultExplorerPanel _vaultExplorerPanel = null!;
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
        public IReadOnlyCollection<string> SelectedVaultPaths => _vaultExplorerPanel.SelectedPaths;
        public IReadOnlyList<string> VaultClipboardPaths => _vaultExplorerPanel.ClipboardPaths;
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
        internal Menu TopMenuBar => _topBarPanel.MenuBar;
        internal IReadOnlyCollection<string> RibbonTabLabels => _ribbonBarPanel.TabLabels;
        internal void SelectRibbonTab(RibbonTabKind tab) => _ribbonBarPanel.SelectTab(tab);
        internal string CurrentVaultPath => _vault.VaultPath;
        public void InsertCanvasInDocument() => InsertNewDrawingBlockAtCaret();
        public void SelectVaultEntry(string fullPath, bool ctrlHeld = false, bool shiftHeld = false)
            => _vaultExplorerPanel.SelectEntry(fullPath, ctrlHeld, shiftHeld);
        public void SelectAllVaultFiles() => _vaultExplorerPanel.SelectAllFiles();
        public void CopySelectedVaultFiles() => _vaultExplorerPanel.CopySelectedFiles();
        public void CutSelectedVaultFiles() => _vaultExplorerPanel.CutSelectedFiles();
        public IReadOnlyList<string> PasteVaultFiles(string? targetDirectoryFullPath = null)
            => _vaultExplorerPanel.PasteFiles(targetDirectoryFullPath);
        public void DeleteSelectedVaultFiles() => _vaultExplorerPanel.DeleteSelectedFiles();
        internal ContextMenu CreateVaultContextMenu(string? entryFullPath, VaultEntryKind? entryKind)
            => _vaultExplorerPanel.CreateContextMenu(entryFullPath, entryKind);

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
            _ribbonBarPanel.RefreshContent();
            RefreshViewLayout();
            _mediator.RequestRefreshVaultTree();
            _mediator.RefreshGitStatus();
            OpenStarterDocument();
        }

        private void InitializeMediatorAndColleagues()
        {
            var callbacks = new WorkspaceHostCallbacks
            {
                GetCurrentVaultPath = () => _vault.VaultPath,
                GetActiveDocumentFilePath = () => _activeTab?.FilePath,
                GetVaultEntries = filter => _vault.GetEntries(filter),
                GetAutoSaveEnabled = () => _autoSaveEnabled,
                GetHasUnsavedDocuments = () => HasUnsavedDocuments,
                GetCurrentPalette = () => _themeConfig.CurrentPalette,
                GetIsDarkTheme = () => _isDarkTheme,
                IsViewVisible = _viewState.IsVisible,
                OpenFileInTab = OpenFileInTab,
                JumpEditorToLine = JumpEditorToLine,
                InsertLatexSnippet = tex => InsertUtilitySnippet($"\n```latex\n{tex}\n```\n"),
                InsertUtilitySnippet = snippet => InsertUtilitySnippet(snippet),
                OpenView = view => _viewState.OpenView(view),
                CloseView = view => _viewState.CloseView(view),
                ToggleWorkspaceView = targetView =>
                {
                    bool next = _viewState.ToggleView(targetView);
                    if (next && targetView == WorkspaceViewKind.EditorPane)
                        _showRawSourceInUnifiedWindow = true;
                    else if (next && targetView == WorkspaceViewKind.PreviewPane)
                        _showRawSourceInUnifiedWindow = false;
                    RefreshViewLayout();
                },
                ResetAllViews = () => _viewState.ResetAllViews(),
                SetUnifiedWindowMode = SetUnifiedWindowMode,
                OpenVaultFolder = async () => await OpenVaultFolderAsync(),
                SaveActiveTab = SaveActiveTab,
                SaveAllDirtyTabs = SaveAllDirtyTabs,
                RequestCloseWindow = Close,
                PerformUndo = PerformUndo,
                PerformRedo = PerformRedo,
                PerformDrawingUndo = () => PerformDrawingUndo(),
                PerformDrawingRedo = () => PerformDrawingRedo(),
                InsertCanvasInDocument = InsertNewDrawingBlockAtCaret,
                InsertCodeBlock = InsertCodeBlockAtCaret,
                PasteImageWithPicker = async () => await PasteImageIntoDocumentAsync(allowFilePickerFallback: true),
                ToggleAutoSave = () => SetAutoSaveEnabled(!_autoSaveEnabled),
                ToggleTheme = () => ApplyTheme(!_isDarkTheme, persistToConfig: true),
                OpenDarkThemeConfig = OpenDarkThemeConfigInTab,
                OpenLightThemeConfig = OpenLightThemeConfigInTab,
                LoadCustomThemeFile = async () => await LoadCustomThemeFileAsync(),
                ExportActiveDrawing = async isSvg =>
                {
                    if (_activeTab?.IsDrawing == true)
                        await _standaloneDrawing.ExportImageAsync(isSvg);
                },
                CloseOpenTabsMatchingPath = CloseOpenTabsMatchingPath,
                RemapOpenTabsAfterMove = RemapOpenTabsAfterMove,
                RefreshTabStrip = RefreshTabStrip,
                RefreshStatusBar = UpdateStatusBar,
                BeginVaultImageDrag = BeginVaultImageDrag,
                ApplyUnsavedExitDecision = ApplyUnsavedExitDecision
            };

            _mediator = new WorkspaceMediator(callbacks);
            _topBarPanel = new TopBarPanel(_mediator);
            _ribbonBarPanel = new RibbonBarPanel(_mediator);
            _vaultExplorerPanel = new VaultExplorerPanel(_mediator, _vault, _assets);
            _statusBarPanel = new StatusBarPanel(_mediator);
            _outlineAndTagsPanel = new OutlineAndTagsPanel(_mediator);
            _liveLatexPreviewPanel = new LiveLatexPreviewPanel(_mediator);

            _mediator.RegisterChromeAndVault(_topBarPanel, _ribbonBarPanel, _vaultExplorerPanel);
            _mediator.RegisterPanels(_statusBarPanel, _outlineAndTagsPanel, _liveLatexPreviewPanel);
            _mediator.RegisterOverlays(
                new UnsavedExitOverlay(_mediator),
                new PromptDialogOverlay(_mediator),
                new QuickSwitcherOverlay(_mediator),
                new GitOverlayPanel(_mediator, _git),
                new SyntaxGuideOverlay());
        }

        private void BuildWorkspaceLayout()
        {
            var outerGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto")
            };

            // 1. Top Header Bar
            Grid.SetRow(_topBarPanel, 0);
            outerGrid.Children.Add(_topBarPanel);

            // 2. Top Tabbed Toolbar Ribbon (Drawing Tools | Git)
            Grid.SetRow(_ribbonBarPanel, 1);
            outerGrid.Children.Add(_ribbonBarPanel);

            // 3. Main Body (Sidebar + Center Workspace)
            _mainBodyGrid.ColumnDefinitions = new ColumnDefinitions("270,*");

            _sidebarGrid.RowDefinitions = new RowDefinitions("*,Auto");
            Grid.SetRow(_vaultExplorerPanel, 0);
            Grid.SetRow(_outlineAndTagsPanel, 1);
            _sidebarGrid.Children.Add(_vaultExplorerPanel);
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

            var header = WorkspaceUiFactory.CreateViewHeaderCustom(
                _previewHeaderTitle,
                WorkspaceViewKind.PreviewPane,
                _viewState.CloseView,
                rightBadge: rightControls,
                onCloseOverride: CloseUnifiedPane,
                accentBrush: AccentBrush());

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

            _vaultExplorerPanel.IsVisible = showVault;
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

            _mediator.NotifyViewLayoutUpdated();
            UpdateStatusBar();
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

            var chromeBg = ThemeConfigService.ParseBrush(palette.ChromeBackground, isDark ? "#1E1E1E" : "#F3F3F3");
            var panelBg = ThemeConfigService.ParseBrush(palette.PanelBackground, isDark ? "#252526" : "#FFFFFF");
            var border = ThemeConfigService.ParseBrush(palette.BorderColor, isDark ? "#3C3C3C" : "#D4D4D4");

            Background = panelBg;
            _tabsBarBorder.Background = chromeBg;
            _tabsBarBorder.BorderBrush = border;

            _editorPanel.Background = panelBg;
            _editorPanel.BorderBrush = border;
            _foldableEditor.ApplyThemePalette(palette, isDark, _themeConfig.Config.EditorFontSize, _themeConfig.Config.EditorFontFamily);

            _previewPanel.Background = panelBg;
            _markdownPreview.ApplyThemePalette(palette, isDark, _themeConfig.Config.PreviewFontSize);
            _standaloneDrawing.ApplyThemePalette(palette, isDark);

            _mediator.NotifyThemeChanged(palette, isDark);
            RefreshTabStrip();
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
            _vaultExplorerPanel.ResetStateForVaultSwitch();
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
            _mediator.NotifyAutoSaveChanged(_autoSaveEnabled);
            ApplyTheme(_themeConfig.IsDark, persistToConfig: false);
            _mediator.RefreshGitStatus();
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

        private IBrush AccentBrush()
            => WorkspaceUiFactory.ResolveAccentBrush(_themeConfig.CurrentPalette, _isDarkTheme);

        private IBrush InactiveTabBrush()
            => ThemeConfigService.ParseBrush(_themeConfig.CurrentPalette.CardBackground, _isDarkTheme ? "#2A2D2E" : "#E8E8E8");

        private IBrush ForegroundBrush()
            => WorkspaceUiFactory.ResolveForegroundBrush(_themeConfig.CurrentPalette, _isDarkTheme);

        private IBrush MutedBrush()
            => ThemeConfigService.ParseBrush(_themeConfig.CurrentPalette.MutedTextColor, _isDarkTheme ? "#9E9E9E" : "#616161");
    }
}
