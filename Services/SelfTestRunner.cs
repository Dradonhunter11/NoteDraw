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
    public static class SelfTestRunner
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
                ("MarkdownPreviewControl: clicking next to a picture places classic cursor beside picture", TestClickNextToPicturePlacesCursorAlongsidePicture)
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

        private static void TestLiveDetectorFencedLatex()
        {
            string md = "# Notes\n\n```latex\n\\frac{a}{b} = c\n```\n\nEnd.";
            int caret = md.IndexOf("\\frac", StringComparison.Ordinal) + 3;
            var ctx = LatexLiveDetector.DetectAtCursor(md, caret);

            Assert(ctx.IsFound, "Expected ```latex block to be detected.");
            Assert(ctx.BlockKind == "```latex block", $"Unexpected BlockKind: {ctx.BlockKind}");
            Assert(ctx.LatexCode == "\\frac{a}{b} = c", $"Unexpected LatexCode: {ctx.LatexCode}");
            Assert(ctx.IsDisplayMode, "Expected display mode true.");
            Assert(ctx.IsClosed, "Expected closed fence.");
        }

        private static void TestLiveDetectorUnclosedFence()
        {
            string md = "# Draft\n```latex\n\\sum_{i=1}^n i";
            int caret = md.Length;
            var ctx = LatexLiveDetector.DetectAtCursor(md, caret);

            Assert(ctx.IsFound, "Expected unclosed ```latex block to be detected while typing.");
            Assert(!ctx.IsClosed, "Expected IsClosed to be false for unclosed fence.");
            Assert(ctx.LatexCode == "\\sum_{i=1}^n i", $"Unexpected partial code: {ctx.LatexCode}");
        }

        private static void TestLiveDetectorMathAndDollars()
        {
            string mdMath = "```math\nx^2 + y^2 = r^2\n```";
            var ctxMath = LatexLiveDetector.DetectAtCursor(mdMath, mdMath.IndexOf("x^2", StringComparison.Ordinal));
            Assert(ctxMath.IsFound && ctxMath.BlockKind == "```math block", "Expected ```math block.");

            string mdDisplay = "Before\n$$\nA = \\begin{pmatrix} 1 & 2 \\\\ 3 & 4 \\end{pmatrix}\n$$\nAfter";
            var ctxDisp = LatexLiveDetector.DetectAtCursor(mdDisplay, mdDisplay.IndexOf("pmatrix", StringComparison.Ordinal));
            Assert(ctxDisp.IsFound && ctxDisp.BlockKind == "$$ display block", "Expected $$ display block.");

            string mdInline = "Euler formula $e^{i\\pi} + 1 = 0$ holds.";
            var ctxInline = LatexLiveDetector.DetectAtCursor(mdInline, mdInline.IndexOf("pi", StringComparison.Ordinal));
            Assert(ctxInline.IsFound && ctxInline.BlockKind == "$ inline math", "Expected $ inline math.");
            Assert(!ctxInline.IsDisplayMode, "Inline math should have IsDisplayMode=false.");
            Assert(ctxInline.LatexCode == "e^{i\\pi} + 1 = 0", $"Unexpected inline code: {ctxInline.LatexCode}");
        }

        private static void TestLiveDetectorIgnoresDrawing()
        {
            string md = "```drawing\n{\"strokes\":[],\"note\":\"$not_math$\"}\n```";
            int caret = md.IndexOf("$not_math$", StringComparison.Ordinal) + 2;
            var ctx = LatexLiveDetector.DetectAtCursor(md, caret);
            Assert(!ctx.IsFound, "Drawing code blocks must not be misdetected as LaTeX.");
        }

        private static void TestLatexParserAndMeasure()
        {
            var r1 = LatexParser.Parse("\\frac{a+1}{b-1}", isDisplayMode: true);
            Assert(!r1.HasWarnings, "Simple fraction should have 0 warnings.");
            Assert(r1.Root is FractionNode, "Expected FractionNode.");
            var b1 = LatexMathRenderer.Measure(r1.Root, 20, Brushes.White, true);
            Assert(b1.Width > 10 && b1.Height > 20, "Measured fraction box should have positive dimensions.");

            var r2 = LatexParser.Parse("\\int_0^\\infty e^{-x^2}\\,dx = \\frac{\\sqrt{\\pi}}{2}", isDisplayMode: true);
            Assert(!r2.HasWarnings, "Gaussian integral should parse without warnings.");
            var b2 = LatexMathRenderer.Measure(r2.Root, 20, Brushes.White, true);
            Assert(b2.Width > 40 && b2.Height > 20, "Measured integral equation should have positive dimensions.");

            var r3 = LatexParser.Parse("\\begin{pmatrix} a & b \\\\ c & d \\end{pmatrix}", isDisplayMode: true);
            Assert(!r3.HasWarnings, "Matrix should parse without warnings.");
            Assert(r3.Root is MatrixNode { Rows.Count: 2 }, "Expected 2-row MatrixNode.");
            var b3 = LatexMathRenderer.Measure(r3.Root, 18, Brushes.White, true);
            Assert(b3.Width > 20 && b3.Height > 25, "Measured matrix box should have positive dimensions.");

            var partial = LatexParser.Parse("\\frac{1}{", isDisplayMode: true);
            Assert(partial.HasWarnings, "Unclosed \\frac should emit non-blocking diagnostic warning.");
            var bPartial = LatexMathRenderer.Measure(partial.Root, 18, Brushes.White, true);
            Assert(bPartial.Width > 0 && bPartial.Height > 0, "Partial expression should still measure cleanly.");
        }

        private static void TestDrawingDocument()
        {
            var doc = new DrawingDocument
            {
                Background = CanvasBackgroundKind.Dotted,
                Strokes =
                {
                    new DrawingStroke
                    {
                        Tool = DrawingToolKind.Pen,
                        ColorHex = "#3B82F6",
                        Thickness = 3,
                        Points = { new(10, 10), new(50, 40) }
                    },
                    new DrawingStroke
                    {
                        Tool = DrawingToolKind.Highlighter,
                        ColorHex = "#FACC15",
                        Thickness = 14,
                        Opacity = 0.36,
                        Points = { new(20, 60), new(120, 60) }
                    },
                    new DrawingStroke
                    {
                        Tool = DrawingToolKind.Rectangle,
                        ColorHex = "#10B981",
                        Thickness = 2,
                        Points = { new(80, 20), new(180, 100) }
                    },
                    new DrawingStroke
                    {
                        Tool = DrawingToolKind.TextLabel,
                        ColorHex = "AUTO",
                        TextContent = "\\frac{x}{2}",
                        IsLatex = true,
                        Points = { new(95, 50) }
                    }
                }
            };

            string json = doc.ToJson();
            var roundTrip = DrawingDocument.FromJson(json);
            Assert(roundTrip.Background == CanvasBackgroundKind.Dotted, "Background should round-trip.");
            Assert(roundTrip.Strokes.Count == 4, "All 4 strokes should round-trip.");
            Assert(roundTrip.Strokes[3].IsLatex && roundTrip.Strokes[3].TextContent == "\\frac{x}{2}", "LaTeX label should round-trip.");

            var clone = roundTrip.Clone();
            clone.Strokes.RemoveAt(0);
            Assert(roundTrip.Strokes.Count == 4 && clone.Strokes.Count == 3, "Clone should isolate mutations.");

            string svg = doc.ExportToSvg(isDarkTheme: true);
            Assert(svg.Contains("<svg", StringComparison.Ordinal) &&
                   svg.Contains("<path", StringComparison.Ordinal) &&
                   svg.Contains("<rect", StringComparison.Ordinal) &&
                   svg.Contains("<text", StringComparison.Ordinal),
                "SVG export should emit expected vector primitives.");
        }

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

        private static void TestDrawingCanvasUndoRedo()
        {
            var canvas = new DrawingCanvasControl();
            canvas.LoadDocument(new DrawingDocument { Background = CanvasBackgroundKind.Grid });
            Assert(!canvas.CanUndo && !canvas.CanRedo, "Fresh canvas should have empty history.");

            // Add label, change background, clear canvas
            canvas.AddTextLabel(new Point(40, 40), "$E = mc^2$");
            canvas.SetBackground(CanvasBackgroundKind.Dotted);
            canvas.AddTextLabel(new Point(80, 120), "Second Label");
            Assert(canvas.Document.Strokes.Count == 2 && canvas.UndoCount == 3, "Expected 2 strokes and 3 undo steps.");

            canvas.ClearCanvas();
            Assert(canvas.Document.Strokes.Count == 0 && canvas.UndoCount == 4, "Clear should push undo snapshot.");

            // Undo clear -> 2 strokes
            Assert(canvas.Undo(), "Undo clear should succeed.");
            Assert(canvas.Document.Strokes.Count == 2, "Undo clear should restore both strokes.");

            // Undo second label -> 1 stroke
            Assert(canvas.Undo(), "Undo second label should succeed.");
            Assert(canvas.Document.Strokes.Count == 1, "Expected 1 stroke remaining.");

            // Undo background -> Grid
            Assert(canvas.Undo(), "Undo background should succeed.");
            Assert(canvas.Document.Background == CanvasBackgroundKind.Grid, "Background should revert to Grid.");

            // Redo background & second label
            Assert(canvas.Redo() && canvas.Document.Background == CanvasBackgroundKind.Dotted, "Redo should restore Dotted background.");
            Assert(canvas.Redo() && canvas.Document.Strokes.Count == 2, "Redo should restore second stroke.");

            // History preservation round-trip
            var saved = canvas.GetHistoryState();
            var clonedCanvas = new DrawingCanvasControl();
            clonedCanvas.LoadDocument(canvas.Document);
            clonedCanvas.RestoreHistoryState(saved.Undo, saved.Redo);
            Assert(clonedCanvas.UndoCount == canvas.UndoCount && clonedCanvas.RedoCount == canvas.RedoCount,
                "Restored history counts should match.");
        }

        private static void TestTextUndoManagerMultiChar()
        {
            var mgr = new TextUndoManager(string.Empty, 0);
            DateTime t = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
            string text = string.Empty;

            // Simulate typing "Hello world" char-by-char in one burst
            foreach (char ch in "Hello world")
            {
                string next = text + ch;
                mgr.RecordEdit(text, text.Length, next, next.Length, t);
                text = next;
                t = t.AddMilliseconds(80);
            }

            // A single Undo must revert more than 1 character (the whole burst)
            Assert(mgr.Undo(text, text.Length, out var undone), "Undo should succeed after typing.");
            Assert(undone.Text == string.Empty, $"Expected multi-char undo back to empty, got '{undone.Text}'.");
            Assert(mgr.Redo(undone.Text, undone.CaretIndex, out var redone) && redone.Text == "Hello world",
                "Redo should restore the entire typed string.");

            // Simulate pause (>1200ms) then typing "!" and backspacing 3 chars
            t = t.AddMilliseconds(2000);
            string afterPause = "Hello world!!";
            mgr.RecordEdit("Hello world", 11, "Hello world!", 12, t);
            t = t.AddMilliseconds(50);
            mgr.RecordEdit("Hello world!", 12, afterPause, 13, t);

            Assert(mgr.Undo(afterPause, 13, out var undoneSecond) && undoneSecond.Text == "Hello world",
                $"Expected second burst undo to 'Hello world', got '{undoneSecond.Text}'.");
        }

        private static void TestChecklistAndLatexPreviewRendering()
        {
            // Parser extensions: array, operatorname*, boxed, comments, displaystyle, big delimiters
            var arr = LatexParser.Parse("\\displaystyle \\operatorname*{argmax}_{x} \\boxed{x+1} + \\begin{array}{cc} 1 & 2 \\\\ 3 & 4 \\end{array} % comment", true);
            Assert(!arr.HasWarnings, $"Extended LaTeX constructs should parse with 0 warnings, got: {string.Join("; ", arr.Diagnostics)}");
            Assert(LatexMathRenderer.Measure(arr.Root, 18, Brushes.White, true).Width > 40, "Extended LaTeX should measure positively.");

            // Normalize inline-started $$...$$ blocks and \[...\]
            string rawMd =
@"- [x] **Closable Views**: Click `×` on any panel header and restore from **View** tab.
- [ ] **Live LaTeX**: Place cursor inside `latex` block or [[Welcome to NoteDraw]].

$$\begin{pmatrix}
a & b \\
c & d
\end{pmatrix}$$

\[ \frac{1}{2} + \big( x^2_i \big) \]
";
            string normalized = MarkdownPreviewControl.NormalizeMathBlocks(rawMd);
            Assert(normalized.Contains("$$\n\\begin{pmatrix}") && normalized.Contains("\\end{pmatrix}\n$$"),
                "NormalizeMathBlocks should split $$ lines around matrices.");

            var preview = new MarkdownPreviewControl { IsDarkTheme = true };
            preview.RenderMarkdown(rawMd);
            preview.Measure(new Size(720, 1400));
            preview.Arrange(new Rect(0, 0, 720, 1400));
            preview.RootPanel.Measure(new Size(720, double.PositiveInfinity));

            using var rtb = new RenderTargetBitmap(new PixelSize(720, 900), new Vector(96, 96));
            rtb.Render(preview);

            Assert(preview.RootPanel.Children.Count >= 3,
                $"Expected checklist + 2 display math cards (>=3 children), got {preview.RootPanel.Children.Count}");
            Assert(preview.RootPanel.DesiredSize.Height > 120,
                $"Expected unclipped preview layout height > 120, got {preview.RootPanel.DesiredSize.Height}");
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

        private static void TestDrawingCanvasWhiteSurfaceAndMousePreview()
        {
            var workspace = new DrawingWorkspaceControl(compactInlineMode: false, isDarkTheme: true);
            var canvas = workspace.Canvas;
            canvas.LoadDocument(new DrawingDocument { Background = CanvasBackgroundKind.Grid });

            canvas.Measure(new Size(640, 400));
            canvas.Arrange(new Rect(0, 0, 640, 400));
            canvas.SetHoverPositionForPreview(new Point(120, 80));
            Assert(canvas.HoverCanvasPosition.HasValue &&
                   Math.Abs(canvas.HoverCanvasPosition.Value.X - 120) < 0.01 &&
                   Math.Abs(canvas.HoverCanvasPosition.Value.Y - 80) < 0.01,
                "HoverCanvasPosition should track SetHoverPositionForPreview(120, 80).");

            using var rtb = new RenderTargetBitmap(new PixelSize(640, 400), new Vector(96, 96));
            rtb.Render(canvas);
        }

        private static void TestDrawingWorkspaceContextSubMenuAndColorSelector()
        {
            var workspace = new DrawingWorkspaceControl(compactInlineMode: false, isDarkTheme: true);
            var canvas = workspace.Canvas;
            canvas.LoadDocument(new DrawingDocument { Background = CanvasBackgroundKind.Grid });

            Assert(!workspace.IsContextSubMenuOpen, "Sub-menu should start closed.");
            workspace.ShowContextSubMenuAt(new Point(100, 100));
            Assert(workspace.IsContextSubMenuOpen, "ShowContextSubMenuAt should open the sub-menu.");
            workspace.SelectCustomColor("#EC4899");
            Assert(string.Equals(canvas.ActiveColorHex, "#EC4899", StringComparison.OrdinalIgnoreCase),
                $"Expected ActiveColorHex #EC4899, got {canvas.ActiveColorHex}");
        }

        private static void TestDrawingCanvasBrushEraserAndUndo()
        {
            var canvas = new DrawingCanvasControl();
            canvas.LoadDocument(new DrawingDocument { Background = CanvasBackgroundKind.Grid });

            canvas.AddStroke(new DrawingStroke
            {
                Tool = DrawingToolKind.Pen,
                ColorHex = "#0F172A",
                Thickness = 4.0,
                Opacity = 1.0,
                Points = new List<DrawingPoint> { new(20, 20), new(200, 20) }
            });

            canvas.ActiveTool = DrawingToolKind.Eraser;
            canvas.AddStroke(new DrawingStroke
            {
                Tool = DrawingToolKind.Eraser,
                ColorHex = "#FFFFFF",
                Thickness = 12.0,
                Opacity = 1.0,
                Points = new List<DrawingPoint> { new(100, 10), new(100, 30) }
            });

            Assert(canvas.Document.Strokes.Count == 2,
                $"Expected 2 strokes (Pen + Eraser brush), got {canvas.Document.Strokes.Count}");
            Assert(canvas.Document.Strokes[0].Tool == DrawingToolKind.Pen,
                "Original Pen stroke must remain intact when erasing with brush Eraser.");
            Assert(canvas.Document.Strokes[1].Tool == DrawingToolKind.Eraser &&
                   string.Equals(canvas.Document.Strokes[1].ColorHex, "#FFFFFF", StringComparison.OrdinalIgnoreCase),
                "Second stroke should be recorded as DrawingToolKind.Eraser with #FFFFFF.");

            Assert(canvas.Undo() && canvas.Document.Strokes.Count == 1,
                "Undo should remove only the eraser brush stroke.");
        }

        private static void TestEditorFoldManagerH1ToH5()
        {
            string sample = string.Join("\n",
                "# Top H1",
                "Intro under H1",
                "## Second H2",
                "Body under H2",
                "### Nested H3",
                "Deep body under H3",
                "## Sibling H2b",
                "Sibling body",
                "#### Sub H4",
                "Under H4",
                "##### Deep H5",
                "Under H5");

            var sections = EditorFoldManager.DetectSections(sample);
            Assert(sections.Count == 6, $"Expected 6 H1-H5 sections, got {sections.Count}");
            Assert(sections.Select(s => s.Level).SequenceEqual(new[] { 1, 2, 3, 2, 4, 5 }),
                "Expected detected levels 1, 2, 3, 2, 4, 5");

            var manager = new EditorFoldManager();
            var gutter = manager.GetGutterLines(sample);
            Assert(gutter.Count == 12, $"Expected 12 gutter lines, got {gutter.Count}");
            Assert(gutter[0].IsHeadingH1ToH5 && gutter[0].HeadingLevel == 1, "Line 0 should be H1");
            Assert(!gutter[1].IsHeadingH1ToH5, "Line 1 should not be a heading");
            Assert(gutter[2].IsHeadingH1ToH5 && gutter[2].HeadingLevel == 2, "Line 2 should be H2");
            Assert(gutter[4].IsHeadingH1ToH5 && gutter[4].HeadingLevel == 3, "Line 4 should be H3");
            Assert(gutter[8].IsHeadingH1ToH5 && gutter[8].HeadingLevel == 4, "Line 8 should be H4");
            Assert(gutter[10].IsHeadingH1ToH5 && gutter[10].HeadingLevel == 5, "Line 10 should be H5");

            string folded = manager.FoldSectionAtLine(sample, 2);
            Assert(manager.HasFoldedSections, "Manager should report active folded sections");
            Assert(!folded.Contains("Body under H2"), "Folded H2 text should hide Body under H2");
            Assert(!folded.Contains("### Nested H3"), "Folded H2 text should hide nested H3 heading");
            Assert(!folded.Contains("Deep body under H3"), "Folded H2 text should hide nested H3 body");
            Assert(folded.Contains("## Sibling H2b"), "Folded H2 text should keep sibling ## H2b visible");
            Assert(folded.Contains("Sibling body"), "Folded H2 text should keep sibling body visible");

            string reconstructed = manager.GetFullUnfoldedText(folded);
            Assert(reconstructed == sample, "GetFullUnfoldedText must reconstruct 100% of original Markdown");

            string toggledBack = manager.ToggleFoldAtLine(folded, 2);
            Assert(toggledBack == sample, "ToggleFoldAtLine should restore original Markdown");
            Assert(!manager.HasFoldedSections, "Manager should have no active folds after unfolding");

            string refolded = manager.FoldSectionAtLine(sample, 0);
            Assert(manager.UnfoldAll(refolded) == sample, "UnfoldAll should restore original Markdown");
        }

        private static void TestPreviewHeadingSectionFolding()
        {
            var preview = new MarkdownPreviewControl();
            string doc = string.Join("\n",
                "# Title",
                "",
                "Intro paragraph",
                "",
                "## Section A",
                "",
                "Hidden paragraph",
                "",
                "- Bullet one",
                "",
                "## Section B",
                "",
                "Visible tail");

            preview.RenderMarkdown(doc);
            int expandedCount = preview.RootPanel.Children.Count;
            Assert(expandedCount >= 6, $"Expected >= 6 rendered preview blocks, got {expandedCount}");

            preview.TogglePreviewHeadingFold(4);
            Assert(preview.CollapsedHeadingLines.Contains(4), "Heading line 4 should be tracked as collapsed.");
            int collapsedCount = preview.RootPanel.Children.Count;
            Assert(collapsedCount < expandedCount,
                $"Expected fewer blocks when folded ({collapsedCount} < {expandedCount}).");

            preview.TogglePreviewHeadingFold(4);
            Assert(!preview.CollapsedHeadingLines.Contains(4), "Heading line 4 should be expanded after toggle.");
            Assert(preview.RootPanel.Children.Count == expandedCount,
                $"Expected block count to restore to {expandedCount}, got {preview.RootPanel.Children.Count}.");
        }

        private static void TestLiveCursorSegmentActivationAndNavigation()
        {
            string source = string.Join("\n",
                "# Title",
                "Old line one",
                "Old line two",
                "Tail line");
            string replaced = MarkdownPreviewControl.ReplaceLineRange(
                source, 1, 2, "New line A\nNew line B\nNew line C");
            Assert(replaced == "# Title\nNew line A\nNew line B\nNew line C\nTail line",
                $"ReplaceLineRange produced unexpected text: {replaced}");

            var preview = new MarkdownPreviewControl();
            string doc = string.Join("\n",
                "# Title",
                "",
                "Intro paragraph");
            preview.RenderMarkdown(doc);

            preview.ActivateSegmentAtLine(0);
            Assert(preview.ActiveCursorLineZeroBased == 0,
                $"Expected ActiveCursorLineZeroBased == 0, got {preview.ActiveCursorLineZeroBased}");
            Assert(preview.ActiveEditorTextBox is not null && preview.ActiveEditorTextBox.Text == "# Title",
                $"Expected active H1 editor text '# Title', got '{preview.ActiveEditorTextBox?.Text}'");

            Assert(preview.MoveCursorToAdjacentSegment(+1),
                "MoveCursorToAdjacentSegment(+1) should advance from line 0 to line 1.");
            Assert(preview.ActiveCursorLineZeroBased == 1 && preview.ActiveEditorTextBox is not null,
                $"Expected cursor on line 1, got line {preview.ActiveCursorLineZeroBased}");

            Assert(preview.MoveCursorToAdjacentSegment(+1),
                "MoveCursorToAdjacentSegment(+1) should advance from line 1 to line 2.");
            Assert(preview.ActiveCursorLineZeroBased == 2 && preview.ActiveEditorTextBox?.Text == "Intro paragraph",
                $"Expected line 2 text 'Intro paragraph', got '{preview.ActiveEditorTextBox?.Text}'");
        }

        private static void TestClickImageToOpenInlineDrawingEditor()
        {
            var preview = new MarkdownPreviewControl();
            var drawingDoc = new DrawingDocument { Background = CanvasBackgroundKind.Grid };
            string docWithImage = $"# Note With Sketch\n\n```drawing\n{drawingDoc.ToJson()}\n```";
            const int fenceLine = 2;
            preview.RenderMarkdown(docWithImage);

            Assert(!preview.OpenInlineImageEditors.Contains(fenceLine),
                "Inline drawing block should start collapsed as a clickable image card.");
            Assert(!ContainsControlType<DrawingWorkspaceControl>(preview.RootPanel),
                "Interactive DrawingWorkspaceControl editor should not appear until image is clicked.");

            preview.OpenInlineImageEditor(fenceLine);
            Assert(preview.OpenInlineImageEditors.Contains(fenceLine),
                "OpenInlineImageEditor should mark the inline drawing editor as open.");
            Assert(ContainsControlType<DrawingWorkspaceControl>(preview.RootPanel),
                "Clicking the image should open DrawingWorkspaceControl directly in preview.");

            preview.CloseInlineImageEditor(fenceLine);
            Assert(!preview.OpenInlineImageEditors.Contains(fenceLine),
                "CloseInlineImageEditor should collapse back to image preview.");
            Assert(!ContainsControlType<DrawingWorkspaceControl>(preview.RootPanel),
                "Collapsed image should replace DrawingWorkspaceControl with image card again.");
        }

        private static void TestSyntaxGuideSubMenu()
        {
            Assert(SyntaxGuideSubMenuControl.AllEntries.Count >= 15,
                $"Expected >= 15 syntax guide entries, got {SyntaxGuideSubMenuControl.AllEntries.Count}");
            Assert(SyntaxGuideSubMenuControl.AllEntries.Any(e => e.SyntaxToType.Contains("# Heading 1")),
                "Syntax guide should include Markdown Heading syntax.");
            Assert(SyntaxGuideSubMenuControl.AllEntries.Any(e => e.SyntaxToType.Contains("\\begin{pmatrix}")),
                "Syntax guide should include LaTeX matrix syntax.");
            Assert(SyntaxGuideSubMenuControl.AllEntries.Any(e => e.SyntaxToType.Contains("```drawing")),
                "Syntax guide should include drawing block syntax.");

            var guide = new SyntaxGuideSubMenuControl();
            Assert(guide.VisibleEntryCount == SyntaxGuideSubMenuControl.AllEntries.Count,
                $"Expected initial visible entry count {SyntaxGuideSubMenuControl.AllEntries.Count}, got {guide.VisibleEntryCount}");

            guide.SetSearchFilter("matrix");
            Assert(guide.VisibleEntryCount >= 1 && guide.VisibleEntryCount < SyntaxGuideSubMenuControl.AllEntries.Count,
                $"Expected filtered 'matrix' count >= 1 and < total, got {guide.VisibleEntryCount}");

            guide.SetSearchFilter(string.Empty);
            Assert(guide.VisibleEntryCount == SyntaxGuideSubMenuControl.AllEntries.Count,
                $"Expected clearing filter to restore all {SyntaxGuideSubMenuControl.AllEntries.Count} entries, got {guide.VisibleEntryCount}");
        }

        private static DrawingDocument CreateSampleDrawingDocument() => new()
        {
            Background = CanvasBackgroundKind.Grid,
            Strokes =
            {
                new DrawingStroke
                {
                    Tool = DrawingToolKind.Pen,
                    ColorHex = "#3B82F6",
                    Thickness = 3.0,
                    Opacity = 1.0,
                    Points = new List<DrawingPoint> { new(10, 10), new(80, 50) }
                }
            }
        };

        private static void TestPastedImageStorageAndTightCanvasBitmapBounds()
        {
            var assets = new DocumentAssetService();
            var doc = CreateSampleDrawingDocument();
            string? pastedPath = null;
            try
            {
                using var bitmap = MarkdownPreviewControl.RenderCanvasToBitmap(doc, 320, 180);
                var pasted = assets.SavePastedBitmap(bitmap, "selftest_paste");
                pastedPath = pasted.FullPath;

                Assert(string.Equals(
                    Path.GetDirectoryName(pasted.FullPath),
                    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "image")),
                    StringComparison.OrdinalIgnoreCase),
                    $"Pasted image should be stored in <exe>/image, got {pasted.FullPath}");
                Assert(File.Exists(pasted.FullPath), "Pasted image file should exist on disk.");
                Assert(string.Equals(
                    assets.ResolveImagePath($"image/{pasted.FileName}"),
                    pasted.FullPath,
                    StringComparison.OrdinalIgnoreCase),
                    "ResolveImagePath should resolve relative image/<file> link.");

                var usedBounds = doc.GetUsedBounds(16.0);
                Assert(usedBounds.Width > 50 && usedBounds.Width < 200 && usedBounds.Height > 30 && usedBounds.Height < 150,
                    $"Expected tight used bounds for small stroke, got {usedBounds.Width}x{usedBounds.Height}");
                using var tightBitmap = MarkdownPreviewControl.RenderCanvasToBitmap(doc);
                Assert(tightBitmap.PixelSize.Width == (int)Math.Ceiling(usedBounds.Width) &&
                       tightBitmap.PixelSize.Height == (int)Math.Ceiling(usedBounds.Height),
                    $"Expected tight rendered bitmap size {Math.Ceiling(usedBounds.Width)}x{Math.Ceiling(usedBounds.Height)}, got {tightBitmap.PixelSize.Width}x{tightBitmap.PixelSize.Height}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(pastedPath) && File.Exists(pastedPath))
                    File.Delete(pastedPath);
            }
        }

        private static void TestDocumentCanvasDefaultFolderCreation()
        {
            var assets = new DocumentAssetService();
            var doc = CreateSampleDrawingDocument();
            string? canvasPath = null;
            try
            {
                var createdCanvas = assets.CreateSavedCanvas(doc, "selftest_canvas");
                canvasPath = createdCanvas.FullPath;
                Assert(string.Equals(
                    Path.GetDirectoryName(createdCanvas.FullPath),
                    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "canvas")),
                    StringComparison.OrdinalIgnoreCase),
                    $"Canvas should default to <exe>/canvas, got {createdCanvas.FullPath}");
                Assert(File.Exists(createdCanvas.FullPath), "Created .ndraw canvas file should exist on disk.");
            }
            finally
            {
                if (!string.IsNullOrEmpty(canvasPath) && File.Exists(canvasPath))
                    File.Delete(canvasPath);
            }
        }

        private static void TestDrawingRememberedColorsPersistence()
        {
            string colorMemoryPath = Path.Combine(AppContext.BaseDirectory, "canvas", "remembered_colors.json");
            SaveAndRestoreFile(colorMemoryPath, () =>
            {
                DrawingWorkspaceControl.RememberColor("#14B8A6");
                Assert(string.Equals(DrawingWorkspaceControl.LastSelectedColorHex, "#14B8A6", StringComparison.OrdinalIgnoreCase),
                    $"Expected LastSelectedColorHex #14B8A6, got {DrawingWorkspaceControl.LastSelectedColorHex}");
                Assert(string.Equals(DrawingWorkspaceControl.RememberedColors[0], "#14B8A6", StringComparison.OrdinalIgnoreCase),
                    "Remembered color should move to index 0 for quick selection.");

                var ws = new DrawingWorkspaceControl();
                Assert(string.Equals(ws.Canvas.ActiveColorHex, "#14B8A6", StringComparison.OrdinalIgnoreCase),
                    $"New DrawingWorkspaceControl should restore remembered color #14B8A6, got {ws.Canvas.ActiveColorHex}");
            });
        }

        private static void TestInlineDrawingDoneEditingImmediateRefresh()
        {
            var preview = new MarkdownPreviewControl();
            var emptyDoc = new DrawingDocument { Background = CanvasBackgroundKind.Grid };
            preview.RenderMarkdown($"# Sketch\n\n```drawing\n{emptyDoc.ToJson()}\n```");
            preview.OpenInlineImageEditor(2);

            var openWorkspace = FindControlType<DrawingWorkspaceControl>(preview.RootPanel);
            Assert(openWorkspace is not null, "Open inline image editor should mount DrawingWorkspaceControl.");

            openWorkspace!.Canvas.AddStroke(new DrawingStroke
            {
                Tool = DrawingToolKind.Pen,
                ColorHex = "#0F172A",
                Thickness = 3.0,
                Opacity = 1.0,
                Points = new List<DrawingPoint> { new(20, 20), new(140, 90) }
            });

            preview.CloseInlineImageEditor(2);
            var collapsedPreviewCanvas = FindControlType<DrawingCanvasControl>(preview.RootPanel);
            Assert(collapsedPreviewCanvas is not null, "Closing editor should render collapsed DrawingCanvasControl preview.");
            Assert(collapsedPreviewCanvas!.Document.Strokes.Count == 1,
                $"Expected 1 stroke immediately visible after Done Editing, got {collapsedPreviewCanvas.Document.Strokes.Count}");
        }

        private static void TestDrawingCanvasResizeAndUndo()
        {
            var canvas = new DrawingCanvasControl();
            canvas.LoadDocument(new DrawingDocument { Background = CanvasBackgroundKind.Grid });

            double origW = canvas.Document.Width;
            double origH = canvas.Document.Height;
            canvas.ResizeCanvas(500, 320);
            Assert(Math.Abs(canvas.Document.Width - 500) < 0.5 &&
                   Math.Abs(canvas.Document.Height - 320) < 0.5,
                $"Expected resized canvas 500x320, got {canvas.Document.Width}x{canvas.Document.Height}");
            Assert(canvas.Undo(), "Undo should revert canvas resize.");
            Assert(Math.Abs(canvas.Document.Width - origW) < 0.5 &&
                   Math.Abs(canvas.Document.Height - origH) < 0.5,
                $"Undo should restore canvas dimensions {origW}x{origH}, got {canvas.Document.Width}x{canvas.Document.Height}");
        }

        private static void TestMarkdownImageContextMenuAndKeyboardDeletion()
        {
            var assets = new DocumentAssetService();
            string? pastedPath = null;
            try
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(64, 48), new Vector(96, 96));
                var pasted = assets.SavePastedBitmap(bitmap, "selftest_imgmenu");
                pastedPath = pasted.FullPath;

                var preview = new MarkdownPreviewControl();
                string lastEditedMd = string.Empty;
                preview.DocumentEditedFromPreview += (_, updated) => lastEditedMd = updated;

                string imgMd = $"Above line\n![Test Image](image/{pasted.FileName})\nBelow line";
                preview.RenderMarkdown(imgMd);

                var ctxHost = FindControlWithContextMenu(preview.RootPanel);
                Assert(ctxHost?.ContextMenu is not null, "Rendered image card must expose a right-click ContextMenu.");
                var headers = ctxHost!.ContextMenu!.Items
                    .OfType<MenuItem>()
                    .Select(m => m.Header?.ToString() ?? string.Empty)
                    .ToList();
                Assert(headers.Any(h => h.Contains("Edit Image", StringComparison.Ordinal)) &&
                       headers.Any(h => h.Contains("Copy Image", StringComparison.Ordinal)) &&
                       headers.Any(h => h.Contains("Delete Image", StringComparison.Ordinal)),
                    $"ContextMenu should contain Edit Image, Copy Image, and Delete Image items, got: {string.Join(", ", headers)}");

                preview.DeleteImageBlockAtLineRange(1, 1);
                Assert(!lastEditedMd.Contains("![Test Image]", StringComparison.Ordinal),
                    $"DeleteImageBlockAtLineRange should remove image from markdown, got: {lastEditedMd}");

                preview.RenderMarkdown(imgMd);
                preview.ActivateSegmentAtLine(2, caretOffset: 0);
                var afterBox = preview.ActiveEditorTextBox;
                Assert(afterBox is not null, "Line below image should activate with ActiveEditorTextBox.");
                afterBox!.CaretIndex = 0;
                afterBox.SelectionStart = 0;
                afterBox.SelectionEnd = 0;
                afterBox.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Back,
                    Source = afterBox
                });
                Assert(!lastEditedMd.Contains("![Test Image]", StringComparison.Ordinal),
                    $"Backspace at start of line below image must delete the image, got: {lastEditedMd}");

                preview.RenderMarkdown(imgMd);
                preview.ActivateSegmentAtLine(0, caretAtAbsoluteEnd: true);
                var beforeBox = preview.ActiveEditorTextBox;
                Assert(beforeBox is not null, "Line above image should activate with ActiveEditorTextBox.");
                int endIdx = beforeBox!.Text?.Length ?? 0;
                beforeBox.CaretIndex = endIdx;
                beforeBox.SelectionStart = endIdx;
                beforeBox.SelectionEnd = endIdx;
                beforeBox.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Delete,
                    Source = beforeBox
                });
                Assert(!lastEditedMd.Contains("![Test Image]", StringComparison.Ordinal),
                    $"Delete at end of line above image must delete the image, got: {lastEditedMd}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(pastedPath) && File.Exists(pastedPath))
                    File.Delete(pastedPath);
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

        private static void TestImageImportDedupAndDropAtTargetLine()
        {
            var assets = new DocumentAssetService();
            string? droppedFile = null;
            try
            {
                var bmp = new RenderTargetBitmap(new PixelSize(64, 48), new Vector(96, 96));
                var saved = assets.SavePastedBitmap(bmp, "dragdrop_unit");
                droppedFile = saved.FullPath;

                var reused = assets.ImportImageFile(droppedFile);
                Assert(string.Equals(reused.FullPath, droppedFile, StringComparison.OrdinalIgnoreCase),
                    $"Importing an image already in <exe>/image must not duplicate ({reused.FullPath}).");

                var preview = new MarkdownPreviewControl();
                string threeLines = "# Title\nMiddle paragraph\nBottom line";
                preview.RenderMarkdown(threeLines);
                string insertedAtLine1 = MarkdownPreviewControl.InsertImageMarkdownAtLine(
                    threeLines, reused.MarkdownLink, 1, out int actualInsertedLine);
                Assert(actualInsertedLine == 1, $"Expected image inserted at line 1, got line {actualInsertedLine}.");

                var splitInserted = insertedAtLine1.Split('\n');
                Assert(splitInserted[0] == "# Title" &&
                       splitInserted[1] == reused.MarkdownLink &&
                       splitInserted[2] == "Middle paragraph",
                    $"Dropped image should appear at exact target line 1, got:\n{insertedAtLine1}");

                int resolvedLine = preview.GetDocumentLineAtPoint(new Point(80, 65));
                Assert(resolvedLine >= 0 && resolvedLine <= 3,
                    $"GetDocumentLineAtPoint should resolve a valid document line index, got {resolvedLine}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(droppedFile) && File.Exists(droppedFile))
                    File.Delete(droppedFile);
            }
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

        private static void TestCanvasPenDrawingPreventsScrollAndCommitsOnCaptureLost()
        {
            var workspace = new DrawingWorkspaceControl(compactInlineMode: false, isDarkTheme: true);
            workspace.Measure(new Size(800, 600));
            workspace.Arrange(new Rect(0, 0, 800, 600));
            var canvas = workspace.Canvas;
            var scroller = FindControlType<ScrollViewer>(workspace);
            Assert(scroller is not null, "DrawingWorkspaceControl must host a ScrollViewer.");

            var penPointer = new Avalonia.Input.Pointer(99, PointerType.Pen, isPrimary: true);
            var pressProps = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed, 0f, 0.7f, 0f, 0f);
            var moveProps = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other, 0f, 0.8f, 0f, 0f);

            var pressedArgs = new PointerPressedEventArgs(
                canvas, penPointer, canvas, new Point(80, 90), 1, pressProps, KeyModifiers.None, 1);
            canvas.RaiseEvent(pressedArgs);

            Assert(canvas.IsPointerPressedOnCanvas && workspace.IsPointerPressedInCanvas && canvas.HasActiveStroke,
                "Pen contact on canvas must start an active stroke and mark pointer as pressed in canvas.");
            Assert(pressedArgs.Handled, "Canvas pen press must be marked handled.");

            var scrollDuringPen = new ScrollGestureEventArgs(1, new Vector(0, -40))
            {
                RoutedEvent = InputElement.ScrollGestureEvent,
                Source = canvas
            };
            canvas.RaiseEvent(scrollDuringPen);
            Assert(scrollDuringPen.Handled,
                "ScrollGestureEvent during a canvas pen stroke must be suppressed so the window does not scroll.");

            canvas.RaiseEvent(new PointerEventArgs(
                InputElement.PointerMovedEvent, canvas, penPointer, canvas, new Point(150, 140), 2, moveProps, KeyModifiers.None));
            Assert(canvas.HasActiveStroke, "Pen move must keep updating the active stroke.");

            // Simulate ScrollGestureRecognizer or OS capture interruption before PointerReleased: stroke must commit, not undo!
            canvas.RaiseEvent(new PointerCaptureLostEventArgs(canvas, penPointer)
            {
                RoutedEvent = InputElement.PointerCaptureLostEvent,
                Source = canvas
            });
            Assert(!canvas.IsPointerPressedOnCanvas && !canvas.HasActiveStroke,
                "PointerCaptureLost must finalize active stroke and clear canvas pressed state.");
            Assert(canvas.Document.Strokes.Count == 1 && canvas.Document.Strokes[0].Tool == DrawingToolKind.Pen &&
                   canvas.Document.Strokes[0].Points.Count >= 2,
                $"Expected 1 committed Pen stroke with >=2 points after capture loss, got count={canvas.Document.Strokes.Count}, pts={(canvas.Document.Strokes.Count > 0 ? canvas.Document.Strokes[0].Points.Count : 0)}.");

            // Verify stylus eraser tip (`IsEraser`) + normal PointerReleased also commits cleanly
            var eraserPress = new PointerPointProperties(RawInputModifiers.PenEraser, PointerUpdateKind.LeftButtonPressed, 0f, 0.6f, 0f, 0f);
            var eraserRelease = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased, 0f, 0f, 0f, 0f);
            canvas.RaiseEvent(new PointerPressedEventArgs(
                canvas, penPointer, canvas, new Point(120, 110), 3, eraserPress, KeyModifiers.None, 1));
            canvas.RaiseEvent(new PointerReleasedEventArgs(
                canvas, penPointer, canvas, new Point(140, 130), 4, eraserRelease, KeyModifiers.None, MouseButton.Left));
            Assert(canvas.Document.Strokes.Count == 2 && canvas.Document.Strokes[1].Tool == DrawingToolKind.Eraser,
                "Stylus eraser tip release must commit an Eraser stroke.");

            // Press outside the canvas frame (directly on ScrollViewer chrome) must NOT set IsPointerPressedInCanvas or suppress outside scroll
            scroller!.RaiseEvent(new PointerPressedEventArgs(
                scroller, penPointer, scroller, new Point(780, 580), 5, pressProps, KeyModifiers.None, 1));
            Assert(!workspace.IsPointerPressedInCanvas,
                "Press originating outside the canvas window must not set IsPointerPressedInCanvas.");
            var outsideScroll = new ScrollGestureEventArgs(2, new Vector(0, -40))
            {
                RoutedEvent = InputElement.ScrollGestureEvent,
                Source = scroller
            };
            scroller.RaiseEvent(outsideScroll);
            Assert(!outsideScroll.Handled,
                "ScrollGestureEvent started outside the canvas window must not be blocked by canvas scroll guards.");
        }

        private static void TestInlineDrawingMultiLineJsonSyncPreservesSubsequentSegments()
        {
            var preview = new MarkdownPreviewControl();
            string doc = "# Sketch\n\n```drawing\n{\"strokes\":[]}\n```\n\nTrailing paragraph after drawing";
            string latestMd = doc;
            preview.InlineDrawingUpdated += (_, args) => latestMd = MainWindow.ReplaceFencedBlockBody(latestMd, args.OpenFenceLineZeroBased, args.UpdatedJson);
            preview.DocumentEditedFromPreview += (_, updated) => latestMd = updated;

            preview.RenderMarkdown(doc);
            preview.OpenInlineImageEditor(2);

            var workspace = FindControlType<DrawingWorkspaceControl>(preview.RootPanel);
            Assert(workspace is not null, "Inline drawing editor must mount DrawingWorkspaceControl.");

            // Adding a stroke expands single-line JSON into ~20 lines via WriteIndented=true
            workspace!.Canvas.AddStroke(new DrawingStroke
            {
                Tool = DrawingToolKind.Pen,
                ColorHex = "#3B82F6",
                Thickness = 3.0,
                Opacity = 1.0,
                Points = new List<DrawingPoint> { new(20, 20), new(90, 60), new(150, 40) }
            });

            Assert(latestMd.Contains("\"strokes\"", StringComparison.Ordinal) &&
                   latestMd.Contains("Trailing paragraph after drawing", StringComparison.Ordinal),
                "Multi-line drawing update must preserve subsequent Markdown paragraphs.");

            // Locate and click the Delete button on the expanded inline drawing header:
            // verifies live end-line tracking shifted past all expanded JSON lines without leaving orphaned lines.
            Button? FindButtonByText(Control? root, string text)
            {
                if (root is Button b && (b.Content?.ToString()?.Contains(text, StringComparison.Ordinal) ?? false))
                    return b;
                if (root is Panel p)
                {
                    foreach (var child in p.Children)
                    {
                        var found = FindButtonByText(child, text);
                        if (found is not null) return found;
                    }
                }
                if (root is Decorator d)
                    return FindButtonByText(d.Child, text);
                if (root is ContentControl cc && cc.Content is Control inner)
                    return FindButtonByText(inner, text);
                return null;
            }

            var deleteBtn = FindButtonByText(preview.RootPanel, "Delete Image");
            Assert(deleteBtn is not null, "Expanded inline drawing editor should expose a Delete Image button.");
            deleteBtn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, deleteBtn));

            Assert(!latestMd.Contains("```drawing", StringComparison.Ordinal) &&
                   !latestMd.Contains("\"strokes\"", StringComparison.Ordinal) &&
                   latestMd.Contains("# Sketch", StringComparison.Ordinal) &&
                   latestMd.Contains("Trailing paragraph after drawing", StringComparison.Ordinal),
                $"Deleting expanded drawing block must remove all expanded JSON lines while preserving surrounding Markdown, got:\n{latestMd}");
        }

        private static void TestCanvasZoomWhileDrawingWheelShortcutsAndControls()
        {
            var workspace = new DrawingWorkspaceControl(compactInlineMode: false, isDarkTheme: true);
            workspace.Measure(new Size(800, 600));
            workspace.Arrange(new Rect(0, 0, 800, 600));
            var canvas = workspace.Canvas;

            var mousePtr = new Avalonia.Input.Pointer(77, PointerType.Mouse, isPrimary: true);
            var pressProps = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            var moveProps = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other);
            var releaseProps = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);

            // Start an active freehand stroke at (100, 100)
            canvas.RaiseEvent(new PointerPressedEventArgs(
                canvas, mousePtr, canvas, new Point(100, 100), 1, pressProps, KeyModifiers.None, 1));
            Assert(canvas.HasActiveStroke && Math.Abs(canvas.Zoom - 1.0) < 0.001,
                "Canvas should begin stroke at default 1.0 zoom.");

            // Zoom in mid-stroke via PointerWheelChanged (without needing Ctrl)
            var wheelArgs = new PointerWheelEventArgs(
                canvas, mousePtr, canvas, new Point(150, 120), 2, new PointerPointProperties(), KeyModifiers.None, new Vector(0, 1))
            {
                RoutedEvent = InputElement.PointerWheelChangedEvent,
                Source = canvas
            };
            canvas.RaiseEvent(wheelArgs);
            Assert(wheelArgs.Handled, "Wheel event over canvas must be handled cleanly for zoom (no parent scroll).");
            Assert(canvas.Zoom > 1.05 && canvas.PanOffset != default,
                $"Expected anchored zoom > 1.05 and updated PanOffset mid-stroke, got zoom={canvas.Zoom}, pan={canvas.PanOffset}.");
            double afterWheelZoom = canvas.Zoom;

            // Zoom further via keyboard '+' and '-' while still mid-stroke
            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.OemPlus, Source = canvas });
            Assert(canvas.Zoom > afterWheelZoom && canvas.HasActiveStroke,
                "Pressing '+' must zoom in further without cancelling the active stroke.");
            double afterPlusZoom = canvas.Zoom;

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.OemMinus, Source = canvas });
            Assert(canvas.Zoom < afterPlusZoom && canvas.HasActiveStroke,
                "Pressing '-' must zoom out without cancelling the active stroke.");

            // Adjust zoom via DrawingWorkspaceControl slider/API helper mid-stroke, then continue & finish stroke
            workspace.SetZoomPercent(200);
            Assert(Math.Abs(canvas.Zoom - 2.0) < 0.01 && canvas.HasActiveStroke,
                $"SetZoomPercent(200) must set Zoom=2.0 mid-stroke, got {canvas.Zoom}.");

            canvas.RaiseEvent(new PointerEventArgs(
                InputElement.PointerMovedEvent, canvas, mousePtr, canvas, new Point(220, 190), 3, moveProps, KeyModifiers.None));
            canvas.RaiseEvent(new PointerReleasedEventArgs(
                canvas, mousePtr, canvas, new Point(220, 190), 4, releaseProps, KeyModifiers.None, MouseButton.Left));

            Assert(!canvas.HasActiveStroke && canvas.Document.Strokes.Count == 1 && canvas.Document.Strokes[0].Points.Count >= 2,
                $"Expected active stroke to finish and commit intact after mid-stroke zooming, got count={canvas.Document.Strokes.Count}.");
        }

        private static void TestClickLinePreservesScrollOffsetWithoutMovingView()
        {
            var preview = new MarkdownPreviewControl();
            string longDoc = string.Join("\n\n", Enumerable.Range(0, 45).Select(i => $"Line {i}: sample paragraph content for scrolling verification."));
            preview.RenderMarkdown(longDoc);

            var host = new Window { Width = 600, Height = 220, Content = preview };
            host.Measure(new Size(600, 220));
            host.Arrange(new Rect(0, 0, 600, 220));
            using (var rtb = new RenderTargetBitmap(new PixelSize(600, 220), new Vector(96, 96)))
                rtb.Render(host);
            host.Measure(new Size(600, 220));
            host.Arrange(new Rect(0, 0, 600, 220));

            var scroller = FindControlType<ScrollViewer>(preview);
            preview.ScrollOffset = new Vector(0, 140);
            host.Measure(new Size(600, 220));
            host.Arrange(new Rect(0, 0, 600, 220));
            Assert(preview.ScrollOffset.Y > 100,
                $"Expected initial scroll offset near 140 before clicking line, got {preview.ScrollOffset.Y} (extent={scroller?.Extent}, viewport={scroller?.Viewport}).");
            double beforeY = preview.ScrollOffset.Y;

            // Verify ScrollViewer also blocks RequestBringIntoView when focus shifts during click activation
            Assert(scroller is not null && !scroller.BringIntoViewOnFocusChange,
                "Preview ScrollViewer must disable BringIntoViewOnFocusChange so mouse clicks do not scroll.");

            // Simulate clicking a visible line: view must NOT scroll or push the clicked line to the bottom.
            preview.ActivateSegmentAtLine(18);
            var bringArgs = new RequestBringIntoViewEventArgs
            {
                RoutedEvent = Control.RequestBringIntoViewEvent,
                Source = preview.ActiveEditorTextBox
            };
            preview.ActiveEditorTextBox!.RaiseEvent(bringArgs);
            Assert(bringArgs.Handled,
                "RequestBringIntoViewEvent during line activation must be suppressed so the viewport does not jump.");

            host.Measure(new Size(600, 220));
            host.Arrange(new Rect(0, 0, 600, 220));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert(preview.ActiveCursorLineZeroBased == 18 && preview.ActiveEditorTextBox is not null,
                "Clicking line 18 should activate an editor segment on line 18.");
            Assert(Math.Abs(preview.ScrollOffset.Y - beforeY) < 0.5,
                $"Clicking a line must preserve scroll offset ({beforeY}), got {preview.ScrollOffset.Y}.");
        }

        private static void TestClickNextToPicturePlacesCursorAlongsidePicture()
        {
            var preview = new MarkdownPreviewControl();
            string lastEditedMd = "Above line\n![Sample](image/test.png)\nBelow line";
            preview.DocumentEditedFromPreview += (_, updated) => lastEditedMd = updated;
            preview.RenderMarkdown(lastEditedMd);

            preview.ActivatePictureAdjacentCursor(1, beforePicture: false);
            Assert(preview.ActiveCursorLineZeroBased == 1,
                $"Expected active cursor on picture line 1, got {preview.ActiveCursorLineZeroBased}.");
            Assert(preview.IsActiveCursorAdjacentToPicture && !preview.IsActivePictureCursorBefore,
                "Activating right of picture must set IsActiveCursorAdjacentToPicture=true and IsActivePictureCursorBefore=false.");
            Assert(preview.ActiveEditorTextBox is not null,
                "Picture-adjacent activation must expose a focused classic cursor TextBox next to the picture.");

            var rightBox = preview.ActiveEditorTextBox!;
            rightBox.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Left,
                Source = rightBox
            });
            Assert(preview.IsActiveCursorAdjacentToPicture && preview.IsActivePictureCursorBefore,
                "Pressing Left from right of picture must move cursor to left edge of the picture.");

            var leftBox = preview.ActiveEditorTextBox!;
            leftBox.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Right,
                Source = leftBox
            });
            Assert(preview.IsActiveCursorAdjacentToPicture && !preview.IsActivePictureCursorBefore,
                "Pressing Right from left of picture must move cursor back to the right edge of the picture.");

            // Typing while cursor is next to picture inserts a classic line seamlessly right next to/after the picture
            preview.ActiveEditorTextBox!.Text = "Caption after picture";
            Assert(lastEditedMd.Contains("![Sample](image/test.png)\nCaption after picture", StringComparison.Ordinal),
                $"Typing while cursor is next to picture must insert text cleanly on the line after the picture, got:\n{lastEditedMd}");

            // Clicking next to picture again and pressing Backspace removes the picture cleanly
            preview.ActivatePictureAdjacentCursor(1, beforePicture: false);
            var delCursorBox = preview.ActiveEditorTextBox!;
            delCursorBox.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Back,
                Source = delCursorBox
            });
            Assert(!lastEditedMd.Contains("![Sample]", StringComparison.Ordinal),
                $"Pressing Backspace when cursor is right next to picture must delete the picture, got:\n{lastEditedMd}");
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
