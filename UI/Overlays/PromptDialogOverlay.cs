using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NoteDraw.Services;

namespace NoteDraw.UI.Overlays
{
    internal sealed class PromptDialogOverlay : Border, IWorkspaceOverlay, IWorkspaceThemeAware
    {
        private readonly IWorkspaceMediator _mediator;
        private readonly TextBlock _title = new();
        private readonly TextBox _input = new();
        private readonly Border _card = new();
        private readonly List<Button> _accentButtons = new();
        private Action<string>? _callback;

        public PromptDialogOverlay(IWorkspaceMediator mediator)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            BuildLayout();
        }

        public bool IsOpen => IsVisible;
        public bool IsInputFocused => _input.IsFocused;

        public void Show(string title, string defaultText, Action<string> onSubmit)
        {
            _title.Text = title;
            _input.Text = defaultText;
            _callback = onSubmit;
            IsVisible = true;
            _input.Focus();
            _input.SelectAll();
        }

        public void Close() => IsVisible = false;

        public bool TryHandleEscape()
        {
            if (!IsVisible)
                return false;
            Close();
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
            _title.FontSize = 14;
            _title.FontWeight = FontWeight.Bold;
            _input.FontSize = 13;
            _input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Submit();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    IsVisible = false;
                    e.Handled = true;
                }
            };

            IBrush accent = _mediator.CurrentAccentBrush;
            var cancelBtn = WorkspaceUiFactory.CreateToolbarButton("Cancel", () => IsVisible = false);
            var confirmBtn = WorkspaceUiFactory.CreateToolbarButton(
                "Confirm",
                Submit,
                accent: true,
                accentBrush: accent,
                accentRegistry: _accentButtons);

            _card.Width = 420;
            _card.CornerRadius = new CornerRadius(10);
            _card.BorderThickness = new Thickness(1);
            _card.BorderBrush = new SolidColorBrush(Color.Parse("#3B82F6"));
            _card.Padding = new Thickness(16);
            _card.HorizontalAlignment = HorizontalAlignment.Center;
            _card.VerticalAlignment = VerticalAlignment.Center;
            _card.Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    _title,
                    _input,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelBtn, confirmBtn }
                    }
                }
            };

            IsVisible = false;
            Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
            Child = _card;
        }

        private void Submit()
        {
            string value = _input.Text?.Trim() ?? string.Empty;
            IsVisible = false;
            if (!string.IsNullOrEmpty(value))
                _callback?.Invoke(value);
        }
    }
}
