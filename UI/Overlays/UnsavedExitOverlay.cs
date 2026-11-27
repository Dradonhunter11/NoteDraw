using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NoteDraw.Services;

namespace NoteDraw.UI.Overlays
{
    internal sealed class UnsavedExitOverlay : Border, IWorkspaceOverlay, IWorkspaceThemeAware
    {
        private readonly IWorkspaceMediator _mediator;
        private readonly TextBlock _messageText = new();
        private readonly Border _card = new();
        private readonly List<Button> _accentButtons = new();

        public UnsavedExitOverlay(IWorkspaceMediator mediator)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            BuildLayout();
        }

        public bool IsOpen => IsVisible;
        public bool IsInputFocused => false;

        public void Show(IReadOnlyList<string> dirtyDocumentTitles)
        {
            string listing = dirtyDocumentTitles.Count > 0
                ? string.Join("\n", dirtyDocumentTitles)
                : "• Active document";

            _messageText.Text =
                $"You have unsaved changes (Auto-Save is turned off):\n\n{listing}\n\nDo you want to save your changes before exiting?";
            IsVisible = true;
        }

        public void Hide() => IsVisible = false;

        public bool TryHandleEscape()
        {
            if (!IsVisible)
                return false;
            _mediator.ApplyUnsavedExitDecision(UnsavedExitDecision.Cancel, closeWindow: false);
            return true;
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
        }

        private void BuildLayout()
        {
            _messageText.FontSize = 13;
            _messageText.TextWrapping = TextWrapping.Wrap;

            IBrush accent = _mediator.CurrentAccentBrush;
            var saveBtn = WorkspaceUiFactory.CreateToolbarButton(
                "Save Unsaved Work",
                () => _mediator.ApplyUnsavedExitDecision(UnsavedExitDecision.Save, closeWindow: true),
                accent: true,
                accentBrush: accent,
                accentRegistry: _accentButtons);
            saveBtn.Padding = new Thickness(12, 6);
            saveBtn.Margin = new Thickness(0, 0, 8, 4);

            var discardBtn = WorkspaceUiFactory.CreateToolbarButton(
                "Discard Unsaved Work",
                () => _mediator.ApplyUnsavedExitDecision(UnsavedExitDecision.Discard, closeWindow: true));
            discardBtn.Padding = new Thickness(12, 6);
            discardBtn.Margin = new Thickness(0, 0, 8, 4);

            var cancelBtn = WorkspaceUiFactory.CreateToolbarButton(
                "Cancel",
                () => _mediator.ApplyUnsavedExitDecision(UnsavedExitDecision.Cancel, closeWindow: false));
            cancelBtn.Padding = new Thickness(12, 6);
            cancelBtn.Margin = new Thickness(0, 0, 0, 4);

            _card.Width = 480;
            _card.CornerRadius = new CornerRadius(10);
            _card.BorderThickness = new Thickness(1);
            _card.BorderBrush = new SolidColorBrush(Color.Parse("#0078D4"));
            _card.Padding = new Thickness(20);
            _card.HorizontalAlignment = HorizontalAlignment.Center;
            _card.VerticalAlignment = VerticalAlignment.Center;
            _card.Child = new StackPanel
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
                    _messageText,
                    new WrapPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { saveBtn, discardBtn, cancelBtn }
                    }
                }
            };

            IsVisible = false;
            Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
            Child = _card;
        }
    }
}
