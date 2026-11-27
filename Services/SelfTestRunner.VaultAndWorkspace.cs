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
using NoteDraw.UI;
using NoteDraw.UI.Overlays;
using NoteDraw.UI.Panels;

namespace NoteDraw
{
    public static partial class SelfTestRunner
    {
        private static void TestVaultService()
        {
            string tempVault = Path.Combine(Path.GetTempPath(), "NoteDrawTestVault_" + Guid.NewGuid().ToString("N"));
            try
            {
                var vault = new VaultService(tempVault);
                var entries = vault.GetEntries();
                Assert(entries.Any(e => e.Name == "Welcome to NoteDraw.md"), "Sample note should be created.");
                Assert(entries.Any(e => e.Name == "LaTeX & Math Showcase.md"), "Showcase note should be created.");
                Assert(entries.Any(e => e.Name == "Sketchpad Demo.ndraw"), "Sample drawing should be created.");
                Assert(entries.Any(e => e.Kind == VaultEntryKind.Folder && e.Name == "Notes"),
                    "Starter subfolder 'Notes' must appear in Vault Explorer entries.");

                var subFolder = vault.CreateFolder("SubFolder");
                Assert(Directory.Exists(subFolder.FullPath), "Created folder must exist on disk.");
                Assert(vault.GetEntries().Any(e => e.Kind == VaultEntryKind.Folder && e.Name == "SubFolder"),
                    "Created folder must appear in Vault Explorer entries.");
                Assert(vault.GetEntries("SubFolder").Any(e => e.Kind == VaultEntryKind.Folder && e.Name == "SubFolder"),
                    "Filtered Vault Explorer query matching a folder name must preserve the folder.");
                var quickRefFiltered = vault.GetEntries("Quick Reference");
                Assert(quickRefFiltered.Any(e => e.Kind == VaultEntryKind.Folder && e.Name == "Notes") &&
                       quickRefFiltered.Any(e => e.Kind == VaultEntryKind.MarkdownNote && e.Name == "Quick Reference.md"),
                    "Filtered Vault Explorer query matching a nested note must keep its parent folder visible.");

                var resolved = vault.ResolveWikiLink("LaTeX & Math Showcase");
                Assert(resolved is not null, "WikiLink resolution should match note without .md extension.");

                string sample = vault.ReadFile(resolved!.FullPath);
                var outline = VaultService.ExtractOutline(sample);
                Assert(outline.Count >= 3, "Outline should extract headings from showcase note.");
                var tags = VaultService.ExtractTags(sample);
                Assert(tags.Contains("#math"), "Tags extractor should find #math.");

                var created = vault.CreateNote("Unit Test Note");
                Assert(File.Exists(created.FullPath), "Created note must exist on disk.");
                Assert(vault.RenameEntry(created.FullPath, "Renamed Note", out string renamedPath), "Rename should succeed.");
                Assert(File.Exists(renamedPath), "Renamed file should exist.");
                vault.DeleteEntry(renamedPath);
                Assert(!File.Exists(renamedPath), "Deleted file should be removed.");
            }
            finally
            {
                if (Directory.Exists(tempVault))
                    Directory.Delete(tempVault, recursive: true);
            }
        }

        private static void TestViewLayoutStateTogglesAndReset()
        {
            var views = new ViewLayoutState();
            Assert(views.VisibleCount == views.TotalCount && views.TotalCount == 7, "All 7 views start open.");

            views.CloseView(WorkspaceViewKind.VaultExplorer);
            views.CloseView(WorkspaceViewKind.LiveLatexPreview);
            Assert(!views.IsVisible(WorkspaceViewKind.VaultExplorer), "VaultExplorer should close.");
            Assert(!views.IsVisible(WorkspaceViewKind.LiveLatexPreview), "LiveLatexPreview should close.");
            Assert(views.VisibleCount == 5, "VisibleCount should be 5 after closing 2 views.");

            views.ToggleView(WorkspaceViewKind.LiveLatexPreview);
            Assert(views.IsVisible(WorkspaceViewKind.LiveLatexPreview), "Toggle should restore closed view.");

            foreach (WorkspaceViewKind v in Enum.GetValues<WorkspaceViewKind>())
                views.CloseView(v);
            Assert(views.VisibleCount == 0, "All views should be closable.");

            views.ResetAllViews();
            Assert(views.VisibleCount == 7, "ResetAllViews should restore all 7 views.");
        }

        private static void TestMarkdownFencedBlockAndChecklistUpdaters()
        {
            string sampleMd = "# Title\n```drawing\n{\"strokes\":[]}\n```\n- [ ] Task one";
            string replaced = MainWindow.ReplaceFencedBlockBody(sampleMd, 1, "{\"strokes\":[1]}");
            Assert(replaced.Contains("{\"strokes\":[1]}") && replaced.Contains("# Title"),
                "ReplaceFencedBlockBody should update JSON cleanly.");

            string checkedMd = MainWindow.ToggleTaskCheckboxLine(replaced, 4, true);
            Assert(checkedMd.Contains("- [x] Task one"), "ToggleTaskCheckboxLine should check item.");
        }

        private static void TestThemeConfigService()
        {
            var classicDark = ThemePalette.CreateClassicDark();
            var classicLight = ThemePalette.CreateClassicLight();
            Assert(classicDark.ChromeBackground == "#1E1E1E",
                $"Expected classic dark ChromeBackground #1E1E1E, got {classicDark.ChromeBackground}");
            Assert(classicDark.PanelBackground == "#252526",
                $"Expected classic dark PanelBackground #252526, got {classicDark.PanelBackground}");
            Assert(classicDark.AccentColor == "#0078D4",
                $"Expected classic dark AccentColor #0078D4, got {classicDark.AccentColor}");
            Assert(classicDark.CanvasBackground == "#FFFFFF" && classicLight.CanvasBackground == "#FFFFFF",
                "Both classic dark and light themes should default drawing CanvasBackground to #FFFFFF.");

            Assert(ThemeConfigService.ParseColor("not-a-hex", "#1E1E1E") == Color.Parse("#1E1E1E"),
                "Invalid hex string should fall back safely to #1E1E1E");

            string tempDir = Path.Combine(Path.GetTempPath(), $"notedraw_theme_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                var service = new ThemeConfigService();
                service.Load(tempDir);

                string darkPath = Path.Combine(tempDir, "dark.json");
                string lightPath = Path.Combine(tempDir, "light.json");
                string legacyPath = Path.Combine(tempDir, "theme.json");
                Assert(File.Exists(darkPath), "ThemeConfigService.Load should create dark.json.");
                Assert(File.Exists(lightPath), "ThemeConfigService.Load should create light.json.");
                Assert(!File.Exists(legacyPath), "ThemeConfigService.Load must NOT create singular theme.json.");

                var customDark = ThemePalette.CreateClassicDark();
                customDark.AccentColor = "#FF6B00";
                customDark.EditorFontSize = 15.5;
                service.SavePaletteToFile(customDark, darkPath);

                var customLight = ThemePalette.CreateClassicLight();
                customLight.AccentColor = "#0EA5E9";
                customLight.PreviewFontSize = 16.5;
                service.SavePaletteToFile(customLight, lightPath);

                var reloadService = new ThemeConfigService();
                var reloadedDark = reloadService.LoadPaletteFromFile(darkPath, applyAsDark: true);
                Assert(reloadedDark.AccentColor == "#FF6B00",
                    $"Expected reloaded dark AccentColor #FF6B00, got {reloadedDark.AccentColor}");
                Assert(Math.Abs(reloadedDark.EditorFontSize - 15.5) < 0.001,
                    $"Expected reloaded dark EditorFontSize 15.5, got {reloadedDark.EditorFontSize}");

                var reloadedLight = reloadService.LoadPaletteFromFile(lightPath, applyAsDark: false);
                Assert(reloadedLight.AccentColor == "#0EA5E9",
                    $"Expected reloaded light AccentColor #0EA5E9, got {reloadedLight.AccentColor}");
                Assert(Math.Abs(reloadedLight.PreviewFontSize - 16.5) < 0.001,
                    $"Expected reloaded light PreviewFontSize 16.5, got {reloadedLight.PreviewFontSize}");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }

        private static void TestThemeConfigAutoSaveOptionPersistence()
        {
            WithTempVault("NoteDrawAutoSaveCfgTest", tempVault =>
                SaveAndRestoreFile(ThemeConfigService.GetOptionsFilePath(), () =>
                {
                    var cfgSvc = new ThemeConfigService();
                    cfgSvc.Load(tempVault);
                    cfgSvc.Config.AutoSave = false;
                    cfgSvc.SaveOptions();

                    var reloaded = new ThemeConfigService();
                    reloaded.Load(tempVault);
                    Assert(!reloaded.Config.AutoSave, "ThemeConfigService should persist AutoSave = false.");

                    cfgSvc.Config.AutoSave = true;
                    cfgSvc.SaveOptions();
                    reloaded.Load(tempVault);
                    Assert(reloaded.Config.AutoSave, "ThemeConfigService should persist AutoSave = true.");
                }));
        }

        private static void TestAutoSaveToggleAndUnsavedWorkExitPrompt()
        {
            var assets = new DocumentAssetService();
            string? fileA = null;
            string? fileB = null;
            try
            {
                var bmp = new RenderTargetBitmap(new PixelSize(48, 36), new Vector(96, 96));
                var savedA = assets.SavePastedBitmap(bmp, "exitA");
                var savedB = assets.SavePastedBitmap(bmp, "exitB");
                fileA = savedA.FullPath;
                fileB = savedB.FullPath;

                WithTempVault("NoteDrawExitPromptTest", tempVault =>
                    SaveAndRestoreFile(ThemeConfigService.GetOptionsFilePath(), () =>
                    {
                        var win = new MainWindow(tempVault);
                        string welcomePath = Path.Combine(tempVault, "Welcome to NoteDraw.md");
                        Assert(File.Exists(welcomePath), "Starter note must exist in isolated test vault.");

                        win.SetAutoSaveEnabled(false);
                        Assert(!win.AutoSaveEnabled && !win.ShouldPromptOnExit(),
                            "Clean document with Auto-Save off should not prompt on exit.");

                        Assert(win.DropImageFileIntoDocumentAtLine(fileA, 1),
                            "DropImageFileIntoDocumentAtLine should succeed on open markdown note.");
                        Assert(win.HasUnsavedDocuments && win.ShouldPromptOnExit(),
                            "With Auto-Save OFF and unsaved changes, ShouldPromptOnExit must be true.");
                        Assert(!File.ReadAllText(welcomePath).Contains(savedA.FileName, StringComparison.Ordinal),
                            "With Auto-Save OFF, disk file must remain unchanged before manual Save.");

                        win.ShowUnsavedExitMessageBox();
                        Assert(win.IsUnsavedExitMessageBoxOpen, "Unsaved Exit MessageBox overlay should open.");
                        win.ApplyUnsavedExitDecision(UnsavedExitDecision.Cancel);
                        Assert(!win.IsUnsavedExitMessageBoxOpen && win.HasUnsavedDocuments,
                            "Cancel must dismiss MessageBox and preserve unsaved state.");

                        win.ApplyUnsavedExitDecision(UnsavedExitDecision.Save);
                        Assert(!win.HasUnsavedDocuments && !win.ShouldPromptOnExit(),
                            "Save decision must flush changes and clear dirty state.");
                        Assert(File.ReadAllText(welcomePath).Contains(savedA.FileName, StringComparison.Ordinal),
                            "Save decision must write changes to disk.");

                        Assert(win.DropImageFileIntoDocumentAtLine(fileB, 2),
                            "Second DropImageFileIntoDocumentAtLine should succeed.");
                        win.ApplyUnsavedExitDecision(UnsavedExitDecision.Discard);
                        Assert(!win.HasUnsavedDocuments &&
                               !File.ReadAllText(welcomePath).Contains(savedB.FileName, StringComparison.Ordinal),
                            "Discard decision must clear dirty state without writing discarded changes.");

                        win.SetAutoSaveEnabled(true);
                        Assert(win.AutoSaveEnabled && win.DropImageFileIntoDocumentAtLine(fileB, 2),
                            "Auto-Save ON should accept document edits.");
                        Assert(!win.HasUnsavedDocuments && !win.ShouldPromptOnExit() &&
                               File.ReadAllText(welcomePath).Contains(savedB.FileName, StringComparison.Ordinal),
                            "With Auto-Save ON, edits must auto-save immediately and never prompt on exit.");
                    }));
            }
            finally
            {
                if (!string.IsNullOrEmpty(fileA) && File.Exists(fileA))
                    File.Delete(fileA);
                if (!string.IsNullOrEmpty(fileB) && File.Exists(fileB))
                    File.Delete(fileB);
            }
        }

        private static void TestObsidianVaultImageResolutionAndPreviewRendering()
        {
            WithTempVault("NoteDrawObsidianImgTest", tempObsidianVault =>
            {
                Directory.CreateDirectory(Path.Combine(tempObsidianVault, ".obsidian"));
                File.WriteAllText(Path.Combine(tempObsidianVault, ".obsidian", "app.json"), "{}");
                Directory.CreateDirectory(Path.Combine(tempObsidianVault, "Attachments"));
                Directory.CreateDirectory(Path.Combine(tempObsidianVault, "Subfolder"));

                var bmp = new RenderTargetBitmap(new PixelSize(80, 50), new Vector(96, 96));
                string img1 = Path.Combine(tempObsidianVault, "Attachments", "Pasted image 20260401.png");
                string img2 = Path.Combine(tempObsidianVault, "Subfolder", "diagram.png");
                string img3 = Path.Combine(tempObsidianVault, "Subfolder", "space image.png");
                bmp.Save(img1);
                bmp.Save(img2);
                bmp.Save(img3);

                string obsidianNote = string.Join("\n",
                    "# Obsidian Import Test",
                    "![[Pasted image 20260401.png]]",
                    "![[Subfolder/diagram.png|400]]",
                    "![Alt text](<Subfolder/space image.png>)",
                    "![[Missing Note#Heading|Alias]]",
                    "Inline image ![[Pasted image 20260401.png|120]] in text.");

                var assets = new DocumentAssetService();
                Assert(string.Equals(assets.ResolveImagePath("Pasted image 20260401.png", tempObsidianVault, tempObsidianVault), Path.GetFullPath(img1), StringComparison.OrdinalIgnoreCase),
                    "ResolveImagePath must find shortest-path Obsidian image inside Attachments/.");
                Assert(string.Equals(assets.ResolveImagePath("Subfolder/diagram.png|400", tempObsidianVault, tempObsidianVault), Path.GetFullPath(img2), StringComparison.OrdinalIgnoreCase),
                    "ResolveImagePath must strip |width suffix and locate Subfolder/diagram.png.");
                Assert(string.Equals(assets.ResolveImagePath("<Subfolder/space%20image.png>", tempObsidianVault, tempObsidianVault), Path.GetFullPath(img3), StringComparison.OrdinalIgnoreCase),
                    "ResolveImagePath must unescape %20 and angle brackets for Markdown image paths.");
                Assert(assets.TryLoadExistingCanvas("Pasted image 20260401.png", tempObsidianVault) is null,
                    "TryLoadExistingCanvas must return null for non-.ndraw image embeds.");
                _ = assets.ResolveCanvasPath("bad|name:test?1*2", tempObsidianVault);

                var preview = new MarkdownPreviewControl
                {
                    ResolveVaultImagePath = target => assets.ResolveImagePath(target, tempObsidianVault, tempObsidianVault),
                    ResolveEmbeddedDrawing = target => assets.TryLoadExistingCanvas(target, tempObsidianVault)
                };
                preview.RenderMarkdown(obsidianNote);
                Assert(ContainsControlType<Image>(preview.RootPanel),
                    "Obsidian vault images must render as Image controls.");
                Assert(!ContainsControlType<DrawingCanvasControl>(preview.RootPanel) &&
                       !ContainsControlType<DrawingWorkspaceControl>(preview.RootPanel),
                    "Obsidian images must NEVER be automatically converted into DrawingCanvas blocks.");
                Assert(!File.Exists(Path.Combine(assets.CanvasFolderPath, "Pasted_image_20260401.png.ndraw")) &&
                       !File.Exists(Path.Combine(assets.CanvasFolderPath, "Pasted_image_20260401.ndraw")),
                    "Rendering Obsidian image embeds must not create .ndraw files on disk.");
            });
        }

        private static void TestLastOpenedVaultStartupPersistence()
        {
            WithTempVault("NoteDrawLastVaultDefault", tempDefaultVault =>
                WithTempVault("NoteDrawLastVaultTarget", tempTargetVault =>
                    SaveAndRestoreFile(ThemeConfigService.GetOptionsFilePath(), () =>
                    {
                        File.WriteAllText(Path.Combine(tempTargetVault, "TargetNote.md"), "# Target Vault\n");
                        var win = new MainWindow(tempDefaultVault);
                        Assert(win.OpenVaultAtPath(tempTargetVault, persistAsLastVault: true),
                            "OpenVaultAtPath should load target vault cleanly.");
                        Assert(string.Equals(ThemeConfigService.GetSavedLastVaultPath(), Path.GetFullPath(tempTargetVault), StringComparison.OrdinalIgnoreCase),
                            "Saved LastVaultPath must point to newly opened vault.");

                        var reopenedWin = new MainWindow();
                        Assert(string.Equals(reopenedWin.CurrentVaultPath, Path.GetFullPath(tempTargetVault), StringComparison.OrdinalIgnoreCase),
                            $"Restarted MainWindow must reopen last opened vault ({tempTargetVault}), got {reopenedWin.CurrentVaultPath}");
                    })));
        }

        private static void TestGitServiceRepositoryWorkflow()
        {
            var git = new GitService();
            if (!git.IsGitInstalled())
                return;

            WithTempVault("NoteDrawGitRepoTest", tempGitVault =>
            {
                string notePath = Path.Combine(tempGitVault, "ObsidianNote.md");
                File.WriteAllText(notePath, "# Git Integration Note\nInitial line.\n");

                var initRes = git.InitRepository(tempGitVault);
                Assert(initRes.Success, $"Git InitRepository failed: {initRes.Output}");
                var st1 = git.GetStatus(tempGitVault);
                Assert(st1.IsRepository && st1.Changes.Count >= 1,
                    "Initialized vault should report untracked/changed files.");

                var commitRes = git.StageAndCommitAll(tempGitVault, "Initial obsidian commit");
                Assert(commitRes.Success, $"Git StageAndCommitAll failed: {commitRes.Output}");
                var st2 = git.GetStatus(tempGitVault);
                Assert(st2.Changes.Count == 0 && st2.RecentCommits.Any(c => c.Subject == "Initial obsidian commit"),
                    "Committed vault should have clean status and recorded commit subject.");

                var brRes = git.SwitchOrCreateBranch(tempGitVault, "notes-branch", createNew: true);
                Assert(brRes.Success && git.GetStatus(tempGitVault).BranchName == "notes-branch",
                    $"Git branch switch/create failed: {brRes.Output}");

                File.AppendAllText(notePath, "Added line for diff.\n");
                string diff = git.GetDiff(tempGitVault, "ObsidianNote.md");
                Assert(diff.Contains("Added line for diff.", StringComparison.Ordinal),
                    $"Git GetDiff should include newly appended line, got: {diff}");
            });
        }

        private static void TestNonOverlappingSlimScrollbars()
        {
            WithTempVault("NoteDrawScrollbarTest", tempVault =>
            {
                var win = new MainWindow(tempVault);
                var scroller = FindControlType<ScrollViewer>(win);
                Assert(scroller is not null && !scroller.AllowAutoHide,
                    "Workspace ScrollViewers must disable overlay auto-hide to prevent overlapping UI controls.");

                Assert(Application.Current is not null &&
                       Application.Current.Resources.TryGetResource("ScrollBarSize", null, out var sizeObj) &&
                       sizeObj is double sbSize &&
                       Math.Abs(sbSize - 6.0) < 0.01,
                    "App.axaml must define slim ScrollBarSize resource of 6px.");
            });
        }

        private static void TestVaultExplorerMultiFileSelection()
        {
            WithTempVault("NoteDrawMultiSelectTest", tempVault =>
            {
                string targetDir = Path.Combine(tempVault, "TargetDir");
                Directory.CreateDirectory(targetDir);
                string file1 = Path.Combine(tempVault, "Alpha.md");
                string file2 = Path.Combine(tempVault, "Beta.md");
                string file3 = Path.Combine(tempVault, "Gamma.md");
                File.WriteAllText(file1, "# Alpha\nFirst note.");
                File.WriteAllText(file2, "# Beta\nSecond note.");
                File.WriteAllText(file3, "# Gamma\nThird note.");

                var win = new MainWindow(tempVault);

                win.SelectVaultEntry(file1, ctrlHeld: false, shiftHeld: false);
                Assert(win.SelectedVaultPaths.Count == 1 && win.SelectedVaultPaths.Contains(Path.GetFullPath(file1)),
                    "Single selection should select Alpha.md.");

                win.SelectVaultEntry(file3, ctrlHeld: true, shiftHeld: false);
                Assert(win.SelectedVaultPaths.Count == 2 &&
                       win.SelectedVaultPaths.Contains(Path.GetFullPath(file1)) &&
                       win.SelectedVaultPaths.Contains(Path.GetFullPath(file3)),
                    "Ctrl+Click must toggle-add Gamma.md alongside Alpha.md.");

                win.SelectVaultEntry(file1, ctrlHeld: false, shiftHeld: false);
                win.SelectVaultEntry(file3, ctrlHeld: false, shiftHeld: true);
                Assert(win.SelectedVaultPaths.Count >= 3 &&
                       win.SelectedVaultPaths.Contains(Path.GetFullPath(file1)) &&
                       win.SelectedVaultPaths.Contains(Path.GetFullPath(file2)) &&
                       win.SelectedVaultPaths.Contains(Path.GetFullPath(file3)),
                    "Shift+Click range selection should select Alpha.md, Beta.md, and Gamma.md.");

                win.SelectAllVaultFiles();
                Assert(win.SelectedVaultPaths.Count >= 3 && !win.SelectedVaultPaths.Contains(Path.GetFullPath(targetDir)),
                    "SelectAllVaultFiles should select visible files and skip folders.");
            });
        }

        private static void TestVaultExplorerContextMenuItems()
        {
            WithTempVault("NoteDrawCtxMenuTest", tempVault =>
            {
                string file1 = Path.Combine(tempVault, "Alpha.md");
                File.WriteAllText(file1, "# Alpha");
                var win = new MainWindow(tempVault);

                var menu = win.CreateVaultContextMenu(file1, VaultEntryKind.MarkdownNote);
                var headers = menu.Items.OfType<MenuItem>().Select(m => m.Header?.ToString() ?? string.Empty).ToList();
                Assert(headers.Any(h => h.Contains("Cut", StringComparison.OrdinalIgnoreCase)) &&
                       headers.Any(h => h.Contains("Copy", StringComparison.OrdinalIgnoreCase)) &&
                       headers.Any(h => h.Contains("Paste", StringComparison.OrdinalIgnoreCase)) &&
                       headers.Any(h => h.Contains("Select All", StringComparison.OrdinalIgnoreCase)) &&
                       headers.Any(h => h.Contains("Delete", StringComparison.OrdinalIgnoreCase)),
                    $"Context menu must contain Cut, Copy, Paste, Select All, and Delete items. Got: {string.Join(", ", headers)}");
            });
        }

        private static void TestVaultExplorerCopyAndPasteFiles()
        {
            WithTempVault("NoteDrawCopyPasteFilesTest", tempVault =>
            {
                string targetDir = Path.Combine(tempVault, "TargetDir");
                Directory.CreateDirectory(targetDir);
                string file1 = Path.Combine(tempVault, "Alpha.md");
                string file2 = Path.Combine(tempVault, "Beta.md");
                File.WriteAllText(file1, "# Alpha");
                File.WriteAllText(file2, "# Beta");

                var win = new MainWindow(tempVault);
                win.SelectVaultEntry(file1, ctrlHeld: false, shiftHeld: false);
                win.SelectVaultEntry(file2, ctrlHeld: true, shiftHeld: false);
                win.CopySelectedVaultFiles();
                Assert(win.VaultClipboardPaths.Count == 2,
                    $"Expected 2 files in clipboard, got {win.VaultClipboardPaths.Count}");

                var pastedSameDir = win.PasteVaultFiles(tempVault);
                Assert(pastedSameDir.Count == 2 &&
                       File.Exists(Path.Combine(tempVault, "Alpha - Copy.md")) &&
                       File.Exists(Path.Combine(tempVault, "Beta - Copy.md")),
                    "Pasting copied files into the same folder must create ' - Copy' duplicates cleanly.");

                var pastedInTarget = win.PasteVaultFiles(targetDir);
                Assert(pastedInTarget.Count == 2 &&
                       File.Exists(Path.Combine(targetDir, "Alpha.md")) &&
                       File.Exists(Path.Combine(targetDir, "Beta.md")),
                    "Pasting copied files into a subfolder must preserve original filenames.");
            });
        }

        private static void TestVaultExplorerCutMoveAndTabRemapping()
        {
            WithTempVault("NoteDrawCutMoveFilesTest", tempVault =>
            {
                string targetDir = Path.Combine(tempVault, "TargetDir");
                Directory.CreateDirectory(targetDir);
                string file3 = Path.Combine(tempVault, "Gamma.md");
                File.WriteAllText(file3, "# Gamma");

                var win = new MainWindow(tempVault);
                win.OpenVaultFileInTab(file3);
                Assert(win.OpenTabFilePaths.Any(p => string.Equals(p, Path.GetFullPath(file3), StringComparison.OrdinalIgnoreCase)),
                    "Gamma.md should be open in a tab before Cut+Paste.");

                win.SelectVaultEntry(file3, ctrlHeld: false, shiftHeld: false);
                win.CutSelectedVaultFiles();
                var movedToTarget = win.PasteVaultFiles(targetDir);
                string movedGamma = Path.Combine(targetDir, "Gamma.md");
                Assert(movedToTarget.Count == 1 && !File.Exists(file3) && File.Exists(movedGamma),
                    "Cut + Paste must move Gamma.md into TargetDir and remove the original file.");
                Assert(win.VaultClipboardPaths.Count == 0,
                    "Cut clipboard should clear immediately after Paste completes.");
                Assert(win.OpenTabFilePaths.Any(p => string.Equals(p, Path.GetFullPath(movedGamma), StringComparison.OrdinalIgnoreCase)),
                    "Cutting and pasting an open file must remap its open tab path cleanly.");
            });
        }

        private static void TestVaultExplorerMultiFileDelete()
        {
            WithTempVault("NoteDrawMultiDeleteFilesTest", tempVault =>
            {
                string fileA = Path.Combine(tempVault, "DeleteA.md");
                string fileB = Path.Combine(tempVault, "DeleteB.md");
                File.WriteAllText(fileA, "# A");
                File.WriteAllText(fileB, "# B");

                var win = new MainWindow(tempVault);
                win.OpenVaultFileInTab(fileA);
                win.SelectVaultEntry(fileA, ctrlHeld: false, shiftHeld: false);
                win.SelectVaultEntry(fileB, ctrlHeld: true, shiftHeld: false);
                win.DeleteSelectedVaultFiles();

                Assert(!File.Exists(fileA) && !File.Exists(fileB),
                    "DeleteSelectedVaultFiles must delete all selected files from disk.");
                Assert(!win.OpenTabFilePaths.Any(p => string.Equals(p, Path.GetFullPath(fileA), StringComparison.OrdinalIgnoreCase)),
                    "Deleting a selected file must close its open tab.");
            });
        }

        private static void TestSingleVaultIsolationAndNoStarterPollution()
        {
            WithTempVault("NoteDrawSingleVaultA", tempVaultA =>
                WithTempVault("NoteDrawSingleVaultB", tempVaultB =>
                {
                    string file1 = Path.Combine(tempVaultA, "Alpha.md");
                    File.WriteAllText(file1, "# Alpha");

                    var win = new MainWindow(tempVaultA);
                    win.OpenVaultFileInTab(file1);
                    win.SelectVaultEntry(file1, ctrlHeld: false, shiftHeld: false);
                    win.CopySelectedVaultFiles();

                    Assert(win.OpenVaultAtPath(tempVaultB, persistAsLastVault: false),
                        "OpenVaultAtPath should switch cleanly to tempVaultB.");
                    Assert(string.Equals(win.CurrentVaultPath, Path.GetFullPath(tempVaultB), StringComparison.OrdinalIgnoreCase),
                        "CurrentVaultPath must switch exclusively to tempVaultB.");
                    Assert(!File.Exists(Path.Combine(tempVaultB, "Welcome to NoteDraw.md")),
                        "Opening a user vault must NOT inject SampleVault starter files.");

                    win.SelectAllVaultFiles();
                    Assert(win.OpenTabFilePaths.Count == 0 &&
                           win.SelectedVaultPaths.Count == 0 &&
                           win.VaultClipboardPaths.Count == 0,
                        "Switching to an empty user vault must clear all open tabs, selections, file clipboard entries, and external folders from the previous vault.");
                }));
        }

        private static void TestInsertCanvasInDocPersistsInFileAcrossCloseSwapAndSave()
        {
            WithTempVault("NoteDrawInsertCanvasPersistTest", tempVault =>
                SaveAndRestoreFile(ThemeConfigService.GetOptionsFilePath(), () =>
                {
                    string noteAPath = Path.Combine(tempVault, "NoteA.md");
                    string noteBPath = Path.Combine(tempVault, "NoteB.md");
                    File.WriteAllText(noteAPath, "# Note A\n\nInitial paragraph.");
                    File.WriteAllText(noteBPath, "# Note B\n\nSecond file.");

                    var win = new MainWindow(tempVault);
                    win.SetAutoSaveEnabled(false);
                    win.OpenVaultFileInTab(noteAPath);
                    win.OpenVaultFileInTab(noteBPath);
                    win.OpenVaultFileInTab(noteAPath);

                    Assert(win.ActiveDocumentTab is not null &&
                           string.Equals(win.ActiveDocumentTab.FilePath, Path.GetFullPath(noteAPath), StringComparison.OrdinalIgnoreCase),
                        "NoteA.md must be active before inserting canvas.");

                    // Simulate user having clicked + typed on a line in Unified Editor & Preview before clicking "+ Canvas in Doc"
                    win.PreviewControl.ActivateSegmentAtLine(2, caretAtAbsoluteEnd: true);
                    win.InsertCanvasInDocument();

                    string noteAAfterInsertDisk = File.ReadAllText(noteAPath);
                    Assert(noteAAfterInsertDisk.Contains("![[canvas/", StringComparison.Ordinal) &&
                           noteAAfterInsertDisk.Contains(".ndraw]]", StringComparison.Ordinal),
                        $"NoteA.md on disk must immediately contain the inserted ![[canvas/...ndraw]] link even when Auto-Save is OFF, got:\n{noteAAfterInsertDisk}");

                    string vaultCanvasDir = Path.Combine(tempVault, "canvas");
                    Assert(Directory.Exists(vaultCanvasDir) &&
                           Directory.GetFiles(vaultCanvasDir, "*.ndraw").Length == 1,
                        "InsertCanvasInDocument must create the .ndraw file inside <vault>/canvas.");

                    // Draw a stroke in the opened inline canvas editor and click Done Editing Image
                    var workspace = FindControlType<DrawingWorkspaceControl>(win.PreviewControl.RootPanel);
                    Assert(workspace is not null, "InsertCanvasInDocument should open the inline canvas workspace.");
                    workspace!.Canvas.AddStroke(new DrawingStroke
                    {
                        Tool = DrawingToolKind.Pen,
                        ColorHex = "#2563EB",
                        Thickness = 3.5,
                        Opacity = 1.0,
                        Points = new List<DrawingPoint> { new(20, 20), new(120, 80) }
                    });

                    var doneBtn = FindButtonByText(win.PreviewControl.RootPanel, "✓ Done Editing Image");
                    Assert(doneBtn is not null, "Inline canvas editor must expose '✓ Done Editing Image' button.");
                    doneBtn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, doneBtn));

                    // Swap to NoteB, close NoteA, then reopen NoteA from disk
                    win.OpenVaultFileInTab(noteBPath);
                    Assert(win.CloseOpenTab(noteAPath), "Closing NoteA tab should succeed.");
                    win.OpenVaultFileInTab(noteAPath);

                    string reopenedDisk = File.ReadAllText(noteAPath);
                    Assert(reopenedDisk.Contains("![[canvas/", StringComparison.Ordinal) &&
                           win.ActiveDocumentTab is not null &&
                           win.ActiveDocumentTab.Content.Contains("![[canvas/", StringComparison.Ordinal),
                        $"Reopening NoteA.md after Done Editing, tab swap, and close must preserve the canvas link, got:\n{reopenedDisk}");

                    var reopenedCanvas = FindControlType<DrawingCanvasControl>(win.PreviewControl.RootPanel);
                    Assert(reopenedCanvas is not null && reopenedCanvas.Document.Strokes.Count == 1,
                        $"Reopened NoteA preview canvas must display the saved stroke, got count={reopenedCanvas?.Document.Strokes.Count ?? -1}.");
                }));
        }

        private static void TestVaultLocalImageAndCanvasStorageInActiveVault()
        {
            string exeImgDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "image"));
            string exeCanvasDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "canvas"));
            var beforeExeImgs = Directory.Exists(exeImgDir)
                ? new HashSet<string>(Directory.GetFiles(exeImgDir), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var beforeExeCanvases = Directory.Exists(exeCanvasDir)
                ? new HashSet<string>(Directory.GetFiles(exeCanvasDir), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            WithTempVault("NoteDrawVaultAssetsA", tempVaultA =>
                WithTempVault("NoteDrawVaultAssetsB", tempVaultB =>
                    SaveAndRestoreFile(ThemeConfigService.GetOptionsFilePath(), () =>
                    {
                        string sourceImgPath = Path.Combine(Path.GetTempPath(), $"notedraw_src_{Guid.NewGuid():N}.png");
                        try
                        {
                            using (var bmp = MarkdownPreviewControl.RenderCanvasToBitmap(CreateSampleDrawingDocument(), 120, 80))
                            {
                                bmp.Save(sourceImgPath);
                            }

                            string noteA = Path.Combine(tempVaultA, "VaultANote.md");
                            string noteB = Path.Combine(tempVaultB, "VaultBNote.md");
                            File.WriteAllText(noteA, "# Vault A\n\nBody A");
                            File.WriteAllText(noteB, "# Vault B\n\nBody B");

                            var win = new MainWindow(tempVaultA);
                            win.OpenVaultFileInTab(noteA);

                            Assert(string.Equals(win.AssetService.ImageFolderPath, Path.GetFullPath(Path.Combine(tempVaultA, "image")), StringComparison.OrdinalIgnoreCase) &&
                                   string.Equals(win.AssetService.CanvasFolderPath, Path.GetFullPath(Path.Combine(tempVaultA, "canvas")), StringComparison.OrdinalIgnoreCase),
                                $"Asset folders must target Vault A ({tempVaultA}), got img={win.AssetService.ImageFolderPath}, canvas={win.AssetService.CanvasFolderPath}.");

                            Assert(win.DropImageFileIntoDocumentAtLine(sourceImgPath, 1),
                                "Importing/dropping image into Vault A note must succeed.");
                            win.InsertCanvasInDocument();

                            Assert(Directory.GetFiles(Path.Combine(tempVaultA, "image")).Length == 1,
                                "Vault A image folder must contain the imported image.");
                            Assert(Directory.GetFiles(Path.Combine(tempVaultA, "canvas"), "*.ndraw").Length == 1,
                                "Vault A canvas folder must contain the created .ndraw canvas.");

                            Assert(win.OpenVaultAtPath(tempVaultB, persistAsLastVault: false),
                                "OpenVaultAtPath must switch cleanly to Vault B.");
                            win.OpenVaultFileInTab(noteB);

                            Assert(string.Equals(win.AssetService.ImageFolderPath, Path.GetFullPath(Path.Combine(tempVaultB, "image")), StringComparison.OrdinalIgnoreCase) &&
                                   string.Equals(win.AssetService.CanvasFolderPath, Path.GetFullPath(Path.Combine(tempVaultB, "canvas")), StringComparison.OrdinalIgnoreCase),
                                $"Asset folders must switch to Vault B ({tempVaultB}), got img={win.AssetService.ImageFolderPath}, canvas={win.AssetService.CanvasFolderPath}.");

                            Assert(win.DropImageFileIntoDocumentAtLine(sourceImgPath, 1),
                                "Importing/dropping image into Vault B note must succeed.");
                            win.InsertCanvasInDocument();

                            Assert(Directory.GetFiles(Path.Combine(tempVaultB, "image")).Length == 1,
                                "Vault B image folder must contain the newly added image.");
                            Assert(Directory.GetFiles(Path.Combine(tempVaultB, "canvas"), "*.ndraw").Length == 1,
                                "Vault B canvas folder must contain the newly added .ndraw canvas.");

                            var afterExeImgs = Directory.Exists(exeImgDir)
                                ? Directory.GetFiles(exeImgDir).Where(f => !beforeExeImgs.Contains(f)).ToList()
                                : new List<string>();
                            var afterExeCanvases = Directory.Exists(exeCanvasDir)
                                ? Directory.GetFiles(exeCanvasDir).Where(f => !beforeExeCanvases.Contains(f)).ToList()
                                : new List<string>();
                            Assert(afterExeImgs.Count == 0 && afterExeCanvases.Count == 0,
                                "Vault image and canvas operations must NOT write new files into <exe>/image or <exe>/canvas.");
                        }
                        finally
                        {
                            if (File.Exists(sourceImgPath))
                                File.Delete(sourceImgPath);
                        }
                    })));
        }

        private static void TestViewModeDropdownWorkspaceViewsSubMenu()
        {
            WithTempVault("NoteDrawViewModeSubMenuTest", tempVault =>
                SaveAndRestoreFile(ThemeConfigService.GetOptionsFilePath(), () =>
                {
                    var win = new MainWindow(tempVault);

                    Assert(!win.RibbonTabLabels.Contains("View"),
                        $"Ribbon must no longer host a separate 'View' tab; got tabs: {string.Join(", ", win.RibbonTabLabels)}");
                    Assert(win.RibbonTabLabels.Contains("Drawing Tools") && win.RibbonTabLabels.Contains("🔀 Git"),
                        $"Ribbon must still host 'Drawing Tools' and '🔀 Git'; got tabs: {string.Join(", ", win.RibbonTabLabels)}");

                    var viewModeMenu = win.TopMenuBar.Items
                        .OfType<MenuItem>()
                        .FirstOrDefault(m => string.Equals(m.Header?.ToString(), "View & Mode", StringComparison.Ordinal));
                    Assert(viewModeMenu is not null, "Top menu bar must expose 'View & Mode' dropdown menu.");

                    var viewSubMenu = viewModeMenu!.Items
                        .OfType<MenuItem>()
                        .FirstOrDefault(m => string.Equals(m.Header?.ToString(), "View", StringComparison.Ordinal));
                    Assert(viewSubMenu is not null, "'View & Mode' menu must host a 'View' submenu for Workspace Views.");

                    var viewItems = viewSubMenu!.Items.OfType<MenuItem>().ToList();
                    int expectedCount = Enum.GetValues<WorkspaceViewKind>().Length;
                    Assert(viewItems.Count == expectedCount,
                        $"Expected {expectedCount} Workspace View toggle items in 'View' submenu, got {viewItems.Count}.");
                    Assert(viewItems.All(item => (item.Header?.ToString() ?? string.Empty).StartsWith("[✓] ", StringComparison.Ordinal)),
                        "All Workspace View submenu items must initially display '[✓] '.");

                    var vaultExplorerItem = viewItems.FirstOrDefault(item =>
                        string.Equals(item.Header?.ToString(), $"[✓] {ViewLayoutState.GetDisplayName(WorkspaceViewKind.VaultExplorer)}", StringComparison.Ordinal));
                    Assert(vaultExplorerItem is not null, "'View' submenu must include Vault Explorer toggle.");

                    vaultExplorerItem!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, vaultExplorerItem));
                    Assert(string.Equals(vaultExplorerItem.Header?.ToString(), $"[ ] {ViewLayoutState.GetDisplayName(WorkspaceViewKind.VaultExplorer)}", StringComparison.Ordinal),
                        $"Clicking Vault Explorer submenu item must toggle header to '[ ] ...', got '{vaultExplorerItem.Header}'.");

                    var resetItem = viewModeMenu.Items
                        .OfType<MenuItem>()
                        .FirstOrDefault(m => string.Equals(m.Header?.ToString(), "Reset All Views", StringComparison.Ordinal));
                    Assert(resetItem is not null, "'View & Mode' menu must retain 'Reset All Views'.");

                    resetItem!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, resetItem));
                    Assert(string.Equals(vaultExplorerItem.Header?.ToString(), $"[✓] {ViewLayoutState.GetDisplayName(WorkspaceViewKind.VaultExplorer)}", StringComparison.Ordinal),
                        $"Clicking 'Reset All Views' must restore Vault Explorer submenu header to '[✓] ...', got '{vaultExplorerItem.Header}'.");
                }));
        }

        private static void TestCrashLoggerLog4NetOutput()
        {
            string expectedDefaultLogsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Logs"));
            Assert(string.Equals(CrashLoggerService.LogsDirectoryPath, expectedDefaultLogsDir, StringComparison.OrdinalIgnoreCase),
                $"CrashLoggerService.LogsDirectoryPath must target <exe>/Logs ({expectedDefaultLogsDir}), got {CrashLoggerService.LogsDirectoryPath}.");

            WithTempVault("NoteDrawCrashLogTest", tempDir =>
            {
                string testLogsDir = Path.Combine(tempDir, "Logs");
                try
                {
                    CrashLoggerService.Initialize(testLogsDir);
                    Assert(Directory.Exists(testLogsDir) &&
                           string.Equals(CrashLoggerService.ActiveLogsDirectory, Path.GetFullPath(testLogsDir), StringComparison.OrdinalIgnoreCase),
                        "Initialize must create and target the configured Logs directory.");

                    CrashLoggerService.LogError("SelfTest.NonFatalError",
                        new InvalidOperationException("Simulated recoverable error"));
                    CrashLoggerService.LogFatalCrash("SelfTest.SimulatedCrash",
                        new ApplicationException("Simulated fatal crash", new IOException("Inner disk failure")),
                        isTerminating: true);
                }
                finally
                {
                    CrashLoggerService.Shutdown();
                }

                string[] logFiles = Directory.Exists(testLogsDir)
                    ? Directory.GetFiles(testLogsDir, "*.log")
                    : Array.Empty<string>();
                Assert(logFiles.Length > 0,
                    $"log4net RollingFileAppender must create at least one .log file in {testLogsDir}.");

                string combined = string.Join(Environment.NewLine, logFiles.Select(File.ReadAllText));
                Assert(combined.Contains("ERROR", StringComparison.Ordinal) &&
                       combined.Contains("FATAL", StringComparison.Ordinal) &&
                       combined.Contains("Simulated recoverable error", StringComparison.Ordinal) &&
                       combined.Contains("Simulated fatal crash", StringComparison.Ordinal) &&
                       combined.Contains("Inner disk failure", StringComparison.Ordinal),
                    $"Log file output missing expected crash/error details:\n{combined}");
            });
        }

        private static void TestRibbonSwitchBetweenDrawingToolsAndGitDoesNotCrash()
        {
            WithTempVault("NoteDrawRibbonSwitchTest", tempVault =>
                SaveAndRestoreFile(ThemeConfigService.GetOptionsFilePath(), () =>
                {
                    var win = new MainWindow(tempVault);
                    win.SelectRibbonTab(RibbonTabKind.DrawingTools);
                    win.SelectRibbonTab(RibbonTabKind.Git);
                    win.SelectRibbonTab(RibbonTabKind.DrawingTools);
                    win.SelectRibbonTab(RibbonTabKind.Git);
                    win.SelectRibbonTab(RibbonTabKind.Git);
                }));
        }

        private static void TestPlatformShortcutsMacCmdVsWindowsLinuxCtrl()
        {
            try
            {
                // 1. macOS mode: Meta (Cmd) is active command key, Control is ignored
                PlatformShortcuts.IsMacOsOverride = true;
                Assert(PlatformShortcuts.IsMacOS &&
                       PlatformShortcuts.PrimaryModifier == KeyModifiers.Meta &&
                       PlatformShortcuts.PrimaryModifierLabel == "Cmd" &&
                       PlatformShortcuts.Format("S") == "Cmd+S",
                    "macOS mode must report Cmd / KeyModifiers.Meta as PrimaryModifier.");
                Assert(PlatformShortcuts.HasPrimaryModifier(KeyModifiers.Meta) &&
                       !PlatformShortcuts.HasPrimaryModifier(KeyModifiers.Control),
                    "macOS mode must match KeyModifiers.Meta and reject KeyModifiers.Control.");

                var macCanvas = new DrawingCanvasControl();
                macCanvas.AddStroke(new DrawingStroke
                {
                    Tool = DrawingToolKind.Pen,
                    Points = new List<DrawingPoint> { new(10, 10), new(50, 50) }
                });
                macCanvas.ActiveTool = DrawingToolKind.Pipette;

                macCanvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.P, KeyModifiers = KeyModifiers.Meta, Source = macCanvas });
                Assert(macCanvas.ActiveTool == DrawingToolKind.Pipette,
                    $"Cmd+P on macOS must not trigger Pen shortcut, got {macCanvas.ActiveTool}.");

                macCanvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Z, KeyModifiers = KeyModifiers.Control, Source = macCanvas });
                Assert(macCanvas.Document.Strokes.Count == 1,
                    "Ctrl+Z on macOS must NOT trigger Undo when Cmd is the command key.");

                macCanvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Z, KeyModifiers = KeyModifiers.Meta, Source = macCanvas });
                Assert(macCanvas.Document.Strokes.Count == 0,
                    "Cmd+Z (Meta+Z) on macOS must trigger Undo.");

                WithTempVault("NoteDrawMacCmdMenuTest", tempVault =>
                    SaveAndRestoreFile(ThemeConfigService.GetOptionsFilePath(), () =>
                    {
                        var win = new MainWindow(tempVault);
                        var fileMenu = win.TopMenuBar.Items.OfType<MenuItem>().FirstOrDefault(m => string.Equals(m.Header?.ToString(), "File", StringComparison.Ordinal));
                        Assert(fileMenu is not null, "MainWindow must expose File menu.");
                        var menuHeaders = fileMenu!.Items.OfType<MenuItem>().Select(m => m.Header?.ToString() ?? string.Empty).ToList();
                        Assert(menuHeaders.Any(h => h.Contains("Cmd+S", StringComparison.Ordinal)) &&
                               !menuHeaders.Any(h => h.Contains("Ctrl+", StringComparison.Ordinal)),
                            $"macOS File menu labels must display Cmd+ shortcuts instead of Ctrl+, got: {string.Join(", ", menuHeaders)}");
                    }));

                // 2. Windows / Linux mode: Control (Ctrl) is active command key, Meta is ignored
                PlatformShortcuts.IsMacOsOverride = false;
                Assert(!PlatformShortcuts.IsMacOS &&
                       PlatformShortcuts.PrimaryModifier == KeyModifiers.Control &&
                       PlatformShortcuts.PrimaryModifierLabel == "Ctrl" &&
                       PlatformShortcuts.Format("S") == "Ctrl+S",
                    "Windows/Linux mode must report Ctrl / KeyModifiers.Control as PrimaryModifier.");
                Assert(PlatformShortcuts.HasPrimaryModifier(KeyModifiers.Control) &&
                       !PlatformShortcuts.HasPrimaryModifier(KeyModifiers.Meta),
                    "Windows/Linux mode must match KeyModifiers.Control and reject KeyModifiers.Meta.");

                var winCanvas = new DrawingCanvasControl();
                winCanvas.AddStroke(new DrawingStroke
                {
                    Tool = DrawingToolKind.Pen,
                    Points = new List<DrawingPoint> { new(10, 10), new(50, 50) }
                });

                winCanvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Z, KeyModifiers = KeyModifiers.Meta, Source = winCanvas });
                Assert(winCanvas.Document.Strokes.Count == 1,
                    "Meta+Z on Windows/Linux must NOT trigger Undo.");

                winCanvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Z, KeyModifiers = KeyModifiers.Control, Source = winCanvas });
                Assert(winCanvas.Document.Strokes.Count == 0,
                    "Ctrl+Z on Windows/Linux must trigger Undo.");
            }
            finally
            {
                PlatformShortcuts.IsMacOsOverride = null;
            }
        }

        private static WorkspaceHostCallbacks CreateTestHostCallbacks(
            string vaultPath,
            Action<GitRepositoryStatus>? onGitStatusUpdated = null,
            Action<UnsavedExitDecision, bool>? onUnsavedExitDecision = null)
        {
            return new WorkspaceHostCallbacks
            {
                GetCurrentVaultPath = () => vaultPath,
                GetVaultEntries = _ => Array.Empty<VaultEntry>(),
                GetAutoSaveEnabled = () => false,
                GetHasUnsavedDocuments = () => false,
                GetCurrentPalette = ThemePalette.CreateClassicDark,
                GetIsDarkTheme = () => true,
                OpenFileInTab = _ => { },
                JumpEditorToLine = _ => { },
                FilterVaultByTag = _ => { },
                InsertLatexSnippet = _ => { },
                CloseView = _ => { },
                RefreshVaultTree = () => { },
                SaveAllDirtyTabs = () => { },
                ApplyUnsavedExitDecision = onUnsavedExitDecision ?? ((_, _) => { }),
                OnGitStatusUpdated = onGitStatusUpdated ?? (_ => { }),
                NotifyNonVaultPanelFocused = () => { }
            };
        }

        private static void TestWorkspaceMediatorStatusAndGitBroadcast()
        {
            GitRepositoryStatus? capturedGitStatus = null;
            var callbacks = CreateTestHostCallbacks(
                Path.GetTempPath(),
                onGitStatusUpdated: s => capturedGitStatus = s);
            var mediator = new WorkspaceMediator(callbacks);
            var statusBar = new StatusBarPanel(mediator);
            var outlineAndTags = new OutlineAndTagsPanel(mediator);
            var liveLatex = new LiveLatexPreviewPanel(mediator);
            mediator.RegisterPanels(statusBar, outlineAndTags, liveLatex);

            mediator.PostStatusMessage("Saved test document");
            mediator.UpdateCaretStatus("Ln 12, Col 4");
            mediator.UpdateLatexStatus("LaTeX: Display");
            Assert(statusBar.SaveStatusText == "Saved test document" &&
                   statusBar.CaretStatusText == "Ln 12, Col 4" &&
                   statusBar.LatexStatusText == "LaTeX: Display",
                "Mediator status updates must route cleanly to StatusBarPanel.");

            mediator.NotifyStatusBarRefresh(new StatusBarSnapshot(
                SaveText: "✓ Saved",
                CountsText: "10 words • 60 chars",
                CaretText: null,
                ViewsText: "Views: 7/7"));
            Assert(statusBar.SaveStatusText == "✓ Saved" &&
                   statusBar.CountsStatusText == "10 words • 60 chars" &&
                   statusBar.CaretStatusText == "Ln 12, Col 4",
                "Snapshot refresh with null CaretText must preserve existing caret label.");

            var gitStatus = new GitRepositoryStatus
            {
                IsGitAvailable = true,
                IsRepository = true,
                BranchName = "feature/mediator",
                Changes = new List<GitFileChange>
                {
                    new("M", "Note.md", "Modified")
                }
            };
            mediator.NotifyGitStatusUpdated(gitStatus);
            Assert(statusBar.GitStatusText == "🌿 feature/mediator (1 changed)" &&
                   capturedGitStatus is not null &&
                   capturedGitStatus.BranchName == "feature/mediator",
                $"NotifyGitStatusUpdated must broadcast to StatusBarPanel and host callback; got '{statusBar.GitStatusText}'.");
        }

        private static void TestWorkspaceMediatorOverlayLifecycleAndEscape()
        {
            UnsavedExitDecision? lastDecision = null;
            var callbacks = CreateTestHostCallbacks(
                Path.GetTempPath(),
                onUnsavedExitDecision: (decision, _) => lastDecision = decision);
            var mediator = new WorkspaceMediator(callbacks);

            var unsavedExit = new UnsavedExitOverlay(mediator);
            var prompt = new PromptDialogOverlay(mediator);
            var quickSwitcher = new QuickSwitcherOverlay(mediator);
            var gitOverlay = new GitOverlayPanel(mediator);
            var syntaxGuide = new SyntaxGuideOverlay();

            mediator.RegisterOverlays(unsavedExit, prompt, quickSwitcher, gitOverlay, syntaxGuide);
            Assert(!mediator.HasOpenOverlay && mediator.OverlayControls.Count == 5,
                "All 5 overlays must register and start closed.");

            mediator.ToggleSyntaxGuide();
            Assert(mediator.HasOpenOverlay && syntaxGuide.IsOpen,
                "ToggleSyntaxGuide must open SyntaxGuideOverlay.");
            mediator.ShowPrompt("Test Prompt", "Default", _ => { });
            mediator.ShowUnsavedExitDialog(new[] { "• Note1.md" });
            Assert(mediator.IsUnsavedExitOpen && unsavedExit.IsOpen && prompt.IsOpen && syntaxGuide.IsOpen,
                "Multiple overlays should report open state accurately through mediator.");

            Assert(mediator.TryDismissTopOverlay() && lastDecision == UnsavedExitDecision.Cancel,
                "First Escape must dismiss highest-priority UnsavedExitOverlay with Cancel decision.");
            unsavedExit.Hide();

            Assert(mediator.TryDismissTopOverlay() && !prompt.IsOpen,
                "Second Escape must dismiss PromptDialogOverlay.");
            Assert(mediator.TryDismissTopOverlay() && !syntaxGuide.IsOpen && !mediator.HasOpenOverlay,
                "Third Escape must dismiss remaining SyntaxGuideOverlay.");
            Assert(!mediator.TryDismissTopOverlay(),
                "TryDismissTopOverlay must return false when no overlay is open.");
        }
    }
}
