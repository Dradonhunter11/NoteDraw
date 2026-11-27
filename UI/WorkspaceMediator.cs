using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

        private TopBarPanel? _topBar;
        private RibbonBarPanel? _ribbonBar;
        private VaultExplorerPanel? _vaultExplorer;
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

        public void RegisterChromeAndVault(
            TopBarPanel topBar,
            RibbonBarPanel ribbonBar,
            VaultExplorerPanel vaultExplorer)
        {
            _topBar = topBar ?? throw new ArgumentNullException(nameof(topBar));
            _ribbonBar = ribbonBar ?? throw new ArgumentNullException(nameof(ribbonBar));
            _vaultExplorer = vaultExplorer ?? throw new ArgumentNullException(nameof(vaultExplorer));

            RegisterThemeAware(topBar);
            RegisterThemeAware(ribbonBar);
            RegisterThemeAware(vaultExplorer);
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
        public string? ActiveDocumentFilePath => _host.GetActiveDocumentFilePath();
        public bool AutoSaveEnabled => _host.GetAutoSaveEnabled();
        public bool HasUnsavedDocuments => _host.GetHasUnsavedDocuments();
        public ThemePalette CurrentPalette => _host.GetCurrentPalette();
        public bool IsDarkTheme => _host.GetIsDarkTheme();
        public IBrush CurrentAccentBrush => WorkspaceUiFactory.ResolveAccentBrush(CurrentPalette, IsDarkTheme);
        public bool HasOpenOverlay => _overlaysInEscapeOrder.Any(o => o.IsOpen);
        public bool IsUtilityInputFocused =>
            (_vaultExplorer?.IsSearchInputFocused == true) ||
            (_liveLatexPreview?.IsInputFocused == true) ||
            _overlaysInEscapeOrder.Any(o => o.IsInputFocused);
        public bool IsUnsavedExitOpen => _unsavedExit?.IsOpen == true;

        public bool IsViewVisible(WorkspaceViewKind view) => _host.IsViewVisible(view);
        public IReadOnlyList<VaultEntry> GetVaultEntries(string? filter = null)
            => _host.GetVaultEntries(filter);

        public void OpenFile(string fullPath) => _host.OpenFileInTab(fullPath);
        public void JumpEditorToLine(int oneBasedLine) => _host.JumpEditorToLine(oneBasedLine);

        public void FilterVaultByTag(string tag)
        {
            _host.OpenView(WorkspaceViewKind.VaultExplorer);
            _vaultExplorer?.ApplySearchFilter(tag.TrimStart('#'));
        }

        public void InsertLatexSnippet(string latexCode) => _host.InsertLatexSnippet(latexCode);
        public void InsertUtilitySnippet(string snippet) => _host.InsertUtilitySnippet(snippet);
        public void CloseView(WorkspaceViewKind view) => _host.CloseView(view);
        public void ToggleWorkspaceView(WorkspaceViewKind view) => _host.ToggleWorkspaceView(view);
        public void ResetAllViews() => _host.ResetAllViews();
        public void SetUnifiedWindowMode(bool showRawSource) => _host.SetUnifiedWindowMode(showRawSource);

        public void OpenVaultFolder() => _host.OpenVaultFolder();
        public void SaveActiveTab() => _host.SaveActiveTab();
        public void RequestSaveAllDirtyTabs() => _host.SaveAllDirtyTabs();
        public void RequestCloseWindow() => _host.RequestCloseWindow();
        public void PerformUndo() => _host.PerformUndo();
        public void PerformRedo() => _host.PerformRedo();
        public void PerformDrawingUndo() => _host.PerformDrawingUndo();
        public void PerformDrawingRedo() => _host.PerformDrawingRedo();
        public void InsertCanvasInDocument() => _host.InsertCanvasInDocument();
        public void InsertCodeBlock() => _host.InsertCodeBlock();
        public void PasteImageWithPicker() => _host.PasteImageWithPicker();
        public void ToggleAutoSave() => _host.ToggleAutoSave();
        public void ToggleTheme() => _host.ToggleTheme();
        public void OpenDarkThemeConfig() => _host.OpenDarkThemeConfig();
        public void OpenLightThemeConfig() => _host.OpenLightThemeConfig();
        public void LoadCustomThemeFile() => _host.LoadCustomThemeFile();
        public void ExportActiveDrawing(bool isSvg) => _host.ExportActiveDrawing(isSvg);

        public void PromptCreateNote() => _vaultExplorer?.PromptCreateNote();
        public void PromptCreateFolder() => _vaultExplorer?.PromptCreateFolder();
        public void PromptCreateDrawing() => _vaultExplorer?.PromptCreateDrawing();
        public void RequestRefreshVaultTree() => _vaultExplorer?.RefreshTree();
        public void CloseOpenTabsMatchingPath(string fullPath) => _host.CloseOpenTabsMatchingPath(fullPath);
        public void RemapOpenTabsAfterMove(IReadOnlyList<(string OldPath, string NewPath)> movedMappings)
            => _host.RemapOpenTabsAfterMove(movedMappings);
        public void RequestRefreshTabStrip() => _host.RefreshTabStrip();
        public void RequestRefreshStatusBar() => _host.RefreshStatusBar();
        public void BeginVaultImageDrag(string imageFullPath, Point windowPoint)
            => _host.BeginVaultImageDrag(imageFullPath, windowPoint);
        public void NotifyNonVaultPanelFocused() => _vaultExplorer?.ClearFocusFlag();

        public bool TryHandleVaultExplorerKeyDown(KeyEventArgs e, bool isTextInputFocused, bool hasModalOpen)
            => _vaultExplorer?.TryHandleKeyDown(e, isTextInputFocused, hasModalOpen) == true;

        public void ApplyUnsavedExitDecision(UnsavedExitDecision decision, bool closeWindow = false)
            => _host.ApplyUnsavedExitDecision(decision, closeWindow);

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
            _ribbonBar?.UpdateGitStatus(status);
            _host.OnGitStatusUpdated?.Invoke(status);
        }

        public void NotifyVaultNameChanged(string vaultFolderName)
            => _topBar?.SetVaultName(vaultFolderName);

        public void NotifyUndoRedoStateChanged(UndoRedoButtonState state)
            => _topBar?.UpdateUndoRedoState(state);

        public void NotifyAutoSaveChanged(bool autoSaveEnabled)
            => _topBar?.UpdateAutoSaveHeader(autoSaveEnabled);

        public void NotifyViewLayoutUpdated()
        {
            _topBar?.UpdateViewToggleLabels(_host.IsViewVisible);
            _ribbonBar?.RefreshContent();
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
