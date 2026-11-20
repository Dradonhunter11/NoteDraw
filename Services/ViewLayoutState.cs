using System;
using System.Collections.Generic;
using System.Linq;

namespace NoteDraw.Services
{
    /// <summary>
    /// All independently closable and restorable UI views in the NoteDraw workspace.
    /// Maintainers: Add new closable workspace panels here and register them in ViewLayoutState and MainWindow.
    /// </summary>
    public enum WorkspaceViewKind
    {
        VaultExplorer,
        OutlineTags,
        FormattingToolbar,
        EditorPane,
        LiveLatexPreview,
        PreviewPane,
        StatusBar
    }

    /// <summary>
    /// Tracks which workspace views are currently open or closed and coordinates
    /// restoring views via the top toolbar's View tab.
    /// </summary>
    public sealed class ViewLayoutState
    {
        private readonly Dictionary<WorkspaceViewKind, bool> _visibility = new();

        public event EventHandler? Changed;

        public ViewLayoutState()
        {
            foreach (WorkspaceViewKind view in Enum.GetValues<WorkspaceViewKind>())
                _visibility[view] = true;
        }

        public bool IsVisible(WorkspaceViewKind view)
            => _visibility.TryGetValue(view, out bool visible) && visible;

        public int TotalCount => _visibility.Count;

        public int VisibleCount => _visibility.Values.Count(v => v);

        public void CloseView(WorkspaceViewKind view)
            => SetViewVisible(view, false);

        public void OpenView(WorkspaceViewKind view)
            => SetViewVisible(view, true);

        public bool ToggleView(WorkspaceViewKind view)
        {
            bool next = !IsVisible(view);
            SetViewVisible(view, next);
            return next;
        }

        public void SetViewVisible(WorkspaceViewKind view, bool isVisible)
        {
            if (_visibility.TryGetValue(view, out bool current) && current == isVisible)
                return;

            _visibility[view] = isVisible;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void ResetAllViews()
        {
            bool anyChanged = false;
            foreach (WorkspaceViewKind view in Enum.GetValues<WorkspaceViewKind>())
            {
                if (!_visibility[view])
                {
                    _visibility[view] = true;
                    anyChanged = true;
                }
            }

            if (anyChanged)
                Changed?.Invoke(this, EventArgs.Empty);
        }

        public void ApplyEditorLayoutPreset(string preset)
        {
            switch (preset)
            {
                case "Split":
                    _visibility[WorkspaceViewKind.EditorPane] = true;
                    _visibility[WorkspaceViewKind.PreviewPane] = true;
                    break;
                case "Editor":
                    _visibility[WorkspaceViewKind.EditorPane] = true;
                    _visibility[WorkspaceViewKind.PreviewPane] = false;
                    break;
                case "Preview":
                    _visibility[WorkspaceViewKind.EditorPane] = false;
                    _visibility[WorkspaceViewKind.PreviewPane] = true;
                    break;
                case "Merged":
                    _visibility[WorkspaceViewKind.EditorPane] = false;
                    _visibility[WorkspaceViewKind.LiveLatexPreview] = false;
                    _visibility[WorkspaceViewKind.PreviewPane] = true;
                    break;
                default:
                    return;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public static string GetDisplayName(WorkspaceViewKind view) => view switch
        {
            WorkspaceViewKind.VaultExplorer => "Vault Explorer",
            WorkspaceViewKind.OutlineTags => "Outline & Tags",
            WorkspaceViewKind.FormattingToolbar => "Formatting Toolbar",
            WorkspaceViewKind.EditorPane => "Markdown Editor",
            WorkspaceViewKind.LiveLatexPreview => "Live LaTeX Preview",
            WorkspaceViewKind.PreviewPane => "Document Preview",
            WorkspaceViewKind.StatusBar => "Status Bar",
            _ => view.ToString()
        };
    }
}
