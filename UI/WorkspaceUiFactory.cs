using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NoteDraw.Services;

namespace NoteDraw.UI
{
    internal static class WorkspaceUiFactory
    {
        public static IBrush ResolveAccentBrush(ThemePalette palette, bool isDark)
            => ThemeConfigService.ParseBrush(palette.AccentColor, isDark ? "#0078D4" : "#0067C0");

        public static IBrush ResolveForegroundBrush(ThemePalette palette, bool isDark)
            => ThemeConfigService.ParseBrush(palette.TextColor, isDark ? "#E0E0E0" : "#1E1E1E");

        public static Button CreateToolbarButton(
            string label,
            Action onClick,
            bool accent = false,
            IBrush? accentBrush = null,
            ICollection<Button>? accentRegistry = null)
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
                if (accentBrush is not null)
                    btn.Background = accentBrush;
                btn.Foreground = Brushes.White;
                accentRegistry?.Add(btn);
            }
            btn.Click += (_, _) => onClick();
            return btn;
        }

        public static Button CreateCloseViewButton(
            WorkspaceViewKind view,
            string tooltip,
            Action<WorkspaceViewKind> onCloseView,
            Action? onCloseOverride = null)
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
            btn.Click += (_, _) =>
            {
                if (onCloseOverride is not null)
                    onCloseOverride();
                else
                    onCloseView(view);
            };
            return btn;
        }

        public static Border CreateViewHeader(
            string title,
            WorkspaceViewKind view,
            Action<WorkspaceViewKind> onCloseView,
            (string Label, Action Action)? extraAction = null,
            Control? rightBadge = null,
            (string Label, Action Action)? secondaryAction = null,
            IBrush? accentBrush = null,
            ICollection<Button>? accentRegistry = null)
        {
            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            return CreateViewHeaderCustom(
                titleBlock,
                view,
                onCloseView,
                extraAction,
                rightBadge,
                onCloseOverride: null,
                secondaryAction: secondaryAction,
                accentBrush: accentBrush,
                accentRegistry: accentRegistry);
        }

        public static Border CreateViewHeaderCustom(
            Control titleControl,
            WorkspaceViewKind view,
            Action<WorkspaceViewKind> onCloseView,
            (string Label, Action Action)? extraAction = null,
            Control? rightBadge = null,
            Action? onCloseOverride = null,
            (string Label, Action Action)? secondaryAction = null,
            IBrush? accentBrush = null,
            ICollection<Button>? accentRegistry = null)
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

            if (secondaryAction.HasValue)
            {
                var secBtn = CreateToolbarButton(
                    secondaryAction.Value.Label,
                    secondaryAction.Value.Action,
                    accentBrush: accentBrush,
                    accentRegistry: accentRegistry);
                secBtn.Padding = new Thickness(6, 2);
                rightStack.Children.Add(secBtn);
            }

            if (extraAction.HasValue)
            {
                var extraBtn = CreateToolbarButton(
                    extraAction.Value.Label,
                    extraAction.Value.Action,
                    accentBrush: accentBrush,
                    accentRegistry: accentRegistry);
                extraBtn.Padding = new Thickness(6, 2);
                rightStack.Children.Add(extraBtn);
            }

            rightStack.Children.Add(CreateCloseViewButton(
                view,
                $"Close {ViewLayoutState.GetDisplayName(view)} (restore from View & Mode → View)",
                onCloseView,
                onCloseOverride));

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

        public static Border CreateVerticalDivider() => new()
        {
            Width = 1,
            Height = 20,
            Margin = new Thickness(6, 2),
            Background = new SolidColorBrush(Color.FromArgb(80, 128, 128, 128)),
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}
