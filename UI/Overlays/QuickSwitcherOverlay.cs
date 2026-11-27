using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NoteDraw.Services;

namespace NoteDraw.UI.Overlays
{
    internal sealed class QuickSwitcherOverlay : Border, IWorkspaceOverlay, IWorkspaceThemeAware
    {
        private readonly IWorkspaceMediator _mediator;
        private readonly TextBox _input = new();
        private readonly StackPanel _results = new();
        private readonly Border _card = new();

        public QuickSwitcherOverlay(IWorkspaceMediator mediator)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            BuildLayout();
        }

        public bool IsOpen => IsVisible;
        public bool IsInputFocused => _input.IsFocused;

        public void Open()
        {
            _input.Text = string.Empty;
            PopulateResults();
            IsVisible = true;
            _input.Focus();
        }

        public void Hide() => IsVisible = false;

        public bool TryHandleEscape()
        {
            if (!IsVisible)
                return false;
            Hide();
            return true;
        }

        public void ApplyTheme(ThemePalette palette, bool isDark)
        {
            _card.Background = ThemeConfigService.ParseBrush(palette.PanelBackground, isDark ? "#252526" : "#FFFFFF");
            _card.BorderBrush = WorkspaceUiFactory.ResolveAccentBrush(palette, isDark);
        }

        private void BuildLayout()
        {
            _input.PlaceholderText = "Jump to note or drawing by name... (Esc to close)";
            _input.FontSize = 13.5;
            _input.TextChanged += (_, _) => PopulateResults();
            _input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    IsVisible = false;
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    var firstBtn = _results.Children.OfType<Button>().FirstOrDefault();
                    firstBtn?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    e.Handled = true;
                }
            };

            _results.Spacing = 4;

            var closeBtn = new Button
            {
                Content = "Close (Esc)",
                HorizontalAlignment = HorizontalAlignment.Right,
                FontSize = 11
            };
            closeBtn.Click += (_, _) => IsVisible = false;

            _card.Width = 520;
            _card.MaxHeight = 420;
            _card.CornerRadius = new CornerRadius(10);
            _card.BorderThickness = new Thickness(1);
            _card.BorderBrush = new SolidColorBrush(Color.Parse("#3B82F6"));
            _card.Padding = new Thickness(14);
            _card.HorizontalAlignment = HorizontalAlignment.Center;
            _card.VerticalAlignment = VerticalAlignment.Top;
            _card.Margin = new Thickness(0, 72, 0, 0);
            _card.Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Quick Switcher", FontSize = 14, FontWeight = FontWeight.Bold },
                    _input,
                    new ScrollViewer { MaxHeight = 290, Content = _results },
                    closeBtn
                }
            };

            IsVisible = false;
            Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
            Child = _card;
        }

        private void PopulateResults()
        {
            _results.Children.Clear();
            var entries = _mediator.GetVaultEntries(_input.Text)
                .Where(e => e.Kind != VaultEntryKind.Folder)
                .Take(15);

            foreach (var entry in entries)
            {
                string icon = entry.Kind == VaultEntryKind.DrawingFile ? "🎨" : "📄";
                string fullPath = entry.FullPath;
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
                    IsVisible = false;
                    _mediator.OpenFile(fullPath);
                };
                _results.Children.Add(btn);
            }
        }
    }
}
