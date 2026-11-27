using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using NoteDraw.Services;

namespace NoteDraw.UI
{
    public interface IWorkspaceThemeAware
    {
        void ApplyTheme(ThemePalette palette, bool isDark);
    }

    public interface IWorkspaceOverlay
    {
        bool IsOpen { get; }
        bool IsInputFocused { get; }
        bool TryHandleEscape();
    }

    public sealed record StatusBarSnapshot(
        string SaveText,
        string CountsText,
        string? CaretText,
        string ViewsText);

    public sealed record UndoRedoButtonState(
        bool CanUndo,
        bool CanRedo,
        string UndoLabel,
        string RedoLabel);

    internal sealed class WorkspaceHostCallbacks
    {
        public required Func<string> GetCurrentVaultPath { get; init; }
        public required Func<string?> GetActiveDocumentFilePath { get; init; }
        public required Func<string?, IReadOnlyList<VaultEntry>> GetVaultEntries { get; init; }
        public required Func<bool> GetAutoSaveEnabled { get; init; }
        public required Func<bool> GetHasUnsavedDocuments { get; init; }
        public required Func<ThemePalette> GetCurrentPalette { get; init; }
        public required Func<bool> GetIsDarkTheme { get; init; }
        public required Func<WorkspaceViewKind, bool> IsViewVisible { get; init; }

        public required Action<string> OpenFileInTab { get; init; }
        public required Action<int> JumpEditorToLine { get; init; }
        public required Action<string> InsertLatexSnippet { get; init; }
        public required Action<string> InsertUtilitySnippet { get; init; }
        public required Action<WorkspaceViewKind> OpenView { get; init; }
        public required Action<WorkspaceViewKind> CloseView { get; init; }
        public required Action<WorkspaceViewKind> ToggleWorkspaceView { get; init; }
        public required Action ResetAllViews { get; init; }
        public required Action<bool> SetUnifiedWindowMode { get; init; }

        public required Action OpenVaultFolder { get; init; }
        public required Action SaveActiveTab { get; init; }
        public required Action SaveAllDirtyTabs { get; init; }
        public required Action RequestCloseWindow { get; init; }
        public required Action PerformUndo { get; init; }
        public required Action PerformRedo { get; init; }
        public required Action PerformDrawingUndo { get; init; }
        public required Action PerformDrawingRedo { get; init; }
        public required Action InsertCanvasInDocument { get; init; }
        public required Action InsertCodeBlock { get; init; }
        public required Action PasteImageWithPicker { get; init; }
        public required Action ToggleAutoSave { get; init; }
        public required Action ToggleTheme { get; init; }
        public required Action OpenDarkThemeConfig { get; init; }
        public required Action OpenLightThemeConfig { get; init; }
        public required Action LoadCustomThemeFile { get; init; }
        public required Action<bool> ExportActiveDrawing { get; init; }

        public required Action<string> CloseOpenTabsMatchingPath { get; init; }
        public required Action<IReadOnlyList<(string OldPath, string NewPath)>> RemapOpenTabsAfterMove { get; init; }
        public required Action RefreshTabStrip { get; init; }
        public required Action RefreshStatusBar { get; init; }
        public required Action<string, Point> BeginVaultImageDrag { get; init; }
        public required Action<UnsavedExitDecision, bool> ApplyUnsavedExitDecision { get; init; }
        public Action<GitRepositoryStatus>? OnGitStatusUpdated { get; init; }
    }

    public interface IWorkspaceMediator
    {
        string CurrentVaultPath { get; }
        string? ActiveDocumentFilePath { get; }
        bool AutoSaveEnabled { get; }
        bool HasUnsavedDocuments { get; }
        ThemePalette CurrentPalette { get; }
        bool IsDarkTheme { get; }
        IBrush CurrentAccentBrush { get; }
        bool HasOpenOverlay { get; }
        bool IsUtilityInputFocused { get; }
        bool IsUnsavedExitOpen { get; }

        bool IsViewVisible(WorkspaceViewKind view);
        IReadOnlyList<VaultEntry> GetVaultEntries(string? filter = null);

        void OpenFile(string fullPath);
        void JumpEditorToLine(int oneBasedLine);
        void FilterVaultByTag(string tag);
        void InsertLatexSnippet(string latexCode);
        void InsertUtilitySnippet(string snippet);
        void CloseView(WorkspaceViewKind view);
        void ToggleWorkspaceView(WorkspaceViewKind view);
        void ResetAllViews();
        void SetUnifiedWindowMode(bool showRawSource);

        void OpenVaultFolder();
        void SaveActiveTab();
        void RequestSaveAllDirtyTabs();
        void RequestCloseWindow();
        void PerformUndo();
        void PerformRedo();
        void PerformDrawingUndo();
        void PerformDrawingRedo();
        void InsertCanvasInDocument();
        void InsertCodeBlock();
        void PasteImageWithPicker();
        void ToggleAutoSave();
        void ToggleTheme();
        void OpenDarkThemeConfig();
        void OpenLightThemeConfig();
        void LoadCustomThemeFile();
        void ExportActiveDrawing(bool isSvg);

        void PromptCreateNote();
        void PromptCreateFolder();
        void PromptCreateDrawing();
        void RequestRefreshVaultTree();
        void CloseOpenTabsMatchingPath(string fullPath);
        void RemapOpenTabsAfterMove(IReadOnlyList<(string OldPath, string NewPath)> movedMappings);
        void RequestRefreshTabStrip();
        void RequestRefreshStatusBar();
        void BeginVaultImageDrag(string imageFullPath, Point windowPoint);
        void NotifyNonVaultPanelFocused();
        bool TryHandleVaultExplorerKeyDown(KeyEventArgs e, bool isTextInputFocused, bool hasModalOpen);

        void ApplyUnsavedExitDecision(UnsavedExitDecision decision, bool closeWindow = false);

        void PostStatusMessage(string message);
        void UpdateCaretStatus(string caretText);
        void UpdateLatexStatus(string latexStatusText);
        void NotifyStatusBarRefresh(StatusBarSnapshot snapshot);
        void NotifyThemeChanged(ThemePalette palette, bool isDark);
        void NotifyDocumentContentChanged(string markdown);
        void NotifyEditorCursorChanged(string editorText, int caretIndex, bool isMarkdownDocument);
        void NotifyGitStatusUpdated(GitRepositoryStatus status);
        void NotifyVaultNameChanged(string vaultFolderName);
        void NotifyUndoRedoStateChanged(UndoRedoButtonState state);
        void NotifyAutoSaveChanged(bool autoSaveEnabled);
        void NotifyViewLayoutUpdated();

        void OpenGitOverlay();
        void RefreshGitStatus(string? outputOverride = null);
        void RunGitInit();
        void PromptGitCommitAll();
        void PromptGitCommitAndPush();
        void RunGitPull();
        void RunGitPush();
        void PromptGitBranch();
        void PromptGitRemote();

        void OpenQuickSwitcher();
        void ShowPrompt(string title, string defaultText, Action<string> onSubmit);
        void ToggleSyntaxGuide();
        void ShowUnsavedExitDialog(IReadOnlyList<string> dirtyDocumentTitles);
        void HideUnsavedExitDialog();
        bool TryDismissTopOverlay();
    }
}
