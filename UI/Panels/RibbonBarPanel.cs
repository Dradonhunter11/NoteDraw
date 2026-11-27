using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NoteDraw.Services;

namespace NoteDraw.UI.Panels
{
    internal sealed class RibbonBarPanel : Border, IWorkspaceThemeAware
    {
        private readonly IWorkspaceMediator _mediator;
        private readonly Dictionary<RibbonTabKind, Button> _tabButtons = new();
        private readonly Border _contentHost = new();
        private readonly TextBlock _gitStatusLabel = new();
        private readonly List<Button> _accentButtons = new();
        private RibbonTabKind _activeTab = RibbonTabKind.DrawingTools;

        public RibbonBarPanel(IWorkspaceMediator mediator)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            BuildLayout();
        }

        internal IReadOnlyCollection<string> TabLabels =>
            _tabButtons.Values.Select(b => b.Content?.ToString() ?? string.Empty).ToList();

        internal RibbonTabKind ActiveTab => _activeTab;
        internal string GitStatusText => _gitStatusLabel.Text ?? string.Empty;

        public void SelectTab(RibbonTabKind tab)
        {
            _activeTab = tab;
            RefreshContent();
        }

        public void UpdateGitStatus(GitRepositoryStatus status)
        {
            if (!status.IsGitAvailable)
            {
                _gitStatusLabel.Text = "Git CLI not detected on PATH";
                return;
            }

            if (!status.IsRepository)
            {
                _gitStatusLabel.Text = "Not a Git repo yet (click Init Repo)";
                return;
            }

            int changedCount = status.Changes.Count;
            _gitStatusLabel.Text = changedCount > 0
                ? $"🌿 {status.BranchName} ({changedCount} changed)"
                : $"🌿 {status.BranchName} ✓";
        }

        public void RefreshContent()
        {
            // Git tab stays accessible even if FormattingToolbar is closed
            bool showSubToolbar = _activeTab == RibbonTabKind.Git ||
                                  _mediator.IsViewVisible(WorkspaceViewKind.FormattingToolbar);
            _contentHost.IsVisible = showSubToolbar;

            IBrush accent = _mediator.CurrentAccentBrush;
            foreach (var (kind, btn) in _tabButtons)
            {
                if (kind == _activeTab)
                {
                    btn.Background = accent;
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

            _accentButtons.Clear();
            Control content = _activeTab switch
            {
                RibbonTabKind.DrawingTools => BuildDrawingToolsRibbon(),
                RibbonTabKind.Git => BuildGitRibbon(),
                _ => new Border()
            };

            if (_activeTab == RibbonTabKind.Git)
            {
                _contentHost.Child = content;
                return;
            }

            // Include a × close button on the Drawing Tools context sub-toolbar
            var closeToolbarBtn = WorkspaceUiFactory.CreateCloseViewButton(
                WorkspaceViewKind.FormattingToolbar,
                "Close sub-toolbar",
                _mediator.CloseView);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(content);
            Grid.SetColumn(closeToolbarBtn, 1);
            row.Children.Add(closeToolbarBtn);
            _contentHost.Child = row;
        }

        public void ApplyTheme(ThemePalette palette, bool isDark)
        {
            IBrush chromeBg = ThemeConfigService.ParseBrush(palette.ChromeBackground, isDark ? "#1E1E1E" : "#F3F3F3");
            IBrush border = ThemeConfigService.ParseBrush(palette.BorderColor, isDark ? "#3C3C3C" : "#D4D4D4");
            IBrush accent = WorkspaceUiFactory.ResolveAccentBrush(palette, isDark);

            Background = chromeBg;
            BorderBrush = border;
            _contentHost.BorderBrush = border;

            foreach (var btn in _accentButtons)
            {
                btn.Background = accent;
                btn.Foreground = Brushes.White;
            }

            RefreshContent();
        }

        private void BuildLayout()
        {
            var tabsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(10, 5, 10, 4)
            };

            AddTabButton(tabsRow, RibbonTabKind.DrawingTools, "Drawing Tools");
            AddTabButton(tabsRow, RibbonTabKind.Git, "🔀 Git");

            _contentHost.Padding = new Thickness(10, 5, 10, 6);
            _contentHost.BorderThickness = new Thickness(0, 1, 0, 0);

            BorderThickness = new Thickness(0, 0, 0, 1);
            Child = new StackPanel
            {
                Children = { tabsRow, _contentHost }
            };
        }

        private void AddTabButton(Panel parent, RibbonTabKind kind, string label)
        {
            var btn = new Button
            {
                Content = label,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(12, 4),
                CornerRadius = new CornerRadius(6)
            };
            btn.Click += (_, _) => SelectTab(kind);
            _tabButtons[kind] = btn;
            parent.Children.Add(btn);
        }

        private Control BuildDrawingToolsRibbon()
        {
            IBrush accent = _mediator.CurrentAccentBrush;
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("↶ Undo Drawing", _mediator.PerformDrawingUndo, accent: true, accent, _accentButtons));
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("↷ Redo Drawing", _mediator.PerformDrawingRedo, accent: true, accent, _accentButtons));
            wrap.Children.Add(WorkspaceUiFactory.CreateVerticalDivider());
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("Insert Inline ```drawing Block", _mediator.InsertCanvasInDocument, accent: true, accent, _accentButtons));
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("New Standalone .ndraw File", _mediator.PromptCreateDrawing, accent: true, accent, _accentButtons));
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("Embed Existing .ndraw", () =>
            {
                var firstDrawing = _mediator.GetVaultEntries().FirstOrDefault(e => e.Kind == VaultEntryKind.DrawingFile);
                string target = firstDrawing?.Name ?? "Sketchpad Demo.ndraw";
                _mediator.InsertUtilitySnippet($"\n![[{target}]]\n");
            }));
            wrap.Children.Add(WorkspaceUiFactory.CreateVerticalDivider());
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("Export Active Drawing PNG", () => _mediator.ExportActiveDrawing(isSvg: false)));
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("Export Active Drawing SVG", () => _mediator.ExportActiveDrawing(isSvg: true)));
            return wrap;
        }

        private Control BuildGitRibbon()
        {
            IBrush accent = _mediator.CurrentAccentBrush;
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("🔀 Open Git Panel", _mediator.OpenGitOverlay, accent: true, accent, _accentButtons));
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("🌱 Init Repo", _mediator.RunGitInit));
            wrap.Children.Add(WorkspaceUiFactory.CreateVerticalDivider());
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("✓ Commit All", _mediator.PromptGitCommitAll, accent: true, accent, _accentButtons));
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("⚡ Commit & Push", _mediator.PromptGitCommitAndPush, accent: true, accent, _accentButtons));
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("⬇ Pull", _mediator.RunGitPull));
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("⬆ Push", _mediator.RunGitPush));
            wrap.Children.Add(WorkspaceUiFactory.CreateVerticalDivider());
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("🌿 Branch", _mediator.PromptGitBranch));
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("🔗 Remote", _mediator.PromptGitRemote));
            wrap.Children.Add(WorkspaceUiFactory.CreateToolbarButton("↻ Refresh", () => _mediator.RefreshGitStatus()));

            _gitStatusLabel.FontSize = 11.5;
            _gitStatusLabel.VerticalAlignment = VerticalAlignment.Center;
            _gitStatusLabel.Margin = new Thickness(10, 0, 0, 0);
            _gitStatusLabel.Opacity = 0.85;
            if (_gitStatusLabel.Parent is Panel oldParent)
                oldParent.Children.Remove(_gitStatusLabel);
            wrap.Children.Add(_gitStatusLabel);
            return wrap;
        }
    }
}
