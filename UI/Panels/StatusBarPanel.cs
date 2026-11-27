using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using NoteDraw.Services;

namespace NoteDraw.UI.Panels
{
    internal sealed class StatusBarPanel : Border, IWorkspaceThemeAware
    {
        private readonly IWorkspaceMediator _mediator;
        private readonly TextBlock _saveText = new();
        private readonly TextBlock _gitText = new();
        private readonly TextBlock _viewsText = new();
        private readonly TextBlock _caretText = new();
        private readonly TextBlock _latexText = new();
        private readonly TextBlock _countsText = new();

        public StatusBarPanel(IWorkspaceMediator mediator)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            BuildLayout();
        }

        internal string SaveStatusText => _saveText.Text ?? string.Empty;
        internal string GitStatusText => _gitText.Text ?? string.Empty;
        internal string CaretStatusText => _caretText.Text ?? string.Empty;
        internal string LatexStatusText => _latexText.Text ?? string.Empty;
        internal string ViewsStatusText => _viewsText.Text ?? string.Empty;
        internal string CountsStatusText => _countsText.Text ?? string.Empty;

        public void SetStatusMessage(string message) => _saveText.Text = message;
        public void SetCaretText(string caretText) => _caretText.Text = caretText;
        public void SetLatexStatus(string latexStatusText) => _latexText.Text = latexStatusText;

        public void UpdateSnapshot(StatusBarSnapshot snapshot)
        {
            _saveText.Text = snapshot.SaveText;
            _countsText.Text = snapshot.CountsText;
            if (snapshot.CaretText is not null)
                _caretText.Text = snapshot.CaretText;
            _viewsText.Text = snapshot.ViewsText;
        }

        public void UpdateGitStatus(GitRepositoryStatus status)
        {
            if (!status.IsGitAvailable)
            {
                _gitText.Text = "Git: CLI missing";
                return;
            }

            if (!status.IsRepository)
            {
                _gitText.Text = "Git: No repo";
                return;
            }

            int changedCount = status.Changes.Count;
            _gitText.Text = changedCount > 0
                ? $"🌿 {status.BranchName} ({changedCount} changed)"
                : $"🌿 {status.BranchName} ✓";
        }

        public void ApplyTheme(ThemePalette palette, bool isDark)
        {
            Background = ThemeConfigService.ParseBrush(palette.ChromeBackground, isDark ? "#1E1E1E" : "#F3F3F3");
            BorderBrush = ThemeConfigService.ParseBrush(palette.BorderColor, isDark ? "#3C3C3C" : "#D4D4D4");
        }

        private void BuildLayout()
        {
            _gitText.Cursor = new Cursor(StandardCursorType.Hand);
            _gitText.PointerPressed += (_, _) => _mediator.OpenGitOverlay();
            ToolTip.SetTip(_gitText, $"Open Git Source Control Panel ({PlatformShortcuts.Format("Shift+G")})");

            var left = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _saveText, _gitText, _viewsText, _caretText, _latexText }
            };

            var closeBtn = WorkspaceUiFactory.CreateCloseViewButton(
                WorkspaceViewKind.StatusBar,
                "Close status bar",
                _mediator.CloseView);

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _countsText, closeBtn }
            };

            foreach (var tb in new[] { _saveText, _gitText, _viewsText, _caretText, _latexText, _countsText })
                tb.FontSize = 11.5;

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(left);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            Padding = new Thickness(12, 4);
            BorderThickness = new Thickness(0, 1, 0, 0);
            Child = grid;
        }
    }
}
