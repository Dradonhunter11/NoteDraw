using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NoteDraw.Services;

namespace NoteDraw.UI.Overlays
{
    internal sealed class GitOverlayPanel : Border, IWorkspaceOverlay, IWorkspaceThemeAware
    {
        private readonly IWorkspaceMediator _mediator;
        private readonly GitService _git;
        private readonly TextBlock _branchBadge = new();
        private readonly TextBlock _remoteLabel = new();
        private readonly TextBox _commitMessageBox = new();
        private readonly StackPanel _changesStack = new();
        private readonly StackPanel _commitsStack = new();
        private readonly TextBox _outputBox = new();
        private readonly Border _card = new();
        private readonly List<Button> _accentButtons = new();
        private GitRepositoryStatus _lastGitStatus = new();

        public GitOverlayPanel(IWorkspaceMediator mediator, GitService? gitService = null)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _git = gitService ?? new GitService();
            BuildLayout();
        }

        public bool IsOpen => IsVisible;
        public bool IsInputFocused => _commitMessageBox.IsFocused;
        internal GitRepositoryStatus LastStatus => _lastGitStatus;

        public void Open()
        {
            RefreshStatus();
            IsVisible = true;
            _commitMessageBox.Focus();
        }

        public void Close() => IsVisible = false;

        public bool TryHandleEscape()
        {
            if (!IsVisible)
                return false;
            Close();
            return true;
        }

        public void RefreshStatus(string? outputOverride = null)
        {
            _lastGitStatus = _git.GetStatus(_mediator.CurrentVaultPath);
            _mediator.NotifyGitStatusUpdated(_lastGitStatus);

            if (IsVisible || outputOverride is not null)
                RefreshOverlayContent(outputOverride);
        }

        public void RunInit()
        {
            var res = _git.InitRepository(_mediator.CurrentVaultPath);
            RefreshStatus(res.Output);
            _mediator.PostStatusMessage(res.Success ? "✓ Git repository initialized" : "⚠ Git init failed");
        }

        public void PromptCommitAll()
        {
            _mediator.ShowPrompt(
                "Git Commit All Changes",
                $"Update notes ({DateTime.Now:yyyy-MM-dd HH:mm})",
                RunCommit);
        }

        public void PromptCommitAndPush()
        {
            _mediator.ShowPrompt(
                "Git Commit & Push (Sync)",
                $"Sync notes ({DateTime.Now:yyyy-MM-dd HH:mm})",
                RunCommitAndPush);
        }

        public void RunCommit(string? message)
        {
            if (_mediator.AutoSaveEnabled || _mediator.HasUnsavedDocuments)
                _mediator.RequestSaveAllDirtyTabs();

            var res = _git.StageAndCommitAll(_mediator.CurrentVaultPath, message ?? string.Empty);
            if (res.Success)
                _commitMessageBox.Text = string.Empty;
            RefreshStatus(res.Output);
            _mediator.PostStatusMessage(res.Success ? "✓ Git commit succeeded" : "⚠ Git commit issue (see Git panel)");
        }

        public void RunCommitAndPush(string? message)
        {
            if (_mediator.AutoSaveEnabled || _mediator.HasUnsavedDocuments)
                _mediator.RequestSaveAllDirtyTabs();

            var res = _git.CommitAndPush(_mediator.CurrentVaultPath, message ?? string.Empty);
            if (res.Success)
                _commitMessageBox.Text = string.Empty;
            RefreshStatus(res.Output);
            _mediator.PostStatusMessage(res.Success ? "✓ Git commit & push succeeded" : "⚠ Git sync issue (see Git panel)");
        }

        public void RunPull()
        {
            var res = _git.Pull(_mediator.CurrentVaultPath);
            _mediator.RequestRefreshVaultTree();
            RefreshStatus(res.Output);
            _mediator.PostStatusMessage(res.Success ? "✓ Git pull completed" : "⚠ Git pull failed (see Git panel)");
        }

        public void RunPush()
        {
            var res = _git.Push(_mediator.CurrentVaultPath);
            RefreshStatus(res.Output);
            _mediator.PostStatusMessage(res.Success ? "✓ Git push completed" : "⚠ Git push failed (see Git panel)");
        }

        public void PromptBranch()
        {
            string defaultBranch = string.IsNullOrWhiteSpace(_lastGitStatus.BranchName)
                ? "main"
                : _lastGitStatus.BranchName;
            _mediator.ShowPrompt("Switch or Create Git Branch", defaultBranch, branch =>
            {
                var res = _git.SwitchOrCreateBranch(_mediator.CurrentVaultPath, branch, createNew: false);
                RefreshStatus(res.Output);
                _mediator.PostStatusMessage(res.Success ? $"✓ Branch: {branch}" : "⚠ Branch switch failed");
            });
        }

        public void PromptRemote()
        {
            string currentRemote = string.IsNullOrWhiteSpace(_lastGitStatus.RemoteUrl)
                ? "https://github.com/user/vault-notes.git"
                : _lastGitStatus.RemoteUrl;
            _mediator.ShowPrompt("Set Git 'origin' Remote URL", currentRemote, url =>
            {
                var res = _git.SetRemoteOrigin(_mediator.CurrentVaultPath, url);
                RefreshStatus(res.Output);
                _mediator.PostStatusMessage(res.Success ? "✓ Git remote updated" : "⚠ Failed to set remote");
            });
        }

        public void ApplyTheme(ThemePalette palette, bool isDark)
        {
            IBrush accent = WorkspaceUiFactory.ResolveAccentBrush(palette, isDark);
            _card.Background = ThemeConfigService.ParseBrush(palette.PanelBackground, isDark ? "#252526" : "#FFFFFF");
            _card.BorderBrush = accent;
            foreach (var btn in _accentButtons)
            {
                btn.Background = accent;
                btn.Foreground = Brushes.White;
            }
            if (IsVisible)
                PopulateChangesList();
        }

        private void BuildLayout()
        {
            _branchBadge.FontSize = 12;
            _branchBadge.FontWeight = FontWeight.SemiBold;
            _branchBadge.VerticalAlignment = VerticalAlignment.Center;
            _branchBadge.Margin = new Thickness(0, 0, 14, 0);

            _remoteLabel.FontSize = 11.5;
            _remoteLabel.Opacity = 0.8;
            _remoteLabel.VerticalAlignment = VerticalAlignment.Center;

            _commitMessageBox.PlaceholderText = "Commit message (e.g. Update vault notes)...";
            _commitMessageBox.FontSize = 12.5;
            _commitMessageBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    RunCommit(_commitMessageBox.Text);
                    e.Handled = true;
                }
            };

            _changesStack.Spacing = 4;
            _commitsStack.Spacing = 3;

            _outputBox.IsReadOnly = true;
            _outputBox.AcceptsReturn = true;
            _outputBox.TextWrapping = TextWrapping.Wrap;
            _outputBox.FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace");
            _outputBox.FontSize = 11.5;
            _outputBox.MinHeight = 100;
            _outputBox.MaxHeight = 160;
            _outputBox.Text = "Git ready. Initialize a repository or commit vault changes below.";

            var topHeader = BuildOverlayHeader();
            var statusMetaBar = new WrapPanel { Orientation = Orientation.Horizontal };
            statusMetaBar.Children.Add(_branchBadge);
            statusMetaBar.Children.Add(_remoteLabel);

            var commitRow = BuildCommitActionBar();
            var splitColumns = BuildChangesAndCommitsGrid();

            _card.Width = 760;
            _card.MaxWidth = 860;
            _card.CornerRadius = new CornerRadius(10);
            _card.BorderThickness = new Thickness(1);
            _card.BorderBrush = new SolidColorBrush(Color.Parse("#0078D4"));
            _card.Padding = new Thickness(18);
            _card.HorizontalAlignment = HorizontalAlignment.Center;
            _card.VerticalAlignment = VerticalAlignment.Center;
            _card.Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    topHeader,
                    statusMetaBar,
                    commitRow,
                    splitColumns,
                    new TextBlock { Text = "Git Output & Diff", FontSize = 11.5, FontWeight = FontWeight.SemiBold },
                    _outputBox
                }
            };

            IsVisible = false;
            Background = new SolidColorBrush(Color.FromArgb(145, 0, 0, 0));
            Child = _card;
            PointerPressed += (_, e) =>
            {
                if (ReferenceEquals(e.Source, this))
                {
                    IsVisible = false;
                    e.Handled = true;
                }
            };
        }

        private Grid BuildOverlayHeader()
        {
            var closeBtn = WorkspaceUiFactory.CreateToolbarButton("Close (Esc)", Close);
            var refreshBtn = WorkspaceUiFactory.CreateToolbarButton("↻ Refresh", () => RefreshStatus());
            var initBtn = WorkspaceUiFactory.CreateToolbarButton("🌱 Init Repo", RunInit);
            var branchBtn = WorkspaceUiFactory.CreateToolbarButton("🌿 Branch", PromptBranch);
            var remoteBtn = WorkspaceUiFactory.CreateToolbarButton("🔗 Remote", PromptRemote);

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
            return topHeader;
        }

        private Grid BuildCommitActionBar()
        {
            IBrush accent = _mediator.CurrentAccentBrush;
            var commitAllBtn = WorkspaceUiFactory.CreateToolbarButton(
                "✓ Commit All",
                () => RunCommit(_commitMessageBox.Text),
                accent: true,
                accentBrush: accent,
                accentRegistry: _accentButtons);
            var commitPushBtn = WorkspaceUiFactory.CreateToolbarButton(
                "⚡ Commit & Push",
                () => RunCommitAndPush(_commitMessageBox.Text),
                accent: true,
                accentBrush: accent,
                accentRegistry: _accentButtons);
            var pullBtn = WorkspaceUiFactory.CreateToolbarButton("⬇ Pull", RunPull);
            var pushBtn = WorkspaceUiFactory.CreateToolbarButton("⬆ Push", RunPush);

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
            commitRow.Children.Add(_commitMessageBox);
            Grid.SetColumn(commitActions, 1);
            commitRow.Children.Add(commitActions);
            return commitRow;
        }

        private Grid BuildChangesAndCommitsGrid()
        {
            var changesPanel = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = "Changed Files", FontSize = 12, FontWeight = FontWeight.SemiBold },
                    new ScrollViewer { Height = 150, Content = _changesStack }
                }
            };

            var commitsPanel = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = "Recent Commits", FontSize = 12, FontWeight = FontWeight.SemiBold },
                    new ScrollViewer { Height = 150, Content = _commitsStack }
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
            return splitColumns;
        }

        private void RefreshOverlayContent(string? outputOverride = null)
        {
            if (!_lastGitStatus.IsGitAvailable)
            {
                _branchBadge.Text = "Git CLI not found on PATH";
                _remoteLabel.Text = string.Empty;
            }
            else if (!_lastGitStatus.IsRepository)
            {
                _branchBadge.Text = $"No Git repository in {_mediator.CurrentVaultPath}";
                _remoteLabel.Text = "Click 'Init Repo' to start tracking this vault.";
            }
            else
            {
                _branchBadge.Text = $"🌿 Branch: {_lastGitStatus.BranchName}";
                _remoteLabel.Text = string.IsNullOrWhiteSpace(_lastGitStatus.RemoteUrl)
                    ? "Remote: (none configured)"
                    : $"Remote: {_lastGitStatus.RemoteUrl}";
            }

            PopulateChangesList();
            PopulateCommitsList();

            if (outputOverride is not null)
                _outputBox.Text = outputOverride;
        }

        private void PopulateChangesList()
        {
            _changesStack.Children.Clear();
            if (_lastGitStatus.Changes.Count == 0)
            {
                _changesStack.Children.Add(new TextBlock
                {
                    Text = _lastGitStatus.IsRepository ? "Working tree clean — no pending changes." : "No repository initialized.",
                    FontSize = 11.5,
                    Opacity = 0.7
                });
                return;
            }

            IBrush accent = _mediator.CurrentAccentBrush;
            string vaultPath = _mediator.CurrentVaultPath;

            foreach (var change in _lastGitStatus.Changes)
            {
                var captured = change;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 5 };
                row.Children.Add(new TextBlock
                {
                    Text = $"[{captured.StatusCode}]",
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Foreground = accent,
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

                var diffBtn = WorkspaceUiFactory.CreateToolbarButton("Diff", () =>
                {
                    _outputBox.Text = _git.GetDiff(_mediator.CurrentVaultPath, captured.RelativePath);
                });
                diffBtn.Padding = new Thickness(5, 1);
                Grid.SetColumn(diffBtn, 2);
                row.Children.Add(diffBtn);

                string fullFile = Path.Combine(vaultPath, captured.RelativePath);
                if (File.Exists(fullFile) &&
                    (fullFile.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                     fullFile.EndsWith(".ndraw", StringComparison.OrdinalIgnoreCase) ||
                     MainWindow.IsSupportedImageExtension(fullFile)))
                {
                    var openBtn = WorkspaceUiFactory.CreateToolbarButton("Open", () =>
                    {
                        IsVisible = false;
                        _mediator.OpenFile(fullFile);
                    });
                    openBtn.Padding = new Thickness(5, 1);
                    Grid.SetColumn(openBtn, 3);
                    row.Children.Add(openBtn);
                }

                _changesStack.Children.Add(row);
            }
        }

        private void PopulateCommitsList()
        {
            _commitsStack.Children.Clear();
            if (_lastGitStatus.RecentCommits.Count == 0)
            {
                _commitsStack.Children.Add(new TextBlock
                {
                    Text = "No commits yet.",
                    FontSize = 11.5,
                    Opacity = 0.7
                });
                return;
            }

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
                _commitsStack.Children.Add(stack);
            }
        }
    }
}
