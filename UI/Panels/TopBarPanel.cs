using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NoteDraw.Services;

namespace NoteDraw.UI.Panels
{
    internal sealed class TopBarPanel : Border, IWorkspaceThemeAware
    {
        private readonly IWorkspaceMediator _mediator;
        private readonly Menu _menuBar = new();
        private readonly MenuItem _autoSaveMenuItem = new();
        private readonly TextBlock _vaultNameLabel = new();
        private readonly Button _undoBtn = new();
        private readonly Button _redoBtn = new();
        private readonly Button _themeBtn = new();
        private readonly Dictionary<WorkspaceViewKind, MenuItem> _viewToggleMenuItems = new();
        private readonly List<Button> _accentButtons = new();

        public TopBarPanel(IWorkspaceMediator mediator)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            BuildLayout();
        }

        internal Menu MenuBar => _menuBar;
        internal string VaultNameText => _vaultNameLabel.Text ?? string.Empty;
        internal string AutoSaveHeaderText => _autoSaveMenuItem.Header?.ToString() ?? string.Empty;
        internal bool IsUndoEnabled => _undoBtn.IsEnabled;
        internal bool IsRedoEnabled => _redoBtn.IsEnabled;
        internal string UndoLabel => _undoBtn.Content?.ToString() ?? string.Empty;
        internal string RedoLabel => _redoBtn.Content?.ToString() ?? string.Empty;

        public void SetVaultName(string vaultFolderName)
            => _vaultNameLabel.Text = $"Vault: {vaultFolderName}";

        public void UpdateAutoSaveHeader(bool autoSaveEnabled)
            => _autoSaveMenuItem.Header = autoSaveEnabled
                ? "[✓] Auto-Save Documents While Editing"
                : "[ ] Auto-Save Documents While Editing";

        public void UpdateViewToggleLabels(Func<WorkspaceViewKind, bool> isVisible)
        {
            foreach (var (view, item) in _viewToggleMenuItems)
            {
                bool open = isVisible(view);
                item.Header = $"{(open ? "[✓]" : "[ ]")} {ViewLayoutState.GetDisplayName(view)}";
            }
        }

        public void UpdateUndoRedoState(UndoRedoButtonState state)
        {
            _undoBtn.IsEnabled = state.CanUndo;
            _redoBtn.IsEnabled = state.CanRedo;
            _undoBtn.Content = state.UndoLabel;
            _redoBtn.Content = state.RedoLabel;
        }

        public void ApplyTheme(ThemePalette palette, bool isDark)
        {
            Background = ThemeConfigService.ParseBrush(palette.ChromeBackground, isDark ? "#1E1E1E" : "#F3F3F3");
            BorderBrush = ThemeConfigService.ParseBrush(palette.BorderColor, isDark ? "#3C3C3C" : "#D4D4D4");
            _themeBtn.Content = isDark ? "Light Mode" : "Dark Mode";

            IBrush accent = WorkspaceUiFactory.ResolveAccentBrush(palette, isDark);
            foreach (var btn in _accentButtons)
            {
                btn.Background = accent;
                btn.Foreground = Brushes.White;
            }
        }

        private void BuildLayout()
        {
            _menuBar.Items.Clear();
            _menuBar.Background = Brushes.Transparent;
            _menuBar.FontSize = 12.5;
            _menuBar.VerticalAlignment = VerticalAlignment.Center;
            _menuBar.Items.Add(BuildFileMenu());
            _menuBar.Items.Add(BuildEditAndInsertMenu());
            _menuBar.Items.Add(BuildViewAndModeMenu());
            _menuBar.Items.Add(BuildOptionsMenu());
            _menuBar.Items.Add(BuildGitMenu());
            _menuBar.Items.Add(BuildThemeMenu());

            var toolbarWrap = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            AddQuickActionButtons(toolbarWrap);

            Padding = new Thickness(12, 5);
            BorderThickness = new Thickness(0, 0, 0, 1);
            Child = toolbarWrap;
        }

        private static MenuItem MakeMenuItem(string header, Action onClick)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => onClick();
            return item;
        }

        private MenuItem BuildFileMenu()
        {
            var fileMenu = new MenuItem { Header = "File" };
            fileMenu.Items.Add(MakeMenuItem("Open Vault Folder...", _mediator.OpenVaultFolder));
            fileMenu.Items.Add(MakeMenuItem($"New Note ({PlatformShortcuts.Format("N")})", _mediator.PromptCreateNote));
            fileMenu.Items.Add(MakeMenuItem("New Folder...", _mediator.PromptCreateFolder));
            fileMenu.Items.Add(MakeMenuItem($"New Standalone Drawing ({PlatformShortcuts.Format("Shift+D")})", _mediator.PromptCreateDrawing));
            fileMenu.Items.Add(new Separator());
            fileMenu.Items.Add(MakeMenuItem($"Save ({PlatformShortcuts.Format("S")})", _mediator.SaveActiveTab));
            fileMenu.Items.Add(MakeMenuItem($"Save All ({PlatformShortcuts.Format("Shift+S")})", _mediator.RequestSaveAllDirtyTabs));
            fileMenu.Items.Add(MakeMenuItem($"Quick Switcher ({PlatformShortcuts.Format("P")})", _mediator.OpenQuickSwitcher));
            fileMenu.Items.Add(new Separator());
            fileMenu.Items.Add(MakeMenuItem("Exit", _mediator.RequestCloseWindow));
            return fileMenu;
        }

        private MenuItem BuildEditAndInsertMenu()
        {
            var editInsertMenu = new MenuItem { Header = "Edit & Insert" };
            editInsertMenu.Items.Add(MakeMenuItem($"↶ Undo ({PlatformShortcuts.Format("Z")})", _mediator.PerformUndo));
            editInsertMenu.Items.Add(MakeMenuItem($"↷ Redo ({PlatformShortcuts.Format("Y")})", _mediator.PerformRedo));
            editInsertMenu.Items.Add(new Separator());
            editInsertMenu.Items.Add(MakeMenuItem("+ Insert Canvas in Document", _mediator.InsertCanvasInDocument));
            editInsertMenu.Items.Add(MakeMenuItem("</ > Insert Code Block", _mediator.InsertCodeBlock));
            editInsertMenu.Items.Add(MakeMenuItem($"📋 Paste Image ({PlatformShortcuts.Format("V")})", _mediator.PasteImageWithPicker));
            return editInsertMenu;
        }

        private MenuItem BuildViewAndModeMenu()
        {
            var viewModeMenu = new MenuItem { Header = "View & Mode" };
            viewModeMenu.Items.Add(MakeMenuItem("✨ Unified Live View", () => _mediator.SetUnifiedWindowMode(showRawSource: false)));
            viewModeMenu.Items.Add(MakeMenuItem("📄 Raw Source Mode (H1–H5 Fold)", () => _mediator.SetUnifiedWindowMode(showRawSource: true)));
            viewModeMenu.Items.Add(new Separator());

            _viewToggleMenuItems.Clear();
            var workspaceViewsSubMenu = new MenuItem { Header = "View" };
            foreach (WorkspaceViewKind view in Enum.GetValues<WorkspaceViewKind>())
            {
                var targetView = view;
                var item = new MenuItem();
                item.Click += (_, _) => _mediator.ToggleWorkspaceView(targetView);
                _viewToggleMenuItems[targetView] = item;
                workspaceViewsSubMenu.Items.Add(item);
            }
            UpdateViewToggleLabels(_mediator.IsViewVisible);
            viewModeMenu.Items.Add(workspaceViewsSubMenu);
            viewModeMenu.Items.Add(MakeMenuItem("Reset All Views", _mediator.ResetAllViews));
            return viewModeMenu;
        }

        private MenuItem BuildOptionsMenu()
        {
            var optionsMenu = new MenuItem { Header = "Options" };
            UpdateAutoSaveHeader(_mediator.AutoSaveEnabled);
            _autoSaveMenuItem.Click += (_, _) => _mediator.ToggleAutoSave();
            optionsMenu.Items.Add(_autoSaveMenuItem);
            return optionsMenu;
        }

        private MenuItem BuildGitMenu()
        {
            var gitMenu = new MenuItem { Header = "Git" };
            gitMenu.Items.Add(MakeMenuItem($"🔀 Git Source Control Panel... ({PlatformShortcuts.Format("Shift+G")})", _mediator.OpenGitOverlay));
            gitMenu.Items.Add(new Separator());
            gitMenu.Items.Add(MakeMenuItem("✓ Commit All Changes...", _mediator.PromptGitCommitAll));
            gitMenu.Items.Add(MakeMenuItem("⚡ Commit & Push (Sync)", _mediator.PromptGitCommitAndPush));
            gitMenu.Items.Add(MakeMenuItem("⬇ Git Pull", _mediator.RunGitPull));
            gitMenu.Items.Add(MakeMenuItem("⬆ Git Push", _mediator.RunGitPush));
            gitMenu.Items.Add(new Separator());
            gitMenu.Items.Add(MakeMenuItem("🌱 Initialize Git Repo in Vault", _mediator.RunGitInit));
            gitMenu.Items.Add(MakeMenuItem("🌿 Switch / Create Branch...", _mediator.PromptGitBranch));
            gitMenu.Items.Add(MakeMenuItem("🔗 Set Remote Origin URL...", _mediator.PromptGitRemote));
            return gitMenu;
        }

        private MenuItem BuildThemeMenu()
        {
            var themeMenu = new MenuItem { Header = "Theme" };
            themeMenu.Items.Add(MakeMenuItem("Switch Dark / Light Mode", _mediator.ToggleTheme));
            themeMenu.Items.Add(new Separator());
            themeMenu.Items.Add(MakeMenuItem("Edit dark.json", _mediator.OpenDarkThemeConfig));
            themeMenu.Items.Add(MakeMenuItem("Edit light.json", _mediator.OpenLightThemeConfig));
            themeMenu.Items.Add(MakeMenuItem("Load Custom Theme (.json)...", _mediator.LoadCustomThemeFile));
            return themeMenu;
        }

        private void AddQuickActionButtons(WrapPanel toolbarWrap)
        {
            void AddWrapped(Control ctrl, double rightMargin = 6)
            {
                ctrl.Margin = new Thickness(0, 2, rightMargin, 2);
                ctrl.VerticalAlignment = VerticalAlignment.Center;
                toolbarWrap.Children.Add(ctrl);
            }

            IBrush accent = _mediator.CurrentAccentBrush;

            AddWrapped(new TextBlock
            {
                Text = "NoteDraw",
                FontSize = 16,
                FontWeight = FontWeight.Bold
            }, rightMargin: 8);

            AddWrapped(_menuBar, rightMargin: 8);

            _vaultNameLabel.FontSize = 12;
            AddWrapped(_vaultNameLabel, rightMargin: 10);

            AddWrapped(WorkspaceUiFactory.CreatePillButton("+ Note", _mediator.PromptCreateNote, accent: true, accent, _accentButtons));
            AddWrapped(WorkspaceUiFactory.CreatePillButton("+ Canvas in Doc", _mediator.InsertCanvasInDocument, accent: true, accent, _accentButtons));
            AddWrapped(WorkspaceUiFactory.CreatePillButton("📋 Paste Image", _mediator.PasteImageWithPicker));
            AddWrapped(WorkspaceUiFactory.CreatePillButton("Save", _mediator.SaveActiveTab));

            _undoBtn.Content = "↶ Undo";
            _undoBtn.FontSize = 12;
            _undoBtn.FontWeight = FontWeight.SemiBold;
            _undoBtn.Padding = new Thickness(10, 5);
            _undoBtn.CornerRadius = new CornerRadius(6);
            _undoBtn.Click += (_, _) => _mediator.PerformUndo();
            ToolTip.SetTip(_undoBtn, $"Undo ({PlatformShortcuts.Format("Z")})");
            AddWrapped(_undoBtn);

            _redoBtn.Content = "↷ Redo";
            _redoBtn.FontSize = 12;
            _redoBtn.FontWeight = FontWeight.SemiBold;
            _redoBtn.Padding = new Thickness(10, 5);
            _redoBtn.CornerRadius = new CornerRadius(6);
            _redoBtn.Click += (_, _) => _mediator.PerformRedo();
            ToolTip.SetTip(_redoBtn, $"Redo ({PlatformShortcuts.Format("Y")} / {PlatformShortcuts.Format("Shift+Z")})");
            AddWrapped(_redoBtn);

            _themeBtn.Content = "Light Mode";
            _themeBtn.FontSize = 12;
            _themeBtn.Padding = new Thickness(10, 5);
            _themeBtn.CornerRadius = new CornerRadius(6);
            _themeBtn.Click += (_, _) => _mediator.ToggleTheme();
            AddWrapped(_themeBtn);

            var helpBtn = WorkspaceUiFactory.CreatePillButton("?", _mediator.ToggleSyntaxGuide);
            helpBtn.FontWeight = FontWeight.Bold;
            helpBtn.FontSize = 13;
            helpBtn.MinWidth = 28;
            helpBtn.Padding = new Thickness(9, 4);
            ToolTip.SetTip(helpBtn, "Markdown & LaTeX Syntax Guide (F1)");
            AddWrapped(helpBtn, rightMargin: 0);
        }
    }
}
