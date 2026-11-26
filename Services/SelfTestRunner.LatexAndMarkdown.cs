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
    public static partial class SelfTestRunner
    {
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
                Assert(!headers.Any(h => h.Contains("Edit Image", StringComparison.Ordinal)) &&
                       headers.Any(h => h.Contains("Copy Image", StringComparison.Ordinal)) &&
                       headers.Any(h => h.Contains("Delete Image", StringComparison.Ordinal)),
                    $"Regular image ContextMenu must expose Copy Image and Delete Image without Edit Image, got: {string.Join(", ", headers)}");
                Assert(FindControlType<Button>(preview.RootPanel) is null,
                    "Regular pasted images must render cleanly like Word/Obsidian without edit/header buttons.");

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
            Assert(scroller is not null && !scroller.BringIntoViewOnFocusChange,
                "Preview ScrollViewer must disable BringIntoViewOnFocusChange so mouse clicks do not scroll.");

            // Case 1: At top (ScrollOffset.Y == 0), pointer clicking a rendered segment must keep ScrollOffset.Y == 0 even after ContextIdle jobs.
            var firstSegBorder = preview.RootPanel.Children.OfType<Border>().FirstOrDefault();
            Assert(firstSegBorder is not null, "Expected rendered segment border in preview RootPanel.");
            var mousePtr = new Avalonia.Input.Pointer(1, PointerType.Mouse, isPrimary: true);
            var pressProps = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            var releaseProps = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
            firstSegBorder!.RaiseEvent(new PointerPressedEventArgs(
                firstSegBorder, mousePtr, host, new Point(40, 8), 1, pressProps, KeyModifiers.None, 1));
            firstSegBorder.RaiseEvent(new PointerReleasedEventArgs(
                firstSegBorder, mousePtr, host, new Point(40, 8), 2, releaseProps, KeyModifiers.None, MouseButton.Left));

            host.Measure(new Size(600, 220));
            host.Arrange(new Rect(0, 0, 600, 220));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(Avalonia.Threading.DispatcherPriority.ContextIdle);
            Assert(Math.Abs(preview.ScrollOffset.Y) < 0.5,
                $"Clicking top line at ScrollOffset.Y=0 must stay at 0 after ContextIdle, got {preview.ScrollOffset.Y}.");

            // Case 2: Scrolled down to ~140, activating line 18 must NOT scroll or push the clicked line to the bottom even after Default/Input/ContextIdle jobs.
            preview.ScrollOffset = new Vector(0, 140);
            host.Measure(new Size(600, 220));
            host.Arrange(new Rect(0, 0, 600, 220));
            Assert(preview.ScrollOffset.Y > 100,
                $"Expected initial scroll offset near 140 before clicking line, got {preview.ScrollOffset.Y} (extent={scroller?.Extent}, viewport={scroller?.Viewport}).");
            double beforeY = preview.ScrollOffset.Y;

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
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(Avalonia.Threading.DispatcherPriority.ContextIdle);

            // Even a delayed TextPresenter.CaretChanged BringIntoView at ContextIdle must remain handled.
            var lateBringArgs = new RequestBringIntoViewEventArgs
            {
                RoutedEvent = Control.RequestBringIntoViewEvent,
                Source = preview.ActiveEditorTextBox
            };
            preview.ActiveEditorTextBox!.RaiseEvent(lateBringArgs);
            Assert(lateBringArgs.Handled,
                "RequestBringIntoViewEvent must remain suppressed after ContextIdle so late CaretChanged calls cannot jump the view.");

            Assert(preview.ActiveCursorLineZeroBased == 18 && preview.ActiveEditorTextBox is not null,
                "Clicking line 18 should activate an editor segment on line 18.");
            Assert(Math.Abs(preview.ScrollOffset.Y - beforeY) < 0.5,
                $"Clicking a line must preserve scroll offset ({beforeY}) across ContextIdle, got {preview.ScrollOffset.Y}.");
        }

        private static void TestClickNextToPicturePlacesCursorAlongsidePicture()
        {
            var assets = new DocumentAssetService();
            string? imgFile = null;
            try
            {
                using var bmp = new RenderTargetBitmap(new PixelSize(120, 80), new Vector(96, 96));
                var saved = assets.SavePastedBitmap(bmp, "selftest_adjcursor");
                imgFile = saved.FullPath;

                var preview = new MarkdownPreviewControl();
                string lastEditedMd = $"Above line\n![Sample](image/{saved.FileName})\nBelow line";
                preview.DocumentEditedFromPreview += (_, updated) => lastEditedMd = updated;
                preview.RenderMarkdown(lastEditedMd);

                var host = new Window { Width = 640, Height = 320, Content = preview };
                host.Measure(new Size(640, 320));
                host.Arrange(new Rect(0, 0, 640, 320));
                using (var rtb = new RenderTargetBitmap(new PixelSize(640, 320), new Vector(96, 96)))
                    rtb.Render(host);
                host.Measure(new Size(640, 320));
                host.Arrange(new Rect(0, 0, 640, 320));

                // Left-clicking right half of image or right zone places cursor AFTER picture (no image edit dialog opens).
                var imgFrame = FindControlWithContextMenu(preview.RootPanel);
                Assert(imgFrame is not null, "Expected rendered image frame in preview.");
                var mousePtr = new Avalonia.Input.Pointer(1, PointerType.Mouse, isPrimary: true);
                var pressProps = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
                var releaseProps = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);

                imgFrame!.RaiseEvent(new PointerPressedEventArgs(
                    imgFrame, mousePtr, host, new Point(Math.Max(80, imgFrame.Bounds.Width * 0.8), 20), 1, pressProps, KeyModifiers.None, 1));
                imgFrame.RaiseEvent(new PointerReleasedEventArgs(
                    imgFrame, mousePtr, host, new Point(Math.Max(80, imgFrame.Bounds.Width * 0.8), 20), 2, releaseProps, KeyModifiers.None, MouseButton.Left));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs(Avalonia.Threading.DispatcherPriority.ContextIdle);

                Assert(preview.ActiveCursorLineZeroBased == 1,
                    $"Expected active cursor on picture line 1 after right-half click, got {preview.ActiveCursorLineZeroBased}.");
                Assert(preview.IsActiveCursorAdjacentToPicture && !preview.IsActivePictureCursorBefore,
                    "Clicking right half / after picture must set IsActiveCursorAdjacentToPicture=true and IsActivePictureCursorBefore=false.");
                Assert(preview.ActiveEditorTextBox is not null,
                    "Picture-adjacent click must keep classic cursor TextBox active after PointerReleased.");

                // Left-clicking left half of image places cursor BEFORE picture.
                var imgFrameLeft = FindControlWithContextMenu(preview.RootPanel)!;
                imgFrameLeft.RaiseEvent(new PointerPressedEventArgs(
                    imgFrameLeft, mousePtr, host, new Point(4, 20), 3, pressProps, KeyModifiers.None, 1));
                imgFrameLeft.RaiseEvent(new PointerReleasedEventArgs(
                    imgFrameLeft, mousePtr, host, new Point(4, 20), 4, releaseProps, KeyModifiers.None, MouseButton.Left));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs(Avalonia.Threading.DispatcherPriority.ContextIdle);

                Assert(preview.IsActiveCursorAdjacentToPicture && preview.IsActivePictureCursorBefore,
                    "Clicking left half / before picture must place cursor before the picture.");

                var leftBox = preview.ActiveEditorTextBox!;
                leftBox.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Right,
                    Source = leftBox
                });
                Assert(preview.IsActiveCursorAdjacentToPicture && !preview.IsActivePictureCursorBefore,
                    "Pressing Right from left of picture must move cursor to the right edge of the picture.");

                var rightBox = preview.ActiveEditorTextBox!;
                rightBox.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Left,
                    Source = rightBox
                });
                Assert(preview.IsActiveCursorAdjacentToPicture && preview.IsActivePictureCursorBefore,
                    "Pressing Left from right of picture must move cursor back to left edge of the picture.");

                preview.ActivatePictureAdjacentCursor(1, beforePicture: false);
                preview.ActiveEditorTextBox!.Text = "Caption after picture";
                Assert(lastEditedMd.Contains($"![Sample](image/{saved.FileName})\nCaption after picture", StringComparison.Ordinal),
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
            finally
            {
                if (!string.IsNullOrEmpty(imgFile) && File.Exists(imgFile))
                    File.Delete(imgFile);
            }
        }

        private static void TestCodeSyntaxHighlighterTokenization()
        {
            Assert(string.Equals(CodeSyntaxHighlighter.GetDisplayName("cs"), "C#", StringComparison.Ordinal) &&
                   string.Equals(CodeSyntaxHighlighter.GetDisplayName("py"), "Python", StringComparison.Ordinal) &&
                   string.Equals(CodeSyntaxHighlighter.GetDisplayName("axaml"), "XML", StringComparison.Ordinal) &&
                   string.Equals(CodeSyntaxHighlighter.GetDisplayName(""), "Plain Text", StringComparison.Ordinal),
                "CodeSyntaxHighlighter must resolve canonical language display names and aliases.");

            var samples = new (string Lang, string Code, CodeTokenKind[] RequiredKinds)[]
            {
                (
                    "csharp",
                    "// greet user\npublic async Task<string> GreetAsync(int count) => $\"Hi {count}\";",
                    new[] { CodeTokenKind.Comment, CodeTokenKind.Keyword, CodeTokenKind.TypeName, CodeTokenKind.String, CodeTokenKind.Function }
                ),
                (
                    "python",
                    "# factorial helper\n@staticmethod\ndef fact(n=5):\n    return True if n == 0 else n",
                    new[] { CodeTokenKind.Comment, CodeTokenKind.Attribute, CodeTokenKind.Keyword, CodeTokenKind.Number, CodeTokenKind.Constant }
                ),
                (
                    "json",
                    "{\n  \"port\": 8080,\n  \"enabled\": true,\n  \"name\": \"NoteDraw\"\n}",
                    new[] { CodeTokenKind.Property, CodeTokenKind.Number, CodeTokenKind.Constant, CodeTokenKind.String }
                ),
                (
                    "html",
                    "<!-- banner -->\n<div class=\"card\">Hello</div>",
                    new[] { CodeTokenKind.Comment, CodeTokenKind.Tag, CodeTokenKind.Property, CodeTokenKind.String }
                ),
                (
                    "sql",
                    "-- fetch notes\nSELECT id, title FROM notes WHERE archived = FALSE AND id > 10;",
                    new[] { CodeTokenKind.Comment, CodeTokenKind.Keyword, CodeTokenKind.Constant, CodeTokenKind.Number }
                )
            };

            foreach (var (lang, code, required) in samples)
            {
                var tokens = CodeSyntaxHighlighter.Tokenize(code, lang);
                string reconstructed = string.Concat(tokens.Select(t => t.Text));
                Assert(string.Equals(reconstructed, code, StringComparison.Ordinal),
                    $"Tokenize({lang}) must losslessly preserve original code text.");

                foreach (var kind in required)
                {
                    Assert(tokens.Any(t => t.Kind == kind),
                        $"Tokenize({lang}) should emit token kind {kind}.");
                }
            }

            var darkKw = CodeSyntaxHighlighter.GetTokenBrush(CodeTokenKind.Keyword, isDarkTheme: true);
            var lightKw = CodeSyntaxHighlighter.GetTokenBrush(CodeTokenKind.Keyword, isDarkTheme: false);
            Assert(darkKw is ISolidColorBrush db && lightKw is ISolidColorBrush lb && db.Color != lb.Color,
                "Theme token brushes must adapt between Dark and Light palettes.");
        }

        private static void TestMarkdownFencedCodeBlockSyntaxHighlightingRender()
        {
            var preview = new MarkdownPreviewControl();
            string md = """
                # Code Demo

                ```csharp
                // return answer
                public int Answer() => 42;
                ```

                ~~~python
                def hello():
                    return "world"
                ~~~
                """.Replace("\r\n", "\n");

            preview.ApplyThemePalette(ThemePalette.CreateClassicDark(), isDark: true);
            preview.RenderMarkdown(md);

            var badges = CollectControlTypes<TextBlock>(preview.RootPanel)
                .Select(t => t.Text)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();
            Assert(badges.Contains("C#") && badges.Contains("Python"),
                $"Rendered fenced code cards must show C# and Python badges (including ~~~ fences), got: {string.Join(", ", badges)}");

            var codeBlocks = CollectControlTypes<SelectableTextBlock>(preview.RootPanel)
                .Where(tb => tb.Inlines is { Count: > 1 } &&
                             tb.Inlines.OfType<Avalonia.Controls.Documents.Run>().Any(r => (r.Text ?? string.Empty).Contains("public", StringComparison.Ordinal)))
                .ToList();
            Assert(codeBlocks.Count == 1,
                $"Expected 1 highlighted C# SelectableTextBlock with syntax Runs, got {codeBlocks.Count}.");

            var distinctColors = codeBlocks[0].Inlines!
                .OfType<Avalonia.Controls.Documents.Run>()
                .Select(r => (r.Foreground as ISolidColorBrush)?.Color)
                .Where(c => c.HasValue)
                .Select(c => c!.Value)
                .Distinct()
                .ToList();
            Assert(distinctColors.Count >= 3,
                $"C# code block must render multiple syntax-highlighted token colors, got {distinctColors.Count}.");

            // Verify gutter line numbers rendered for multi-line code blocks
            Assert(badges.Any(b => b == "1\n2"),
                "Multi-line fenced code blocks must render aligned line numbers in the gutter.");
        }
    }
}
