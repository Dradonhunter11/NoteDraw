using System;
using System.Collections.Generic;
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

    internal sealed class WorkspaceHostCallbacks
    {
        public required Func<string> GetCurrentVaultPath { get; init; }
        public required Func<string?, IReadOnlyList<VaultEntry>> GetVaultEntries { get; init; }
        public required Func<bool> GetAutoSaveEnabled { get; init; }
        public required Func<bool> GetHasUnsavedDocuments { get; init; }
        public required Func<ThemePalette> GetCurrentPalette { get; init; }
        public required Func<bool> GetIsDarkTheme { get; init; }
        public required Action<string> OpenFileInTab { get; init; }
        public required Action<int> JumpEditorToLine { get; init; }
        public required Action<string> FilterVaultByTag { get; init; }
        public required Action<string> InsertLatexSnippet { get; init; }
        public required Action<WorkspaceViewKind> CloseView { get; init; }
        public required Action RefreshVaultTree { get; init; }
        public required Action SaveAllDirtyTabs { get; init; }
        public required Action<UnsavedExitDecision, bool> ApplyUnsavedExitDecision { get; init; }
        public required Action<GitRepositoryStatus> OnGitStatusUpdated { get; init; }
        public required Action NotifyNonVaultPanelFocused { get; init; }
    }

    public interface IWorkspaceMediator
    {
        string CurrentVaultPath { get; }
        bool AutoSaveEnabled { get; }
        bool HasUnsavedDocuments { get; }
        ThemePalette CurrentPalette { get; }
        bool IsDarkTheme { get; }
        IBrush CurrentAccentBrush { get; }
        bool HasOpenOverlay { get; }
        bool IsUtilityInputFocused { get; }
        bool IsUnsavedExitOpen { get; }

        IReadOnlyList<VaultEntry> GetVaultEntries(string? filter = null);

        void OpenFile(string fullPath);
        void JumpEditorToLine(int oneBasedLine);
        void FilterVaultByTag(string tag);
        void InsertLatexSnippet(string latexCode);
        void CloseView(WorkspaceViewKind view);
        void RequestRefreshVaultTree();
        void RequestSaveAllDirtyTabs();
        void ApplyUnsavedExitDecision(UnsavedExitDecision decision, bool closeWindow = false);
        void NotifyNonVaultPanelFocused();

        void PostStatusMessage(string message);
        void UpdateCaretStatus(string caretText);
        void UpdateLatexStatus(string latexStatusText);
        void NotifyStatusBarRefresh(StatusBarSnapshot snapshot);
        void NotifyThemeChanged(ThemePalette palette, bool isDark);
        void NotifyDocumentContentChanged(string markdown);
        void NotifyEditorCursorChanged(string editorText, int caretIndex, bool isMarkdownDocument);
        void NotifyGitStatusUpdated(GitRepositoryStatus status);

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
