using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using NoteDraw.Drawing;
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
        public string Content { get; set; } = string.Empty;
        public bool IsDirty { get; set; }
        public int CaretIndex { get; set; }
    }

    internal enum RibbonTabKind
    {
        EditFormat,
        View,
        LatexAssistant,
        DrawingTools
    }

    public partial class MainWindow : Window
    {
        private readonly VaultService _vault = new();
        private readonly ViewLayoutState _viewState = new();
        private readonly List<OpenDocumentTab> _openTabs = new();
        private OpenDocumentTab? _activeTab;
        private RibbonTabKind _activeRibbonTab = RibbonTabKind.EditFormat;
        private bool _isDarkTheme = true;
        private bool _suppressEditorTextChanged;
        private bool _suppressPreviewRebuild;

        // Top bar & ribbon controls
        private readonly Border _topBarBorder = new();
        private readonly TextBlock _vaultNameLabel = new();
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
        private readonly TextBox _editorTextBox = new();
        private readonly TextBlock _editorCursorLabel = new();

        // Live LaTeX typing preview view
        private readonly Border _liveLatexPanel = new();
        private readonly TextBlock _liveLatexBadge = new();
        private readonly LatexMathControl _liveLatexControl = new();
        private readonly TextBlock _liveLatexDiag = new();
        private readonly TextBox _liveLatexScratchpad = new();
        private bool _syncingScratchpad;

        // Document preview & standalone drawing view
        private readonly Border _previewPanel = new();
        private readonly TextBlock _previewHeaderTitle = new();
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

        // Modal / inline overlays (Quick Switcher & Prompt)
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
            BuildWorkspaceLayout();
            WireEvents();
            ApplyTheme(_isDarkTheme);
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

            _workspaceSplitGrid.ColumnDefinitions = new ColumnDefinitions("*,*");
            _leftEditorColumnGrid.RowDefinitions = new RowDefinitions("*,Auto");

            BuildEditorView();
            BuildLiveLatexPreviewView();
            BuildPreviewView();

            Grid.SetRow(_editorPanel, 0);
            Grid.SetRow(_liveLatexPanel, 1);
            _leftEditorColumnGrid.Children.Add(_editorPanel);
            _leftEditorColumnGrid.Children.Add(_liveLatexPanel);

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
            BuildQuickSwitcherOverlay();
            BuildPromptOverlay();

            var rootContainer = new Grid();
            rootContainer.Children.Add(outerGrid);
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
            left.Children.Add(CreatePillButton("Save", SaveActiveTab));

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            right.Children.Add(CreatePillButton("Quick Switcher (Ctrl+P)", OpenQuickSwitcher));
            _themeBtn.Content = "Light Mode";
            _themeBtn.FontSize = 12;
            _themeBtn.Padding = new Thickness(10, 5);
            _themeBtn.CornerRadius = new CornerRadius(6);
            _themeBtn.Click += (_, _) => ApplyTheme(!_isDarkTheme);
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

            AddRibbonTabButton(tabsRow, RibbonTabKind.EditFormat, "Edit & Format");
            AddRibbonTabButton(tabsRow, RibbonTabKind.View, "View");
            AddRibbonTabButton(tabsRow, RibbonTabKind.LatexAssistant, "LaTeX Assistant");
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
            bool showSubToolbar = _activeRibbonTab == RibbonTabKind.View || _viewState.IsVisible(WorkspaceViewKind.FormattingToolbar);
            _ribbonContentHost.IsVisible = showSubToolbar;

            foreach (var (kind, btn) in _ribbonTabButtons)
            {
                if (kind == _activeRibbonTab)
                {
                    btn.Background = new SolidColorBrush(Color.Parse("#2563EB"));
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
                RibbonTabKind.EditFormat => BuildEditFormatRibbon(),
                RibbonTabKind.View => BuildViewTabRibbon(),
                RibbonTabKind.LatexAssistant => BuildLatexAssistantRibbon(),
                RibbonTabKind.DrawingTools => BuildDrawingToolsRibbon(),
                _ => new Border()
            };

            if (_activeRibbonTab == RibbonTabKind.View)
            {
                _ribbonContentHost.Child = content;
                return;
            }

            // Include a × close button on the Formatting/Context sub-toolbar
            var closeToolbarBtn = CreateCloseViewButton(WorkspaceViewKind.FormattingToolbar, "Close formatting sub-toolbar");
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(content);
            Grid.SetColumn(closeToolbarBtn, 1);
            row.Children.Add(closeToolbarBtn);
            _ribbonContentHost.Child = row;
        }

        private Control BuildEditFormatRibbon()
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            wrap.Children.Add(CreateToolbarBtn("H1", () => PrefixCurrentLine("# ")));
            wrap.Children.Add(CreateToolbarBtn("H2", () => PrefixCurrentLine("## ")));
            wrap.Children.Add(CreateToolbarBtn("H3", () => PrefixCurrentLine("### ")));
            wrap.Children.Add(CreateToolbarBtn("Bold", () => WrapSelection("**", "**", "bold")));
            wrap.Children.Add(CreateToolbarBtn("Italic", () => WrapSelection("*", "*", "italic")));
            wrap.Children.Add(CreateToolbarBtn("Strike", () => WrapSelection("~~", "~~", "text")));
            wrap.Children.Add(CreateToolbarBtn("Inline Code", () => WrapSelection("`", "`", "code")));
            wrap.Children.Add(CreateToolbarBtn("Quote", () => PrefixCurrentLine("> ")));
            wrap.Children.Add(CreateToolbarBtn("Bullet", () => PrefixCurrentLine("- ")));
            wrap.Children.Add(CreateToolbarBtn("Checklist", () => PrefixCurrentLine("- [ ] ")));
            wrap.Children.Add(CreateToolbarBtn("Table", () => InsertSnippet("\n| Column 1 | Column 2 |\n| --- | --- |\n| Value A | Value B |\n")));
            wrap.Children.Add(CreateToolbarBtn("[[WikiLink]]", () => WrapSelection("[[", "]]", "Note Name")));

            wrap.Children.Add(CreateVerticalDivider());

            wrap.Children.Add(CreateToolbarBtn("+ ```latex Block", () =>
                InsertSnippet("\n```latex\n\\frac{a}{b} = c\n```\n", caretOffsetFromInsertEnd: -5), accent: true));
            wrap.Children.Add(CreateToolbarBtn("+ $$ Math $$", () =>
                InsertSnippet("\n$$\n\\sum_{i=1}^n x_i\n$$\n", caretOffsetFromInsertEnd: -4), accent: true));
            wrap.Children.Add(CreateToolbarBtn("+ ```drawing Canvas", InsertNewDrawingBlockAtCaret, accent: true));

            wrap.Children.Add(CreateVerticalDivider());

            wrap.Children.Add(CreateToolbarBtn("Split View", () => _viewState.ApplyEditorLayoutPreset("Split")));
            wrap.Children.Add(CreateToolbarBtn("Editor Only", () => _viewState.ApplyEditorLayoutPreset("Editor")));
            wrap.Children.Add(CreateToolbarBtn("Preview Only", () => _viewState.ApplyEditorLayoutPreset("Preview")));

            return wrap;
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
                btn.Click += (_, _) => _viewState.ToggleView(targetView);
                _viewToggleButtons[targetView] = btn;
                wrap.Children.Add(btn);
            }

            wrap.Children.Add(CreateVerticalDivider());

            var resetBtn = CreateToolbarBtn("Reset All Views", () => _viewState.ResetAllViews(), accent: true);
            wrap.Children.Add(resetBtn);

            UpdateViewToggleLabels();
            return wrap;
        }

        private Control BuildLatexAssistantRibbon()
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            var snippets = new (string Label, string Code)[]
            {
                ("```latex Block", "\n```latex\n\\int_0^1 x^2\\,dx = \\frac{1}{3}\n```\n"),
                ("\\frac{a}{b}", "\\frac{a}{b}"),
                ("\\sqrt{x}", "\\sqrt{x}"),
                ("\\sqrt[n]{x}", "\\sqrt[n]{x}"),
                ("\\int_a^b", "\\int_{a}^{b} f(x)\\,dx"),
                ("\\sum_{i=1}^n", "\\sum_{i=1}^{n} a_i"),
                ("\\prod", "\\prod_{i=1}^{n} x_i"),
                ("\\lim", "\\lim_{x \\to \\infty} f(x)"),
                ("\\partial", "\\frac{\\partial f}{\\partial x}"),
                ("2x2 Matrix", "\\begin{pmatrix}\na & b \\\\\nc & d\n\\end{pmatrix}"),
                ("3x3 Bracket", "\\begin{bmatrix}\n1 & 0 & 0 \\\\\n0 & 1 & 0 \\\\\n0 & 0 & 1\n\\end{bmatrix}"),
                ("cases", "\\begin{cases}\nx^2 & x \\ge 0 \\\\\n-x & x < 0\n\\end{cases}"),
                ("α", "\\alpha"),
                ("β", "\\beta"),
                ("θ", "\\theta"),
                ("π", "\\pi"),
                ("λ", "\\lambda"),
                ("Σ", "\\Sigma"),
                ("∇", "\\nabla"),
                ("∞", "\\infty"),
                ("≤", "\\le"),
                ("≥", "\\ge"),
                ("≠", "\\neq"),
                ("≈", "\\approx"),
                ("∈", "\\in"),
                ("∀", "\\forall"),
                ("∃", "\\exists"),
                ("ℝ", "\\mathbb{R}")
            };

            foreach (var (label, code) in snippets)
            {
                string snippet = code;
                wrap.Children.Add(CreateToolbarBtn(label, () => InsertSnippet(snippet)));
            }

            return wrap;
        }

        private Control BuildDrawingToolsRibbon()
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            wrap.Children.Add(CreateToolbarBtn("Insert Inline ```drawing Block", InsertNewDrawingBlockAtCaret, accent: true));
            wrap.Children.Add(CreateToolbarBtn("New Standalone .ndraw File", PromptCreateDrawing, accent: true));
            wrap.Children.Add(CreateToolbarBtn("Embed Existing .ndraw", () =>
            {
                var firstDrawing = _vault.GetEntries().FirstOrDefault(e => e.Kind == VaultEntryKind.DrawingFile);
                string target = firstDrawing?.Name ?? "Sketchpad Demo.ndraw";
                InsertSnippet($"\n![[{target}]]\n");
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
            var header = CreateViewHeader("VAULT EXPLORER", WorkspaceViewKind.VaultExplorer, extraAction: ("+ Note", PromptCreateNote));
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

        private void BuildEditorView()
        {
            _editorCursorLabel.FontSize = 11;
            _editorCursorLabel.Opacity = 0.75;
            _editorCursorLabel.VerticalAlignment = VerticalAlignment.Center;

            _editorTextBox.AcceptsReturn = true;
            _editorTextBox.AcceptsTab = true;
            _editorTextBox.TextWrapping = TextWrapping.Wrap;
            _editorTextBox.FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace");
            _editorTextBox.FontSize = 13.5;
            _editorTextBox.BorderThickness = new Thickness(0);
            _editorTextBox.Padding = new Thickness(14, 12);
            _editorTextBox.PlaceholderText = "Write Markdown, $inline math$, ```latex blocks, or ```drawing blocks...";

            var header = CreateViewHeader("MARKDOWN & LATEX EDITOR", WorkspaceViewKind.EditorPane, rightBadge: _editorCursorLabel);
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            grid.Children.Add(header);
            Grid.SetRow(_editorTextBox, 1);
            grid.Children.Add(_editorTextBox);

            _editorPanel.BorderThickness = new Thickness(0, 0, 1, 0);
            _editorPanel.Child = grid;
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
                    InsertSnippet($"\n```latex\n{tex}\n```\n");
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

            _liveLatexPanel.BorderThickness = new Thickness(0, 1, 1, 0);
            _liveLatexPanel.Child = grid;
        }

        private void BuildPreviewView()
        {
            _previewHeaderTitle.Text = "DOCUMENT PREVIEW";
            _previewHeaderTitle.FontSize = 11;
            _previewHeaderTitle.FontWeight = FontWeight.Bold;
            _previewHeaderTitle.VerticalAlignment = VerticalAlignment.Center;

            var header = CreateViewHeaderCustom(_previewHeaderTitle, WorkspaceViewKind.PreviewPane);
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            grid.Children.Add(header);
            Grid.SetRow(_previewContentHost, 1);
            grid.Children.Add(_previewContentHost);

            _previewContentHost.Content = _markdownPreview;
            _previewPanel.Child = grid;
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

            _editorTextBox.TextChanged += (_, _) =>
            {
                if (_suppressEditorTextChanged || _activeTab is null || _activeTab.IsDrawing)
                    return;

                _activeTab.Content = _editorTextBox.Text ?? string.Empty;
                _activeTab.IsDirty = true;
                _activeTab.CaretIndex = _editorTextBox.CaretIndex;

                if (!_suppressPreviewRebuild)
                    _markdownPreview.RenderMarkdown(_activeTab.Content);

                RefreshOutlineAndTags(_activeTab.Content);
                UpdateLiveLatexFromCursor();
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
                var entry = _vault.ResolveWikiLink(target);
                if (entry is null || !File.Exists(entry.FullPath))
                    return null;
                return DrawingDocument.FromJson(_vault.ReadFile(entry.FullPath));
            };

            _markdownPreview.InlineDrawingUpdated += (_, args) =>
            {
                if (_activeTab is null || _activeTab.IsDrawing) return;
                string updated = ReplaceFencedBlockBody(_activeTab.Content, args.OpenFenceLineZeroBased, args.UpdatedJson);
                _activeTab.Content = updated;
                _activeTab.IsDirty = true;

                _suppressEditorTextChanged = true;
                _suppressPreviewRebuild = true;
                try
                {
                    int savedCaret = _editorTextBox.CaretIndex;
                    _editorTextBox.Text = updated;
                    _editorTextBox.CaretIndex = Math.Clamp(savedCaret, 0, updated.Length);
                }
                finally
                {
                    _suppressPreviewRebuild = false;
                    _suppressEditorTextChanged = false;
                }

                UpdateStatusBar();
                RefreshTabStrip();
            };

            _markdownPreview.ExternalDrawingUpdated += (_, args) =>
            {
                var entry = _vault.ResolveWikiLink(args.TargetName);
                if (entry is null) return;
                _vault.WriteFile(entry.FullPath, args.UpdatedJson);
                var open = _openTabs.FirstOrDefault(t => string.Equals(t.FilePath, entry.FullPath, StringComparison.OrdinalIgnoreCase));
                if (open is not null)
                {
                    open.Content = args.UpdatedJson;
                    open.IsDirty = false;
                }
                UpdateStatusBar();
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

            _standaloneDrawing.DocumentChanged += (_, _) =>
            {
                if (_activeTab is not null && _activeTab.IsDrawing)
                {
                    _activeTab.Content = _standaloneDrawing.Canvas.Document.ToJson();
                    _activeTab.IsDirty = true;
                    UpdateStatusBar();
                    RefreshTabStrip();
                }
            };

            KeyDown += OnWindowKeyDown;
        }

        private void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
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
            bool showEditor = !isDrawingTab && _viewState.IsVisible(WorkspaceViewKind.EditorPane);
            bool showLiveLatex = !isDrawingTab && _viewState.IsVisible(WorkspaceViewKind.LiveLatexPreview);
            bool showLeftCol = showEditor || showLiveLatex;
            bool showPreview = _viewState.IsVisible(WorkspaceViewKind.PreviewPane);

            _editorPanel.IsVisible = showEditor;
            _liveLatexPanel.IsVisible = showLiveLatex;
            _leftEditorColumnGrid.IsVisible = showLeftCol;
            _previewPanel.IsVisible = showPreview;

            _leftEditorColumnGrid.RowDefinitions[0].Height = showEditor ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            _leftEditorColumnGrid.RowDefinitions[1].Height = showLiveLatex ? GridLength.Auto : new GridLength(0);

            _workspaceSplitGrid.ColumnDefinitions[0].Width = showLeftCol ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            _workspaceSplitGrid.ColumnDefinitions[1].Width = showPreview ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

            _statusBarBorder.IsVisible = _viewState.IsVisible(WorkspaceViewKind.StatusBar);

            RefreshRibbonContent();
            UpdateViewToggleLabels();
            UpdateStatusBar();
        }

        private void UpdateViewToggleLabels()
        {
            var activeBg = new SolidColorBrush(Color.Parse("#2563EB"));
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

            foreach (var entry in entries)
            {
                if (entry.Kind == VaultEntryKind.Folder)
                {
                    _vaultListStack.Children.Add(new TextBlock
                    {
                        Text = $"📁 {entry.Name}",
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(4 + entry.Depth * 12, 4, 4, 2),
                        Opacity = 0.85
                    });
                    continue;
                }

                bool isActive = _activeTab is not null &&
                                string.Equals(_activeTab.FilePath, entry.FullPath, StringComparison.OrdinalIgnoreCase);

                string icon = entry.Kind == VaultEntryKind.DrawingFile ? "🎨" : "📄";
                var openBtn = new Button
                {
                    Content = $"{icon} {entry.Name}",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    FontSize = 12.5,
                    Padding = new Thickness(8, 5),
                    CornerRadius = new CornerRadius(5),
                    Background = isActive ? new SolidColorBrush(Color.Parse("#2563EB")) : Brushes.Transparent,
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
                existing = new OpenDocumentTab
                {
                    FilePath = fullPath,
                    Content = _vault.ReadFile(fullPath),
                    IsDirty = false
                };
                _openTabs.Add(existing);
            }

            SetActiveTab(existing);
        }

        private void SetActiveTab(OpenDocumentTab? tab)
        {
            _activeTab = tab;
            RefreshTabStrip();
            RefreshVaultTree();

            if (tab is null)
            {
                _suppressEditorTextChanged = true;
                _editorTextBox.Text = string.Empty;
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
                _previewHeaderTitle.Text = $"VECTOR DRAWING CANVAS — {tab.Title}";
                _standaloneDrawing.LoadDocument(DrawingDocument.FromJson(tab.Content));
                _previewContentHost.Content = _standaloneDrawing;
                RefreshOutlineAndTags(string.Empty);
            }
            else
            {
                _previewHeaderTitle.Text = $"DOCUMENT PREVIEW — {tab.Title}";
                _previewContentHost.Content = _markdownPreview;
                _suppressEditorTextChanged = true;
                _editorTextBox.Text = tab.Content;
                _editorTextBox.CaretIndex = Math.Clamp(tab.CaretIndex, 0, tab.Content.Length);
                _suppressEditorTextChanged = false;
                _markdownPreview.RenderMarkdown(tab.Content);
                RefreshOutlineAndTags(tab.Content);
                UpdateLiveLatexFromCursor();
            }

            RefreshViewLayout();
            UpdateStatusBar();
        }

        private void RefreshTabStrip()
        {
            _tabsStrip.Children.Clear();
            foreach (var tab in _openTabs)
            {
                bool active = ReferenceEquals(tab, _activeTab);
                string dirtyDot = tab.IsDirty ? " ●" : "";
                string icon = tab.IsDrawing ? "🎨" : "📄";

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
                        ? new SolidColorBrush(Color.Parse("#2563EB"))
                        : new SolidColorBrush(Color.Parse(_isDarkTheme ? "#1E2638" : "#E2E8F0")),
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
            if (tab.IsDirty)
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
            if (_activeTab is null) return;
            _vault.WriteFile(_activeTab.FilePath, _activeTab.Content);
            _activeTab.IsDirty = false;
            RefreshTabStrip();
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
                    Background = new SolidColorBrush(Color.Parse(_isDarkTheme ? "#064E3B" : "#D1FAE5")),
                    Foreground = new SolidColorBrush(Color.Parse(_isDarkTheme ? "#34D399" : "#059669"))
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
                    string text = _editorTextBox.Text ?? string.Empty;
                    int caret = Math.Clamp(_editorTextBox.CaretIndex, 0, text.Length);
                    var (ln, col) = GetLineAndColumn(text, caret);
                    _editorCursorLabel.Text = $"Ln {ln}, Col {col}";
                    _statusCaretText.Text = $"Ln {ln}, Col {col}";
                    int words = string.IsNullOrWhiteSpace(text)
                        ? 0
                        : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                    _statusCountsText.Text = $"{words} words • {text.Length} chars";
                }
            }

            _statusViewsText.Text = $"Views: {_viewState.VisibleCount}/{_viewState.TotalCount} (View tab)";
        }

        private void JumpEditorToLine(int oneBasedLine)
        {
            _viewState.OpenView(WorkspaceViewKind.EditorPane);
            string text = _editorTextBox.Text ?? string.Empty;
            int offset = GetOffsetForLine(text, oneBasedLine);
            _editorTextBox.Focus();
            _editorTextBox.CaretIndex = offset;
            UpdateLiveLatexFromCursor();
            UpdateStatusBar();
        }

        private void InsertSnippet(string snippet, int caretOffsetFromInsertEnd = 0)
        {
            if (_activeTab is null || _activeTab.IsDrawing) return;
            _viewState.OpenView(WorkspaceViewKind.EditorPane);

            string current = _editorTextBox.Text ?? string.Empty;
            int start = Math.Clamp(_editorTextBox.SelectionStart, 0, current.Length);
            int end = Math.Clamp(_editorTextBox.SelectionEnd, 0, current.Length);
            if (start > end) (start, end) = (end, start);

            string updated = current[..start] + snippet + current[end..];
            int newCaret = Math.Clamp(start + snippet.Length + caretOffsetFromInsertEnd, 0, updated.Length);

            ApplyMarkdownContentUpdate(updated, rebuildPreview: true, newCaretIndex: newCaret);
            _editorTextBox.Focus();
        }

        private void WrapSelection(string prefix, string suffix, string placeholder)
        {
            if (_activeTab is null || _activeTab.IsDrawing) return;
            _viewState.OpenView(WorkspaceViewKind.EditorPane);

            string current = _editorTextBox.Text ?? string.Empty;
            int start = Math.Clamp(_editorTextBox.SelectionStart, 0, current.Length);
            int end = Math.Clamp(_editorTextBox.SelectionEnd, 0, current.Length);
            if (start > end) (start, end) = (end, start);

            string inner = start < end ? current[start..end] : placeholder;
            string inserted = prefix + inner + suffix;
            string updated = current[..start] + inserted + current[end..];
            int newCaret = start + prefix.Length + inner.Length;

            ApplyMarkdownContentUpdate(updated, rebuildPreview: true, newCaretIndex: newCaret);
            _editorTextBox.Focus();
        }

        private void PrefixCurrentLine(string prefix)
        {
            if (_activeTab is null || _activeTab.IsDrawing) return;
            _viewState.OpenView(WorkspaceViewKind.EditorPane);

            string current = _editorTextBox.Text ?? string.Empty;
            int caret = Math.Clamp(_editorTextBox.CaretIndex, 0, current.Length);
            int lineStart = current.LastIndexOf('\n', Math.Max(0, caret - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;

            string updated = current[..lineStart] + prefix + current[lineStart..];
            ApplyMarkdownContentUpdate(updated, rebuildPreview: true, newCaretIndex: caret + prefix.Length);
            _editorTextBox.Focus();
        }

        private void InsertNewDrawingBlockAtCaret()
        {
            var blank = new DrawingDocument { Background = CanvasBackgroundKind.Grid };
            string snippet = $"\n```drawing\n{blank.ToJson()}\n```\n";
            InsertSnippet(snippet);
        }

        private void ApplyMarkdownContentUpdate(string updated, bool rebuildPreview, int? newCaretIndex = null)
        {
            if (_activeTab is null || _activeTab.IsDrawing) return;

            _activeTab.Content = updated;
            _activeTab.IsDirty = true;

            _suppressEditorTextChanged = true;
            _editorTextBox.Text = updated;
            if (newCaretIndex.HasValue)
                _editorTextBox.CaretIndex = Math.Clamp(newCaretIndex.Value, 0, updated.Length);
            _suppressEditorTextChanged = false;

            if (rebuildPreview)
                _markdownPreview.RenderMarkdown(updated);

            RefreshOutlineAndTags(updated);
            UpdateLiveLatexFromCursor();
            UpdateStatusBar();
            RefreshTabStrip();
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
            var tab = _openTabs.FirstOrDefault(t => string.Equals(t.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (tab is not null)
            {
                tab.IsDirty = false;
                CloseTab(tab);
            }
            _vault.DeleteEntry(fullPath);
            RefreshVaultTree();
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
            _vault.OpenVault(path);
            _vault.EnsureVaultInitialized();
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

        private void ApplyTheme(bool isDark)
        {
            _isDarkTheme = isDark;
            if (Application.Current is not null)
                Application.Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;

            _themeBtn.Content = isDark ? "Light Mode" : "Dark Mode";

            var chromeBg = new SolidColorBrush(Color.Parse(isDark ? "#0F1420" : "#F1F5F9"));
            var panelBg = new SolidColorBrush(Color.Parse(isDark ? "#131926" : "#FFFFFF"));
            var headerBg = new SolidColorBrush(Color.Parse(isDark ? "#192234" : "#E2E8F0"));
            var border = new SolidColorBrush(Color.Parse(isDark ? "#263147" : "#CBD5E1"));
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
            _editorTextBox.Background = panelBg;
            _editorTextBox.Foreground = text;

            _liveLatexPanel.Background = headerBg;
            _liveLatexPanel.BorderBrush = border;
            _liveLatexControl.Foreground = text;
            _liveLatexDiag.Foreground = new SolidColorBrush(Color.Parse(isDark ? "#FBBF24" : "#D97706"));

            _previewPanel.Background = panelBg;
            _markdownPreview.IsDarkTheme = isDark;
            _standaloneDrawing.ApplyTheme(isDark);

            if (_quickSwitcherOverlay.Child is Border qsCard)
                qsCard.Background = panelBg;
            if (_promptOverlay.Child is Border pCard)
                pCard.Background = panelBg;

            RefreshTabStrip();
            RefreshVaultTree();
        }

        private Border CreateViewHeader(string title, WorkspaceViewKind view, (string Label, Action Action)? extraAction = null, Control? rightBadge = null)
        {
            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            return CreateViewHeaderCustom(titleBlock, view, extraAction, rightBadge);
        }

        private Border CreateViewHeaderCustom(Control titleControl, WorkspaceViewKind view, (string Label, Action Action)? extraAction = null, Control? rightBadge = null)
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

            if (extraAction.HasValue)
            {
                var extraBtn = CreateToolbarBtn(extraAction.Value.Label, extraAction.Value.Action);
                extraBtn.Padding = new Thickness(6, 2);
                rightStack.Children.Add(extraBtn);
            }

            rightStack.Children.Add(CreateCloseViewButton(view, $"Close {ViewLayoutState.GetDisplayName(view)} (restore from View tab)"));

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

        private Button CreateCloseViewButton(WorkspaceViewKind view, string tooltip)
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
            btn.Click += (_, _) => _viewState.CloseView(view);
            return btn;
        }

        private static Button CreatePillButton(string label, Action onClick, bool accent = false)
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
                btn.Background = new SolidColorBrush(Color.Parse("#2563EB"));
                btn.Foreground = Brushes.White;
            }
            btn.Click += (_, _) => onClick();
            return btn;
        }

        private static Button CreateToolbarBtn(string label, Action onClick, bool accent = false)
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
                btn.Background = new SolidColorBrush(Color.Parse("#2563EB"));
                btn.Foreground = Brushes.White;
            }
            btn.Click += (_, _) => onClick();
            return btn;
        }

        private static Border CreateVerticalDivider() => new()
        {
            Width = 1,
            Height = 20,
            Margin = new Thickness(6, 2),
            Background = new SolidColorBrush(Color.FromArgb(80, 148, 163, 184)),
            VerticalAlignment = VerticalAlignment.Center
        };

        private IBrush ForegroundBrush() => new SolidColorBrush(Color.Parse(_isDarkTheme ? "#E2E8F0" : "#1E293B"));
        private IBrush MutedBrush() => new SolidColorBrush(Color.Parse(_isDarkTheme ? "#94A3B8" : "#64748B"));

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
