using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NoteDraw.Services;

namespace NoteDraw.UI.Panels
{
    internal sealed class OutlineAndTagsPanel : Border, IWorkspaceThemeAware
    {
        private readonly IWorkspaceMediator _mediator;
        private readonly StackPanel _outlineStack = new();
        private readonly WrapPanel _tagsWrap = new();
        private ThemePalette _currentPalette = ThemePalette.CreateClassicDark();
        private bool _isDarkTheme = true;
        private string _lastMarkdown = string.Empty;

        public OutlineAndTagsPanel(IWorkspaceMediator mediator)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            BuildLayout();
            AddHandler(
                PointerPressedEvent,
                (_, _) => _mediator.NotifyNonVaultPanelFocused(),
                Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        public void Refresh(string markdown)
        {
            _lastMarkdown = markdown ?? string.Empty;
            PopulateOutline(_lastMarkdown);
            PopulateTags(_lastMarkdown);
        }

        public void ApplyTheme(ThemePalette palette, bool isDark)
        {
            _currentPalette = palette;
            _isDarkTheme = isDark;
            Background = ThemeConfigService.ParseBrush(palette.ChromeBackground, isDark ? "#1E1E1E" : "#F3F3F3");
            BorderBrush = ThemeConfigService.ParseBrush(palette.BorderColor, isDark ? "#3C3C3C" : "#D4D4D4");
            PopulateTags(_lastMarkdown);
        }

        private void BuildLayout()
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
            container.Children.Add(WorkspaceUiFactory.CreateViewHeader(
                "OUTLINE & TAGS",
                WorkspaceViewKind.OutlineTags,
                _mediator.CloseView));
            Grid.SetRow(body, 1);
            container.Children.Add(body);

            BorderThickness = new Thickness(0, 0, 1, 0);
            Child = container;
        }

        private void PopulateOutline(string markdown)
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
                return;
            }

            foreach (var item in outline)
            {
                int targetLine = item.LineNumber;
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
                itemBtn.Click += (_, _) => _mediator.JumpEditorToLine(targetLine);
                _outlineStack.Children.Add(itemBtn);
            }
        }

        private void PopulateTags(string markdown)
        {
            _tagsWrap.Children.Clear();
            var tags = VaultService.ExtractTags(markdown);
            IBrush tagBg = ThemeConfigService.ParseBrush(_currentPalette.TagTintColor, _isDarkTheme ? "#233B23" : "#DFF6DD");
            IBrush tagFg = ThemeConfigService.ParseBrush(_currentPalette.TagColor, _isDarkTheme ? "#6CCB5F" : "#107C10");

            foreach (string tag in tags)
            {
                string captured = tag;
                var tagBtn = new Button
                {
                    Content = captured,
                    FontSize = 11,
                    Padding = new Thickness(6, 2),
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(9),
                    Background = tagBg,
                    Foreground = tagFg
                };
                tagBtn.Click += (_, _) => _mediator.FilterVaultByTag(captured);
                _tagsWrap.Children.Add(tagBtn);
            }
        }
    }
}
