using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NoteDraw.Rendering;
using NoteDraw.Services;

namespace NoteDraw.UI.Overlays
{
    internal sealed class SyntaxGuideOverlay : Border, IWorkspaceOverlay, IWorkspaceThemeAware
    {
        private readonly SyntaxGuideSubMenuControl _popup = new(maxListHeight: 470);

        public SyntaxGuideOverlay()
        {
            BuildLayout();
        }

        public bool IsOpen => IsVisible;
        public bool IsInputFocused => false;

        public void Show()
        {
            IsVisible = true;
            _popup.FocusSearchBox();
        }

        public void Hide() => IsVisible = false;

        public void Toggle()
        {
            if (IsVisible)
                Hide();
            else
                Show();
        }

        public bool TryHandleEscape()
        {
            if (!IsVisible)
                return false;
            Hide();
            return true;
        }

        public void ApplyTheme(ThemePalette palette, bool isDark)
            => _popup.ApplyThemePalette(palette, isDark);

        private void BuildLayout()
        {
            _popup.Width = 680;
            _popup.MaxWidth = 740;
            _popup.HorizontalAlignment = HorizontalAlignment.Right;
            _popup.VerticalAlignment = VerticalAlignment.Top;
            _popup.Margin = new Thickness(0, 56, 20, 0);
            _popup.CloseRequested += (_, _) => IsVisible = false;

            IsVisible = false;
            Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
            Child = _popup;
            PointerPressed += (_, e) =>
            {
                if (ReferenceEquals(e.Source, this))
                {
                    IsVisible = false;
                    e.Handled = true;
                }
            };
        }
    }
}
