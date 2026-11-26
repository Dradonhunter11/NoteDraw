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

            var topHeader = BuildGitOverlayHeader();
            var statusMetaBar = new WrapPanel { Orientation = Orientation.Horizontal };
            statusMetaBar.Children.Add(_gitBranchBadge.Also(b => b.Margin = new Thickness(0, 0, 14, 0)));
            statusMetaBar.Children.Add(_gitRemoteLabel);

            var commitRow = BuildGitCommitActionBar();
            var splitColumns = BuildGitChangesAndCommitsGrid();

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

        private Grid BuildGitOverlayHeader()
        {
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
            return topHeader;
        }

        private Grid BuildGitCommitActionBar()
        {
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
            return commitRow;
        }

        private Grid BuildGitChangesAndCommitsGrid()
        {
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
            return splitColumns;
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

            PopulateGitChangesList();
            PopulateGitCommitsList();

            if (outputOverride is not null)
                _gitOutputBox.Text = outputOverride;
        }

        private void PopulateGitChangesList()
        {
            _gitChangesStack.Children.Clear();
            if (_lastGitStatus.Changes.Count == 0)
            {
                _gitChangesStack.Children.Add(new TextBlock
                {
                    Text = _lastGitStatus.IsRepository ? "Working tree clean — no pending changes." : "No repository initialized.",
                    FontSize = 11.5,
                    Opacity = 0.7
                });
                return;
            }

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

        private void PopulateGitCommitsList()
        {
            _gitCommitsStack.Children.Clear();
            if (_lastGitStatus.RecentCommits.Count == 0)
            {
                _gitCommitsStack.Children.Add(new TextBlock
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
                _gitCommitsStack.Children.Add(stack);
            }
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
    }
}
