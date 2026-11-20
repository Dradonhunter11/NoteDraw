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
        public TextUndoManager TextUndo { get; } = new();
    }

    internal enum RibbonTabKind
    {
        View,
        DrawingTools
    }

    public partial class MainWindow : Window
    {
        private readonly VaultService _vault = new();
        private readonly ThemeConfigService _themeConfig = new();
        private readonly DocumentAssetService _assets = new();
        private readonly ViewLayoutState _viewState = new();
        private Bitmap? _inAppCopiedBitmap;
        private readonly List<OpenDocumentTab> _openTabs = new();
        private readonly List<Button> _accentButtons = new();
        private readonly HashSet<string> _collapsedVaultFolders = new(StringComparer.OrdinalIgnoreCase);
        private OpenDocumentTab? _activeTab;
        private RibbonTabKind _activeRibbonTab = RibbonTabKind.View;
        private bool _isDarkTheme = true;
        private bool _suppressEditorTextChanged;
        private bool _suppressPreviewRebuild;
        private bool _preferDrawingUndo;
        private bool _showRawSourceInUnifiedWindow;

        // Top bar & ribbon controls
        private readonly Border _topBarBorder = new();
        private readonly TextBlock _vaultNameLabel = new();
        private readonly Button _topUndoBtn = new();
        private readonly Button _topRedoBtn = new();
        private readonly Button _themeBtn = new();
        private readonly Border _ribbonBorder = new();
        private readonly Dictionary<RibbonTabKind, Button> _ribbonTabButtons = new();
        private readonly Border _ribbonContentHost = new();
        private readonly Dictionary<WorkspaceViewKind, Button> _viewToggleButtons = new();

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
        private readonly TextBlock _statusViewsText = new();
        private readonly TextBlock _statusCaretText = new();
        private readonly TextBlock _statusLatexText = new();
        private readonly TextBlock _statusCountsText = new();

        // Modal / inline overlays (Syntax Guide Sub-Menu, Quick Switcher & Prompt)
        private readonly SyntaxGuideSubMenuControl _floatingSyntaxGuidePopup = new(maxListHeight: 470);
        private readonly Border _syntaxGuideOverlay = new();
        private readonly Border _quickSwitcherOverlay = new();
        private readonly TextBox _quickSwitcherInput = new();
        private readonly StackPanel _quickSwitcherResults = new();
        private readonly Border _promptOverlay = new();
        private readonly TextBlock _promptTitle = new();
        private readonly TextBox _promptInput = new();
        private Action<string>? _promptCallback;

        public MainWindow()
        {
            InitializeComponent();
            _assets.EnsureFoldersExist();
            RegisterAssetFoldersInVault();
            _themeConfig.Load(_vault.VaultPath);
            _isDarkTheme = _themeConfig.IsDark;
            BuildWorkspaceLayout();
            WireEvents();
            ApplyTheme(_isDarkTheme, persistToConfig: false);
            RefreshRibbonContent();
            RefreshViewLayout();
            RefreshVaultTree();
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

            _tabsBarBorder.Padding = new Thickness(8, 5);
            _tabsBarBorder.BorderThickness = new Thickness(0, 0, 0, 1);
            _tabsStrip.Orientation = Orientation.Horizontal;
            _tabsStrip.Spacing = 4;
            _tabsBarBorder.Child = new ScrollViewer
            {
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

            var rootContainer = new Grid();
            rootContainer.Children.Add(outerGrid);
            rootContainer.Children.Add(_syntaxGuideOverlay);
            rootContainer.Children.Add(_quickSwitcherOverlay);
            rootContainer.Children.Add(_promptOverlay);

            RootHost.Children.Add(rootContainer);
        }

        private Control BuildTopBar()
        {
            var left = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };

            left.Children.Add(new TextBlock
            {
                Text = "NoteDraw",
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });

            _vaultNameLabel.FontSize = 12;
            _vaultNameLabel.VerticalAlignment = VerticalAlignment.Center;
            _vaultNameLabel.Margin = new Thickness(6, 0);
            left.Children.Add(_vaultNameLabel);

            left.Children.Add(CreatePillButton("Open Vault...", async () => await OpenVaultFolderAsync()));
            left.Children.Add(CreatePillButton("+ Note", PromptCreateNote, accent: true));
            left.Children.Add(CreatePillButton("+ Drawing", PromptCreateDrawing, accent: true));
            left.Children.Add(CreatePillButton("+ Canvas in Doc", InsertNewDrawingBlockAtCaret, accent: true));
            left.Children.Add(CreatePillButton("📋 Paste Image", async () => await PasteImageIntoDocumentAsync(allowFilePickerFallback: true)));
            left.Children.Add(CreatePillButton("Save", SaveActiveTab));

            _topUndoBtn.Content = "↶ Undo";
            _topUndoBtn.FontSize = 12;
            _topUndoBtn.FontWeight = FontWeight.SemiBold;
            _topUndoBtn.Padding = new Thickness(10, 5);
            _topUndoBtn.CornerRadius = new CornerRadius(6);
            _topUndoBtn.Click += (_, _) => PerformUndo();
            ToolTip.SetTip(_topUndoBtn, "Undo (Ctrl+Z)");

            _topRedoBtn.Content = "↷ Redo";
            _topRedoBtn.FontSize = 12;
            _topRedoBtn.FontWeight = FontWeight.SemiBold;
            _topRedoBtn.Padding = new Thickness(10, 5);
            _topRedoBtn.CornerRadius = new CornerRadius(6);
            _topRedoBtn.Click += (_, _) => PerformRedo();
            ToolTip.SetTip(_topRedoBtn, "Redo (Ctrl+Y / Ctrl+Shift+Z)");

            left.Children.Add(_topUndoBtn);
            left.Children.Add(_topRedoBtn);

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            right.Children.Add(CreatePillButton("Unified Live View", () => SetUnifiedWindowMode(showRawSource: false)));
            right.Children.Add(CreatePillButton("Raw Source Mode", () => SetUnifiedWindowMode(showRawSource: true)));
            var helpBtn = CreatePillButton("?", ToggleSyntaxGuidePopup);
            helpBtn.FontWeight = FontWeight.Bold;
            helpBtn.FontSize = 13;
            helpBtn.MinWidth = 28;
            helpBtn.Padding = new Thickness(9, 4);
            ToolTip.SetTip(helpBtn, "Markdown & LaTeX Syntax Guide (F1)");
            right.Children.Add(helpBtn);
            right.Children.Add(CreatePillButton("Quick Switcher (Ctrl+P)", OpenQuickSwitcher));
            right.Children.Add(CreatePillButton("dark.json", OpenDarkThemeConfigInTab));
            right.Children.Add(CreatePillButton("light.json", OpenLightThemeConfigInTab));
            right.Children.Add(CreatePillButton("Load Theme...", async () => await LoadCustomThemeFileAsync()));
            _themeBtn.Content = "Light Mode";
            _themeBtn.FontSize = 12;
            _themeBtn.Padding = new Thickness(10, 5);
            _themeBtn.CornerRadius = new CornerRadius(6);
            _themeBtn.Click += (_, _) => ApplyTheme(!_isDarkTheme, persistToConfig: true);
            right.Children.Add(_themeBtn);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(left);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            _topBarBorder.Padding = new Thickness(12, 7);
            _topBarBorder.BorderThickness = new Thickness(0, 0, 0, 1);
            _topBarBorder.Child = grid;
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

            AddRibbonTabButton(tabsRow, RibbonTabKind.View, "View");
            AddRibbonTabButton(tabsRow, RibbonTabKind.DrawingTools, "Drawing Tools");

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
            // View tab stays open even if FormattingToolbar is closed so the user can always bring views back
            bool showSubToolbar = _activeRibbonTab == RibbonTabKind.View ||
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
                RibbonTabKind.View => BuildViewTabRibbon(),
                RibbonTabKind.DrawingTools => BuildDrawingToolsRibbon(),
                _ => new Border()
            };

            if (_activeRibbonTab == RibbonTabKind.View)
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

        private Control BuildViewTabRibbon()
        {
            _viewToggleButtons.Clear();
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };

            wrap.Children.Add(new TextBlock
            {
                Text = "Workspace Views:",
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            foreach (WorkspaceViewKind view in Enum.GetValues<WorkspaceViewKind>())
            {
                var targetView = view;
                var btn = new Button
                {
                    FontSize = 11.5,
                    Padding = new Thickness(9, 4),
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(5)
                };
                btn.Click += (_, _) =>
                {
                    bool next = _viewState.ToggleView(targetView);
                    if (next && targetView == WorkspaceViewKind.EditorPane)
                        _showRawSourceInUnifiedWindow = true;
                    else if (next && targetView == WorkspaceViewKind.PreviewPane)
                        _showRawSourceInUnifiedWindow = false;
                    RefreshViewLayout();
                };
                _viewToggleButtons[targetView] = btn;
                wrap.Children.Add(btn);
            }

            wrap.Children.Add(CreateVerticalDivider());

            var resetBtn = CreateToolbarBtn("Reset All Views", () => _viewState.ResetAllViews(), accent: true);
            wrap.Children.Add(resetBtn);

            UpdateViewToggleLabels();
            return wrap;
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

        private void BuildVaultExplorerView()
        {
            _vaultSearchBox.PlaceholderText = "Filter files...";
            _vaultSearchBox.FontSize = 12;
            _vaultSearchBox.Margin = new Thickness(0, 0, 0, 6);
            _vaultSearchBox.TextChanged += (_, _) => RefreshVaultTree();

            _vaultListStack.Spacing = 2;

            var content = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*")
            };
            content.Children.Add(_vaultSearchBox);
            var scroller = new ScrollViewer { Content = _vaultListStack };
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
            var left = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _statusSaveText, _statusViewsText, _statusCaretText, _statusLatexText }
            };

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _statusCountsText, CreateCloseViewButton(WorkspaceViewKind.StatusBar, "Close status bar") }
            };

            foreach (var tb in new[] { _statusSaveText, _statusViewsText, _statusCaretText, _statusLatexText, _statusCountsText })
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

        private void WireEvents()
        {
            _viewState.Changed += (_, _) => RefreshViewLayout();

            _editorTextBox.GotFocus += (_, _) =>
            {
                _preferDrawingUndo = false;
                UpdateUndoRedoButtons();
            };

            _editorTextBox.PointerPressed += (_, _) =>
            {
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

            _markdownPreview.ResolveEmbeddedDrawing = target => _assets.LoadOrCreateCanvas(target, _vault.VaultPath);

            _markdownPreview.ResolveVaultImagePath = target =>
                _assets.ResolveImagePath(
                    target,
                    _activeTab is not null ? Path.GetDirectoryName(_activeTab.FilePath) : null,
                    _vault.VaultPath);

            _markdownPreview.SaveInlineCanvasToFolder = (openFenceLine, doc) =>
                _assets.SaveInlineCanvasToCanvasFolder(_activeTab?.Title, openFenceLine, doc);

            _markdownPreview.CreateSavedDocumentCanvas = doc =>
            {
                var created = _assets.CreateSavedCanvas(doc);
                RefreshVaultTree();
                return created;
            };

            _markdownPreview.TryHandleImagePasteCallback = lineZeroBased =>
                TryHandleImagePasteKey(lineZeroBased);

            _markdownPreview.CopyImageRequested += async (_, bmp) =>
                await CopyBitmapToClipboardAsync(bmp);

            _markdownPreview.InlineDrawingUpdated += (_, args) =>
            {
                if (_activeTab is null || _activeTab.IsDrawing) return;
                string updated = ReplaceFencedBlockBody(_activeTab.Content, args.OpenFenceLineZeroBased, args.UpdatedJson);
                _activeTab.Content = updated;
                _activeTab.IsDirty = true;

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
                string savedPath = _assets.SaveCanvas(args.TargetName, args.UpdatedJson, _vault.VaultPath);
                var open = _openTabs.FirstOrDefault(t => string.Equals(t.FilePath, savedPath, StringComparison.OrdinalIgnoreCase));
                if (open is not null)
                {
                    open.Content = args.UpdatedJson;
                    open.IsDirty = false;
                }
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

            _standaloneDrawing.Canvas.HistoryChanged += (_, _) => UpdateUndoRedoButtons();
            _standaloneDrawing.DocumentChanged += (_, _) =>
            {
                _preferDrawingUndo = true;
                if (_activeTab is not null && _activeTab.IsDrawing)
                {
                    _activeTab.Content = _standaloneDrawing.Canvas.Document.ToJson();
                    _activeTab.IsDirty = true;
                    UpdateUndoRedoButtons();
                    UpdateStatusBar();
                    RefreshTabStrip();
                }
            };

            AddHandler(KeyDownEvent, OnWindowTunnelKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            KeyDown += OnWindowKeyDown;
        }

        private void OnWindowTunnelKeyDown(object? sender, KeyEventArgs e)
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
                return;

            // Allow small utility inputs to keep standard single-line editing shortcuts
            if (_vaultSearchBox.IsFocused || _quickSwitcherInput.IsFocused || _promptInput.IsFocused || _liveLatexScratchpad.IsFocused)
                return;

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

            if (e.Key == Key.Escape && _syntaxGuideOverlay.IsVisible)
            {
                _syntaxGuideOverlay.IsVisible = false;
                e.Handled = true;
                return;
            }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (e.Key == Key.S)
                {
                    SaveActiveTab();
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
            var activeBg = AccentBrush();
            foreach (var (view, btn) in _viewToggleButtons)
            {
                bool open = _viewState.IsVisible(view);
                btn.Content = $"{(open ? "[✓]" : "[ ]")} {ViewLayoutState.GetDisplayName(view)}";
                if (open)
                {
                    btn.Background = activeBg;
                    btn.Foreground = Brushes.White;
                }
                else
                {
                    btn.ClearValue(BackgroundProperty);
                    btn.ClearValue(Button.ForegroundProperty);
                }
            }
        }

        private void RefreshVaultTree()
        {
            _vaultNameLabel.Text = $"Vault: {Path.GetFileName(_vault.VaultPath)}";
            _vaultListStack.Children.Clear();

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
                        if (!_collapsedVaultFolders.Remove(folderPath))
                            _collapsedVaultFolders.Add(folderPath);
                        RefreshVaultTree();
                    };

                    var folderRow = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                        Margin = new Thickness(entry.Depth * 12, 2, 0, 1)
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

                    _vaultListStack.Children.Add(folderRow);
                    if (isCollapsed)
                        collapsedDepth = entry.Depth;
                    continue;
                }

                bool isActive = _activeTab is not null &&
                                string.Equals(_activeTab.FilePath, entry.FullPath, StringComparison.OrdinalIgnoreCase);

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
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    FontSize = 12.5,
                    Padding = new Thickness(8, 5),
                    CornerRadius = new CornerRadius(5),
                    Background = isActive ? AccentBrush() : Brushes.Transparent,
                    Foreground = isActive ? Brushes.White : ForegroundBrush()
                };
                openBtn.Click += (_, _) => OpenFileInTab(entry.FullPath);

                var delBtn = new Button
                {
                    Content = "🗑",
                    FontSize = 11,
                    Padding = new Thickness(5, 3),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0)
                };
                ToolTip.SetTip(delBtn, $"Delete {entry.Name}");
                delBtn.Click += (_, _) => DeleteVaultFile(entry.FullPath);

                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Margin = new Thickness(entry.Depth * 12, 0, 0, 0)
                };
                row.Children.Add(openBtn);
                Grid.SetColumn(delBtn, 1);
                row.Children.Add(delBtn);

                _vaultListStack.Children.Add(row);
            }
        }

        private void OpenStarterDocument()
        {
            var entries = _vault.GetEntries().Where(e => e.Kind != VaultEntryKind.Folder).ToList();
            var welcome = entries.FirstOrDefault(e => e.Name.Equals("Welcome to NoteDraw.md", StringComparison.OrdinalIgnoreCase))
                          ?? entries.FirstOrDefault();

            if (welcome is not null)
                OpenFileInTab(welcome.FullPath);
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
            if (tab.IsDirty && !tab.IsImage)
            {
                _vault.WriteFile(tab.FilePath, tab.Content);
                tab.IsDirty = false;
            }

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
            _vault.WriteFile(_activeTab.FilePath, _activeTab.Content);
            _activeTab.IsDirty = false;
            RefreshTabStrip();
            UpdateStatusBar();

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
                _statusSaveText.Text = _activeTab.IsDirty ? "● Unsaved changes (Ctrl+S)" : "✓ Saved";
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

            _statusViewsText.Text = $"Views: {_viewState.VisibleCount}/{_viewState.TotalCount} (View tab)";
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
            if (_activeTab is null || _activeTab.IsDrawing) return;
            var blank = new DrawingDocument { Background = CanvasBackgroundKind.Grid };
            var saved = _assets.CreateSavedCanvas(blank);
            string embed = saved.EmbedMarkdown;

            if (!_showRawSourceInUnifiedWindow)
            {
                string cur = _activeTab.Content ?? string.Empty;
                string lineEnding = cur.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                string sep = string.IsNullOrWhiteSpace(cur) ? string.Empty : $"{lineEnding}{lineEnding}";
                string next = $"{cur.TrimEnd()}{sep}{embed}";
                _markdownPreview.OpenEmbeddedImageEditor($"canvas/{saved.FileName}");
                ApplyMarkdownContentUpdate(next, rebuildPreview: true);
            }
            else
            {
                InsertUtilitySnippet($"\n{embed}\n");
            }

            _statusSaveText.Text = $"✓ Canvas saved to canvas/{saved.FileName}";
            RefreshVaultTree();
        }

        private async System.Threading.Tasks.Task CopyBitmapToClipboardAsync(Bitmap bitmap)
        {
            _inAppCopiedBitmap = bitmap;
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
            if (_activeTab is null || _activeTab.IsDrawing)
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

            if (targetLineZeroBased.HasValue && !string.IsNullOrWhiteSpace(current))
            {
                var lines = current.Replace("\r\n", "\n").Split('\n').ToList();
                int idx = Math.Clamp(targetLineZeroBased.Value, 0, Math.Max(0, lines.Count - 1));
                if (string.IsNullOrWhiteSpace(lines[idx]))
                    lines[idx] = markdownLink;
                else
                    lines.Insert(idx + 1, markdownLink);
                next = string.Join(lineEnding, lines);
            }
            else
            {
                string sep = string.IsNullOrWhiteSpace(current) ? string.Empty : $"{lineEnding}{lineEnding}";
                next = $"{current.TrimEnd()}{sep}{markdownLink}";
            }

            _markdownPreview.DeactivateActiveSegment();
            ApplyMarkdownContentUpdate(next, rebuildPreview: true);
        }

        internal static bool IsSupportedImageExtension(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
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

        private void DeleteVaultFile(string fullPath)
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
            _collapsedVaultFolders.Remove(fullPath);
            _vault.DeleteEntry(fullPath);
            RefreshVaultTree();
        }

        private void RegisterAssetFoldersInVault()
        {
            _vault.RegisterExternalFolder("canvas", _assets.CanvasFolderPath);
            _vault.RegisterExternalFolder("image", _assets.ImageFolderPath);
        }

        private async System.Threading.Tasks.Task OpenVaultFolderAsync()
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select NoteDraw Vault Folder",
                AllowMultiple = false
            });
            if (folders.Count == 0) return;

            string? path = folders[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;

            _openTabs.Clear();
            _activeTab = null;
            _collapsedVaultFolders.Clear();
            _vault.OpenVault(path);
            _vault.EnsureVaultInitialized();
            RegisterAssetFoldersInVault();
            _themeConfig.Load(_vault.VaultPath);
            ApplyTheme(_themeConfig.IsDark, persistToConfig: false);
            RefreshVaultTree();
            OpenStarterDocument();
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

            rightStack.Children.Add(CreateCloseViewButton(view, $"Close {ViewLayoutState.GetDisplayName(view)} (restore from View tab)", onCloseOverride));

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
