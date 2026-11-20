using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NoteDraw.Drawing;
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
                ("ViewLayoutState & Markdown block updaters", TestViewStateAndHelpers),
                ("DrawingCanvasControl: Undo/Redo text labels, background, clear & history", TestDrawingCanvasUndoRedo),
                ("TextUndoManager: multi-character burst & deletion Undo/Redo", TestTextUndoManagerMultiChar),
                ("Checklist & LaTeX Rendering: unclipped checklist rows, $$ matrix, array, operatorname", TestChecklistAndLatexPreviewRendering),
                ("ThemeConfigService: separate dark.json/light.json configs, white canvas & hex fallback", TestThemeConfigService),
                ("DrawingCanvas & Workspace: white canvas, mouse preview, right-click color selector & brush eraser", TestDrawingWhiteCanvasMousePreviewColorSelectorAndBrushEraser),
                ("EditorFoldManager: H1-H5 section detection, fold/unfold preservation & gutter info", TestEditorFoldManagerH1ToH5),
                ("MarkdownPreviewControl: in-preview editing, heading folds & click-image-to-edit", TestInPreviewBlockEditing)
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

        private static void TestViewStateAndHelpers()
        {
            var views = new ViewLayoutState();
            Assert(views.VisibleCount == views.TotalCount && views.TotalCount == 7, "All 7 views start open.");

            views.CloseView(WorkspaceViewKind.VaultExplorer);
            views.CloseView(WorkspaceViewKind.LiveLatexPreview);
            Assert(!views.IsVisible(WorkspaceViewKind.VaultExplorer), "VaultExplorer should close.");
            Assert(!views.IsVisible(WorkspaceViewKind.LiveLatexPreview), "LiveLatexPreview should close.");
            Assert(views.VisibleCount == 5, "VisibleCount should be 5 after closing 2 views.");

            // Simulate restoring from View tab toggle
            views.ToggleView(WorkspaceViewKind.LiveLatexPreview);
            Assert(views.IsVisible(WorkspaceViewKind.LiveLatexPreview), "Toggle should restore closed view.");

            // Close all then ResetAllViews
            foreach (WorkspaceViewKind v in Enum.GetValues<WorkspaceViewKind>())
                views.CloseView(v);
            Assert(views.VisibleCount == 0, "All views should be closable.");

            views.ResetAllViews();
            Assert(views.VisibleCount == 7, "ResetAllViews should restore all 7 views.");

            string sampleMd = "# Title\n```drawing\n{\"strokes\":[]}\n```\n- [ ] Task one";
            string replaced = MainWindow.ReplaceFencedBlockBody(sampleMd, 1, "{\"strokes\":[1]}");
            Assert(replaced.Contains("{\"strokes\":[1]}") && replaced.Contains("# Title"), "ReplaceFencedBlockBody should update JSON cleanly.");

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

        private static void TestDrawingWhiteCanvasMousePreviewColorSelectorAndBrushEraser()
        {
            var workspace = new DrawingWorkspaceControl(compactInlineMode: false, isDarkTheme: true);
            var canvas = workspace.Canvas;
            canvas.LoadDocument(new DrawingDocument { Background = CanvasBackgroundKind.Grid });

            // White canvas default & mouse cursor preview position + headless render check
            canvas.Measure(new Size(640, 400));
            canvas.Arrange(new Rect(0, 0, 640, 400));
            canvas.SetHoverPositionForPreview(new Point(120, 80));
            Assert(canvas.HoverCanvasPosition.HasValue &&
                   Math.Abs(canvas.HoverCanvasPosition.Value.X - 120) < 0.01 &&
                   Math.Abs(canvas.HoverCanvasPosition.Value.Y - 80) < 0.01,
                "HoverCanvasPosition should track SetHoverPositionForPreview(120, 80).");

            using var rtb = new RenderTargetBitmap(new PixelSize(640, 400), new Vector(96, 96));
            rtb.Render(canvas);

            // Right-click sub-menu & custom color selector
            Assert(!workspace.IsContextSubMenuOpen, "Sub-menu should start closed.");
            workspace.ShowContextSubMenuAt(new Point(100, 100));
            Assert(workspace.IsContextSubMenuOpen, "ShowContextSubMenuAt should open the sub-menu.");
            workspace.SelectCustomColor("#EC4899");
            Assert(string.Equals(canvas.ActiveColorHex, "#EC4899", StringComparison.OrdinalIgnoreCase),
                $"Expected ActiveColorHex #EC4899, got {canvas.ActiveColorHex}");

            // Freehand brush Eraser: should append an eraser stroke rather than deleting the entire Pen stroke
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

        private static void TestInPreviewBlockEditing()
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
            Assert(preview.CollapsedHeadingLines.Contains(4), "Heading line 4 should be tracked as collapsed");
            int collapsedCount = preview.RootPanel.Children.Count;
            Assert(collapsedCount < expandedCount,
                $"Expected fewer blocks when folded ({collapsedCount} < {expandedCount})");

            preview.TogglePreviewHeadingFold(4);
            Assert(!preview.CollapsedHeadingLines.Contains(4), "Heading line 4 should be expanded after toggle");
            Assert(preview.RootPanel.Children.Count == expandedCount,
                $"Expected block count to restore to {expandedCount}, got {preview.RootPanel.Children.Count}");

            // Click-image-to-open-image-editor verification
            var drawingDoc = new DrawingDocument { Background = CanvasBackgroundKind.Grid };
            string docWithImage = $"# Note With Sketch\n\n```drawing\n{drawingDoc.ToJson()}\n```";
            int fenceLine = 2;
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

        private static bool ContainsControlType<T>(Control? root) where T : Control
        {
            if (root is null) return false;
            if (root is T) return true;
            if (root is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (ContainsControlType<T>(child))
                        return true;
                }
            }
            if (root is Decorator decorator && ContainsControlType<T>(decorator.Child))
                return true;
            if (root is ContentControl contentControl && contentControl.Content is Control inner && ContainsControlType<T>(inner))
                return true;
            return false;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
