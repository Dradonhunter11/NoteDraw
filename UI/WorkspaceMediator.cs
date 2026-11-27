using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using NoteDraw.Services;
using NoteDraw.UI.Overlays;
using NoteDraw.UI.Panels;

namespace NoteDraw.UI
{
    internal sealed class WorkspaceMediator : IWorkspaceMediator
    {
        private readonly WorkspaceHostCallbacks _host;
        private readonly List<IWorkspaceThemeAware> _themeAwareComponents = new();
        private readonly List<IWorkspaceOverlay> _overlaysInEscapeOrder = new();
        private readonly List<Control> _overlayControlsInZOrder = new();

        private StatusBarPanel? _statusBar;
        private OutlineAndTagsPanel? _outlineAndTags;
        private LiveLatexPreviewPanel? _liveLatexPreview;

        private UnsavedExitOverlay? _unsavedExit;
        private PromptDialogOverlay? _promptDialog;
        private QuickSwitcherOverlay? _quickSwitcher;
        private GitOverlayPanel? _gitOverlay;
        private SyntaxGuideOverlay? _syntaxGuide;

        public WorkspaceMediator(WorkspaceHostCallbacks host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public void RegisterPanels(
            StatusBarPanel statusBar,
            OutlineAndTagsPanel outlineAndTags,
            LiveLatexPreviewPanel liveLatexPreview)
        {
            _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
            _outlineAndTags = outlineAndTags ?? throw new ArgumentNullException(nameof(outlineAndTags));
            _liveLatexPreview = liveLatexPreview ?? throw new ArgumentNullException(nameof(liveLatexPreview));

            RegisterThemeAware(statusBar);
            RegisterThemeAware(outlineAndTags);
            RegisterThemeAware(liveLatexPreview);
        }

        public void RegisterOverlays(
            UnsavedExitOverlay unsavedExit,
            PromptDialogOverlay promptDialog,
            QuickSwitcherOverlay quickSwitcher,
            GitOverlayPanel gitOverlay,
            SyntaxGuideOverlay syntaxGuide)
        {
            _unsavedExit = unsavedExit ?? throw new ArgumentNullException(nameof(unsavedExit));
            _promptDialog = promptDialog ?? throw new ArgumentNullException(nameof(promptDialog));
            _quickSwitcher = quickSwitcher ?? throw new ArgumentNullException(nameof(quickSwitcher));
            _gitOverlay = gitOverlay ?? throw new ArgumentNullException(nameof(gitOverlay));
            _syntaxGuide = syntaxGuide ?? throw new ArgumentNullException(nameof(syntaxGuide));

            _overlaysInEscapeOrder.Clear();
            _overlaysInEscapeOrder.AddRange(new IWorkspaceOverlay[]
            {
                unsavedExit,
                promptDialog,
                quickSwitcher,
                gitOverlay,
                syntaxGuide
            });

            // Preserve original z-order from MainWindow.BuildWorkspaceLayout
            _overlayControlsInZOrder.Clear();
            _overlayControlsInZOrder.AddRange(new Control[]
            {
                syntaxGuide,
                quickSwitcher,
                promptDialog,
                unsavedExit,
                gitOverlay
            });

            RegisterThemeAware(syntaxGuide);
            RegisterThemeAware(quickSwitcher);
            RegisterThemeAware(promptDialog);
            RegisterThemeAware(unsavedExit);
            RegisterThemeAware(gitOverlay);
        }

        public IReadOnlyList<Control> OverlayControls => _overlayControlsInZOrder;

        public string CurrentVaultPath => _host.GetCurrentVaultPath();
        public bool AutoSaveEnabled => _host.GetAutoSaveEnabled();
        public bool HasUnsavedDocuments => _host.GetHasUnsavedDocuments();
        public ThemePalette CurrentPalette => _host.GetCurrentPalette();
        public bool IsDarkTheme => _host.GetIsDarkTheme();
        public IBrush CurrentAccentBrush => WorkspaceUiFactory.ResolveAccentBrush(CurrentPalette, IsDarkTheme);
        public bool HasOpenOverlay => _overlaysInEscapeOrder.Any(o => o.IsOpen);
        public bool IsUtilityInputFocused =>
            (_liveLatexPreview?.IsInputFocused == true) ||
            _overlaysInEscapeOrder.Any(o => o.IsInputFocused);
        public bool IsUnsavedExitOpen => _unsavedExit?.IsOpen == true;

        public IReadOnlyList<VaultEntry> GetVaultEntries(string? filter = null)
            => _host.GetVaultEntries(filter);

        public void OpenFile(string fullPath) => _host.OpenFileInTab(fullPath);
        public void JumpEditorToLine(int oneBasedLine) => _host.JumpEditorToLine(oneBasedLine);
        public void FilterVaultByTag(string tag) => _host.FilterVaultByTag(tag);
        public void InsertLatexSnippet(string latexCode) => _host.InsertLatexSnippet(latexCode);
        public void CloseView(WorkspaceViewKind view) => _host.CloseView(view);
        public void RequestRefreshVaultTree() => _host.RefreshVaultTree();
        public void RequestSaveAllDirtyTabs() => _host.SaveAllDirtyTabs();
        public void ApplyUnsavedExitDecision(UnsavedExitDecision decision, bool closeWindow = false)
            => _host.ApplyUnsavedExitDecision(decision, closeWindow);
        public void NotifyNonVaultPanelFocused() => _host.NotifyNonVaultPanelFocused();

        public void PostStatusMessage(string message) => _statusBar?.SetStatusMessage(message);
        public void UpdateCaretStatus(string caretText) => _statusBar?.SetCaretText(caretText);
        public void UpdateLatexStatus(string latexStatusText) => _statusBar?.SetLatexStatus(latexStatusText);
        public void NotifyStatusBarRefresh(StatusBarSnapshot snapshot) => _statusBar?.UpdateSnapshot(snapshot);

        public void NotifyThemeChanged(ThemePalette palette, bool isDark)
        {
            foreach (var component in _themeAwareComponents)
                component.ApplyTheme(palette, isDark);
        }

        public void NotifyDocumentContentChanged(string markdown)
            => _outlineAndTags?.Refresh(markdown);

        public void NotifyEditorCursorChanged(string editorText, int caretIndex, bool isMarkdownDocument)
            => _liveLatexPreview?.UpdateFromCursor(editorText, caretIndex, isMarkdownDocument);

        public void NotifyGitStatusUpdated(GitRepositoryStatus status)
        {
            _statusBar?.UpdateGitStatus(status);
            _host.OnGitStatusUpdated(status);
        }

        public void OpenGitOverlay() => _gitOverlay?.Open();
        public void RefreshGitStatus(string? outputOverride = null) => _gitOverlay?.RefreshStatus(outputOverride);
        public void RunGitInit() => _gitOverlay?.RunInit();
        public void PromptGitCommitAll() => _gitOverlay?.PromptCommitAll();
        public void PromptGitCommitAndPush() => _gitOverlay?.PromptCommitAndPush();
        public void RunGitPull() => _gitOverlay?.RunPull();
        public void RunGitPush() => _gitOverlay?.RunPush();
        public void PromptGitBranch() => _gitOverlay?.PromptBranch();
        public void PromptGitRemote() => _gitOverlay?.PromptRemote();

        public void OpenQuickSwitcher() => _quickSwitcher?.Open();
        public void ShowPrompt(string title, string defaultText, Action<string> onSubmit)
            => _promptDialog?.Show(title, defaultText, onSubmit);
        public void ToggleSyntaxGuide() => _syntaxGuide?.Toggle();
        public void ShowUnsavedExitDialog(IReadOnlyList<string> dirtyDocumentTitles)
            => _unsavedExit?.Show(dirtyDocumentTitles);
        public void HideUnsavedExitDialog() => _unsavedExit?.Hide();

        public bool TryDismissTopOverlay()
        {
            foreach (var overlay in _overlaysInEscapeOrder)
            {
                if (overlay.TryHandleEscape())
                    return true;
            }
            return false;
        }

        private void RegisterThemeAware(IWorkspaceThemeAware component)
        {
            if (!_themeAwareComponents.Contains(component))
                _themeAwareComponents.Add(component);
        }
    }
}
