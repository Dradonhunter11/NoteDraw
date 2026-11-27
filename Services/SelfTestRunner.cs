using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NoteDraw.Drawing;
using NoteDraw.Rendering;
using NoteDraw.Rendering.Latex;
using NoteDraw.Rendering.Markdown;
using NoteDraw.Services;

namespace NoteDraw
{
    /// <summary>
    /// Built-in automated verification runner executed via `dotnet run -- --self-test`.
    /// Validates LaTeX parsing, live block detection, box layout measurement,
    /// vector drawing serialization/SVG export, VaultService, and ViewLayoutState.
    /// </summary>
    public static partial class SelfTestRunner
    {
        public static int RunAll()
        {
            var tests = new (string Name, Action Test)[]
            {
                ("LatexLiveDetector: fenced ```latex block", TestLiveDetectorFencedLatex),
                ("LatexLiveDetector: unclosed ```latex while typing", TestLiveDetectorUnclosedFence),
                ("LatexLiveDetector: fenced ```math & $$...$$ & $...$", TestLiveDetectorMathAndDollars),
                ("LatexLiveDetector: ignores ```drawing JSON blocks", TestLiveDetectorIgnoresDrawing),
                ("LatexParser & Renderer: fractions, integrals, matrices, partial typing", TestLatexParserAndMeasure),
                ("DrawingDocument: JSON round-trip, clone, SVG export", TestDrawingDocument),
                ("VaultService: starter files, outline, tags, wiki-links, CRUD", TestVaultService),
                ("ViewLayoutState: view close, toggle & reset all views", TestViewLayoutStateTogglesAndReset),
                ("MainWindow Helpers: fenced block body replacement & checklist toggle", TestMarkdownFencedBlockAndChecklistUpdaters),
                ("DrawingCanvasControl: Undo/Redo text labels, background, clear & history", TestDrawingCanvasUndoRedo),
                ("TextUndoManager: multi-character burst & deletion Undo/Redo", TestTextUndoManagerMultiChar),
                ("Checklist & LaTeX Rendering: unclipped checklist rows, $$ matrix, array, operatorname", TestChecklistAndLatexPreviewRendering),
                ("ThemeConfigService: separate dark.json/light.json configs, white canvas & hex fallback", TestThemeConfigService),
                ("DrawingCanvasControl: white canvas surface & mouse hover preview", TestDrawingCanvasWhiteSurfaceAndMousePreview),
                ("DrawingWorkspaceControl: right-click sub-menu & custom color selector", TestDrawingWorkspaceContextSubMenuAndColorSelector),
                ("DrawingCanvasControl: freehand brush eraser & stroke undo", TestDrawingCanvasBrushEraserAndUndo),
                ("EditorFoldManager: H1-H5 section detection, fold/unfold preservation & gutter info", TestEditorFoldManagerH1ToH5),
                ("MarkdownPreviewControl: H1-H5 section collapse & expand in preview", TestPreviewHeadingSectionFolding),
                ("MarkdownPreviewControl: single-click live cursor & arrow segment navigation", TestLiveCursorSegmentActivationAndNavigation),
                ("MarkdownPreviewControl: click drawing card to open/close inline editor", TestClickImageToOpenInlineDrawingEditor),
                ("SyntaxGuideSubMenu: read-only how-to entries, category & search filtering", TestSyntaxGuideSubMenu),
                ("DocumentAssetService: pasted image storage in <exe>/image & tight bitmap bounds", TestPastedImageStorageAndTightCanvasBitmapBounds),
                ("DocumentAssetService: new document canvases saved in <exe>/canvas", TestDocumentCanvasDefaultFolderCreation),
                ("DrawingWorkspaceControl: remembered color persistence & quick restore", TestDrawingRememberedColorsPersistence),
                ("MarkdownPreviewControl: Done Editing Image immediately refreshes preview", TestInlineDrawingDoneEditingImmediateRefresh),
                ("DrawingCanvasControl: canvas resize dimensions & undo/redo", TestDrawingCanvasResizeAndUndo),
                ("MarkdownPreviewControl: image right-click context menu & keyboard deletion", TestMarkdownImageContextMenuAndKeyboardDeletion),
                ("ThemeConfigService: AutoSave option persistence in app_options.json", TestThemeConfigAutoSaveOptionPersistence),
                ("MarkdownPreviewControl & Assets: image import deduplication & line drop insertion", TestImageImportDedupAndDropAtTargetLine),
                ("MainWindow: Auto-Save toggle & Unsaved Work exit confirmation decisions", TestAutoSaveToggleAndUnsavedWorkExitPrompt),
                ("Obsidian Vault Compatibility: image embed resolution without canvas conversion", TestObsidianVaultImageResolutionAndPreviewRendering),
                ("MainWindow & ThemeConfigService: reopen last opened vault on startup", TestLastOpenedVaultStartupPersistence),
                ("GitService: repository init, status, commit, branch & diff", TestGitServiceRepositoryWorkflow),
                ("ScrollViewer & App Styles: non-overlapping slim scrollbars (AllowAutoHide=False)", TestNonOverlappingSlimScrollbars),
                ("Vault Explorer: multi-file Ctrl+Click, Shift+Click & Ctrl+A selection", TestVaultExplorerMultiFileSelection),
                ("Vault Explorer: right-click context menu Cut, Copy, Paste, Select All & Delete items", TestVaultExplorerContextMenuItems),
                ("Vault Explorer: copy & paste files into same folder and subfolders", TestVaultExplorerCopyAndPasteFiles),
                ("Vault Explorer: cut & paste file move with open tab path remapping", TestVaultExplorerCutMoveAndTabRemapping),
                ("Vault Explorer: delete multiple selected files & close matching tabs", TestVaultExplorerMultiFileDelete),
                ("MainWindow & VaultService: single-vault enforcement without starter note pollution", TestSingleVaultIsolationAndNoStarterPollution),
                ("DrawingCanvasControl: tablet pen stroke prevents scroll & commits on capture lost", TestCanvasPenDrawingPreventsScrollAndCommitsOnCaptureLost),
                ("MarkdownPreviewControl: inline drawing multi-line JSON update shifts subsequent segment lines", TestInlineDrawingMultiLineJsonSyncPreservesSubsequentSegments),
                ("DrawingCanvasControl & Workspace: zoom while drawing via wheel, shortcuts & zoom slider", TestCanvasZoomWhileDrawingWheelShortcutsAndControls),
                ("MarkdownPreviewControl: clicking a line preserves scroll offset without jumping to bottom", TestClickLinePreservesScrollOffsetWithoutMovingView),
                ("MarkdownPreviewControl: clicking next to a picture places classic cursor beside picture", TestClickNextToPicturePlacesCursorAlongsidePicture),
                ("MarkdownPreviewControl & MainWindow: canvas context menu Open in a seperate tab", TestCanvasContextMenuOpenInSeparateTab),
                ("DrawingCanvasControl & Workspace: Pipette tool samples stroke and background colors", TestDrawingPipetteColorSampling),
                ("DrawingCanvasControl & Workspace: keyboard shortcuts for Pen, Highlight, Eraser & Pipette", TestDrawingFreehandAndPipetteKeyboardShortcuts),
                ("DrawingCanvasControl & Workspace: keyboard shortcuts for Line, Arrow, Rect & Ellipse", TestDrawingShapeKeyboardShortcuts),
                ("MainWindow & MarkdownPreviewControl: + Canvas in Doc persists canvas link in file across close, swap & save", TestInsertCanvasInDocPersistsInFileAcrossCloseSwapAndSave),
                ("DocumentAssetService & MainWindow: pasted images and added canvases are stored in active vault folders", TestVaultLocalImageAndCanvasStorageInActiveVault),
                ("MainWindow: View & Mode menu hosts View submenu with Workspace View toggles", TestViewModeDropdownWorkspaceViewsSubMenu),
                ("DrawingCanvasControl & Workspace: copy and paste images in canvas with move, undo & JSON/SVG persistence", TestCanvasCopyAndPasteImage),
                ("CrashLoggerService: log4net crash & error logging outputs to <exe>/Logs folder", TestCrashLoggerLog4NetOutput),
                ("MainWindow: switching between Drawing Tools and Git ribbon tabs does not crash", TestRibbonSwitchBetweenDrawingToolsAndGitDoesNotCrash),
                ("DrawingCanvasControl & Workspace: Move Position tool pans viewport without moving strokes", TestCanvasMovePositionToolPanning),
                ("DrawingCanvasControl & Workspace: Select Area tool restricts drawing and cancels on <5x5 or double-click", TestCanvasSelectAreaRestrictsDrawingAndCancels),
                ("DrawingCanvasControl & Workspace: Move Selection tool translates selection area and enclosed strokes", TestCanvasMoveSelectionToolMovesAreaAndStrokes),
                ("DrawingCanvasControl & Workspace: Resize Selection tool scales selection area and enclosed strokes", TestCanvasResizeSelectionToolResizesAreaAndStrokes),
                ("CodeSyntaxHighlighter: tokenizes keywords, strings, comments, numbers & types across languages", TestCodeSyntaxHighlighterTokenization),
                ("MarkdownPreviewControl: fenced code blocks render syntax-highlighted Runs, language badge & tilde fences", TestMarkdownFencedCodeBlockSyntaxHighlightingRender),
                ("PlatformShortcuts: macOS uses Cmd (Meta) exclusively while Windows/Linux uses Ctrl", TestPlatformShortcutsMacCmdVsWindowsLinuxCtrl),
                ("DrawingWorkspace & Tools: vertical side toolbar with vector icons, Command Pattern & modular tool classes", TestCanvasSideToolbarIconsAndCommandPattern),
                ("WorkspaceMediator: broadcasts status bar snapshots and Git status to colleagues", TestWorkspaceMediatorStatusAndGitBroadcast),
                ("WorkspaceMediator: manages overlay lifecycle and Escape dismissal priority", TestWorkspaceMediatorOverlayLifecycleAndEscape),
                ("WorkspaceMediator: coordinates VaultExplorer filtering, vault label & Git ribbon updates", TestWorkspaceMediatorVaultAndRibbonCoordination),
                ("WorkspaceMediator: synchronizes TopBar undo/redo, auto-save menu & RibbonBar tabs", TestWorkspaceMediatorTopBarAndRibbonControls)
            };

            int passed = 0;
            foreach (var (name, test) in tests)
            {
                try
                {
                    test();
                    Console.WriteLine($"[PASS] {name}");
                    passed++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[FAIL] {name}: {ex.Message}");
                }
            }

            Console.WriteLine($"\nNoteDraw Self-Test Summary: {passed}/{tests.Length} passed.");
            return passed == tests.Length ? 0 : 1;
        }

        private static void WithTempVault(string prefix, Action<string> action)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                action(tempDir);
            }
            finally
            {
                DeleteDirectorySafe(tempDir);
            }
        }

        private static void SaveAndRestoreFile(string filePath, Action action)
        {
            string? previous = File.Exists(filePath) ? File.ReadAllText(filePath) : null;
            try
            {
                action();
            }
            finally
            {
                if (previous is not null)
                    File.WriteAllText(filePath, previous);
                else if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        private static Control? FindControlWithContextMenu(Control? root)
        {
            if (root is not null && root.ContextMenu is not null)
                return root;
            if (root is Panel p)
            {
                foreach (var c in p.Children)
                {
                    var found = FindControlWithContextMenu(c);
                    if (found is not null) return found;
                }
            }
            if (root is Decorator d)
                return FindControlWithContextMenu(d.Child);
            if (root is ContentControl cc && cc.Content is Control inner)
                return FindControlWithContextMenu(inner);
            return null;
        }

        private static Button? FindButtonByText(Control? root, string text)
        {
            if (root is Button btn && string.Equals(btn.Content?.ToString(), text, StringComparison.Ordinal))
                return btn;
            if (root is Panel p)
            {
                foreach (var c in p.Children)
                {
                    var found = FindButtonByText(c, text);
                    if (found is not null) return found;
                }
            }
            if (root is Decorator d)
                return FindButtonByText(d.Child, text);
            if (root is ContentControl cc && cc.Content is Control inner)
                return FindButtonByText(inner, text);
            return null;
        }

        private static void DeleteDirectorySafe(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return;
            try
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var attrs = File.GetAttributes(file);
                        if ((attrs & FileAttributes.ReadOnly) != 0)
                            File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                    }
                    catch { }
                }
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup
            }
        }

        private static bool ContainsControlType<T>(Control? root) where T : Control
            => FindControlType<T>(root) is not null;

        private static List<T> CollectControlTypes<T>(Control? root) where T : Control
        {
            var results = new List<T>();
            void Visit(Control? node)
            {
                if (node is null) return;
                if (node is T match)
                    results.Add(match);
                if (node is Panel panel)
                {
                    foreach (var child in panel.Children)
                        Visit(child);
                }
                if (node is Decorator decorator)
                    Visit(decorator.Child);
                if (node is ContentControl contentControl && contentControl.Content is Control inner)
                    Visit(inner);
            }
            Visit(root);
            return results;
        }

        private static T? FindControlType<T>(Control? root) where T : Control
        {
            if (root is null) return null;
            if (root is T match) return match;
            if (root is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    var found = FindControlType<T>(child);
                    if (found is not null)
                        return found;
                }
            }
            if (root is Decorator decorator)
            {
                var found = FindControlType<T>(decorator.Child);
                if (found is not null)
                    return found;
            }
            if (root is ContentControl contentControl && contentControl.Content is Control inner)
            {
                var found = FindControlType<T>(inner);
                if (found is not null)
                    return found;
            }
            return null;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
