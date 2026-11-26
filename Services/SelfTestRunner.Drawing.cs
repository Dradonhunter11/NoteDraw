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

        private static void TestCanvasContextMenuOpenInSeparateTab()
        {
            string? createdSeparateCanvasFile = null;
            WithTempVault("NoteDrawSeparateCanvasTabTest", tempVault =>
                SaveAndRestoreFile(ThemeConfigService.GetOptionsFilePath(), () =>
                {
                    var sampleDoc = CreateSampleDrawingDocument();
                    string notePath = Path.Combine(tempVault, "CanvasHost.md");
                    string noteContent = $"# Diagram Note\n\n```drawing\n{sampleDoc.ToJson()}\n```\n";
                    File.WriteAllText(notePath, noteContent);

                    var win = new MainWindow(tempVault);
                    win.OpenVaultFileInTab(notePath);
                    Assert(win.ActiveDocumentTab is { IsDrawing: false },
                        "Opening markdown note must activate non-drawing tab first.");

                    var ctxHost = FindControlWithContextMenu(win.PreviewControl.RootPanel);
                    Assert(ctxHost?.ContextMenu is not null,
                        "Rendered drawing canvas card must expose a right-click ContextMenu.");

                    var menuHeaders = ctxHost!.ContextMenu!.Items
                        .OfType<MenuItem>()
                        .Select(m => m.Header?.ToString() ?? string.Empty)
                        .ToList();
                    Assert(menuHeaders.Any(h => h.Contains("Open in a seperate tab", StringComparison.Ordinal)),
                        $"Canvas ContextMenu must expose 'Open in a seperate tab', got: {string.Join(", ", menuHeaders)}");

                    createdSeparateCanvasFile = win.OpenCanvasInSeparateTab(null, 2, sampleDoc);
                    Assert(!string.IsNullOrWhiteSpace(createdSeparateCanvasFile) && File.Exists(createdSeparateCanvasFile),
                        $"OpenCanvasInSeparateTab must create/open a canvas file, got '{createdSeparateCanvasFile}'.");
                    Assert(win.ActiveDocumentTab is { IsDrawing: true },
                        "OpenCanvasInSeparateTab must switch MainWindow to a dedicated .ndraw drawing tab.");

                    win.StandaloneDrawingWorkspace.Canvas.AddStroke(new DrawingStroke
                    {
                        Tool = DrawingToolKind.Line,
                        ColorHex = "#EF4444",
                        Thickness = 4.0,
                        Opacity = 1.0,
                        Points = new List<DrawingPoint> { new(25, 25), new(110, 95) }
                    });

                    win.OpenVaultFileInTab(notePath);
                    string updatedNoteMd = win.ActiveDocumentTab!.Content;
                    Assert(updatedNoteMd.Contains("#EF4444", StringComparison.Ordinal),
                        $"Edits made in separate canvas tab must sync back to source Markdown block, got:\n{updatedNoteMd}");
                }));

            if (!string.IsNullOrEmpty(createdSeparateCanvasFile) && File.Exists(createdSeparateCanvasFile))
                File.Delete(createdSeparateCanvasFile);
        }

        private static void TestDrawingPipetteColorSampling()
        {
            string colorMemoryPath = Path.Combine(AppContext.BaseDirectory, "canvas", "remembered_colors.json");
            SaveAndRestoreFile(colorMemoryPath, () =>
            {
                var workspace = new DrawingWorkspaceControl(compactInlineMode: false, isDarkTheme: true);
                var canvas = workspace.Canvas;
                canvas.Width = 400;
                canvas.Height = 300;
                canvas.Measure(new Size(400, 300));
                canvas.Arrange(new Rect(0, 0, 400, 300));

                canvas.AddStroke(new DrawingStroke
                {
                    Tool = DrawingToolKind.Pen,
                    ColorHex = "#EF4444",
                    Thickness = 5.0,
                    Opacity = 1.0,
                    Points = new List<DrawingPoint> { new(20, 20), new(60, 20) }
                });
                canvas.AddStroke(new DrawingStroke
                {
                    Tool = DrawingToolKind.Rectangle,
                    ColorHex = "#10B981",
                    Thickness = 4.0,
                    Opacity = 1.0,
                    Points = new List<DrawingPoint> { new(100, 100), new(180, 180) }
                });

                canvas.ActiveTool = DrawingToolKind.Pipette;
                var mousePtr = new Avalonia.Input.Pointer(1, PointerType.Mouse, isPrimary: true);
                var leftPress = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);

                // Click directly on the first red stroke
                canvas.RaiseEvent(new PointerPressedEventArgs(
                    canvas, mousePtr, canvas, new Point(40, 20), 1, leftPress, KeyModifiers.None, 1));
                Assert(string.Equals(canvas.ActiveColorHex, "#EF4444", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(DrawingWorkspaceControl.LastSelectedColorHex, "#EF4444", StringComparison.OrdinalIgnoreCase),
                    $"Pipette click on red stroke must select #EF4444, got canvas={canvas.ActiveColorHex}, workspace={DrawingWorkspaceControl.LastSelectedColorHex}.");

                // Click on green rectangle border
                canvas.RaiseEvent(new PointerPressedEventArgs(
                    canvas, mousePtr, canvas, new Point(100, 140), 2, leftPress, KeyModifiers.None, 1));
                Assert(string.Equals(canvas.ActiveColorHex, "#10B981", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(DrawingWorkspaceControl.RememberedColors[0], "#10B981", StringComparison.OrdinalIgnoreCase),
                    $"Pipette click on green rectangle must sample #10B981 into workspace memory, got {canvas.ActiveColorHex}.");

                // Click on empty background samples effective canvas background (#FFFFFF) without creating strokes
                canvas.RaiseEvent(new PointerPressedEventArgs(
                    canvas, mousePtr, canvas, new Point(320, 240), 3, leftPress, KeyModifiers.None, 1));
                Assert(string.Equals(canvas.ActiveColorHex, "#FFFFFF", StringComparison.OrdinalIgnoreCase),
                    $"Pipette click on blank canvas must sample #FFFFFF background, got {canvas.ActiveColorHex}.");
                Assert(canvas.Document.Strokes.Count == 2,
                    $"Pipette sampling must not add strokes to canvas, got count={canvas.Document.Strokes.Count}.");
            });
        }

        private static void TestDrawingFreehandAndPipetteKeyboardShortcuts()
        {
            var workspace = new DrawingWorkspaceControl(compactInlineMode: false, isDarkTheme: true);
            var canvas = workspace.Canvas;

            canvas.ActiveTool = DrawingToolKind.SelectPan;
            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.P, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Pen, $"Pressing P must select Pen, got {canvas.ActiveTool}.");

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.H, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Highlighter, $"Pressing H must select Highlighter, got {canvas.ActiveTool}.");

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.E, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Eraser, $"Pressing E must select Eraser, got {canvas.ActiveTool}.");

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.I, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Pipette, $"Pressing I must select Pipette, got {canvas.ActiveTool}.");

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.D1, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Pen, $"Pressing 1 must select Pen, got {canvas.ActiveTool}.");

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.D2, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Highlighter, $"Pressing 2 must select Highlighter, got {canvas.ActiveTool}.");

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.D3, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Eraser, $"Pressing 3 must select Eraser, got {canvas.ActiveTool}.");

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.D4, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Pipette, $"Pressing 4 must select Pipette, got {canvas.ActiveTool}.");

            // Ctrl-modified keys or TextBox input must not hijack drawing tools
            canvas.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.P,
                KeyModifiers = KeyModifiers.Control,
                Source = canvas
            });
            Assert(canvas.ActiveTool == DrawingToolKind.Pipette,
                $"Ctrl+P must not trigger Pen shortcut while Pipette is active, got {canvas.ActiveTool}.");

            var hexBox = FindControlType<TextBox>(workspace);
            Assert(hexBox is not null, "DrawingWorkspaceControl must contain text input fields.");
            hexBox!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.E, Source = hexBox });
            Assert(canvas.ActiveTool == DrawingToolKind.Pipette,
                $"Typing inside TextBox must not trigger Eraser shortcut, got {canvas.ActiveTool}.");
        }

        private static void TestDrawingShapeKeyboardShortcuts()
        {
            var workspace = new DrawingWorkspaceControl(compactInlineMode: false, isDarkTheme: true);
            var canvas = workspace.Canvas;

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.L, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Line, $"Pressing L must select Line, got {canvas.ActiveTool}.");

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Arrow, $"Pressing A must select Arrow, got {canvas.ActiveTool}.");

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.R, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Rectangle, $"Pressing R must select Rectangle, got {canvas.ActiveTool}.");

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.O, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Ellipse, $"Pressing O must select Ellipse, got {canvas.ActiveTool}.");

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.D5, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Line, $"Pressing 5 must select Line, got {canvas.ActiveTool}.");

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.D6, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Arrow, $"Pressing 6 must select Arrow, got {canvas.ActiveTool}.");

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.D7, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Rectangle, $"Pressing 7 must select Rectangle, got {canvas.ActiveTool}.");

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.Ellipse, $"Pressing C must select Ellipse, got {canvas.ActiveTool}.");
        }

        private static void TestCanvasCopyAndPasteImage()
        {
            var previousShared = DrawingWorkspaceControl.SharedInAppClipboardBitmap;
            try
            {
                var sourceWs = new DrawingWorkspaceControl(CreateSampleDrawingDocument(), compactInlineMode: false, isDarkTheme: true);
                sourceWs.Measure(new Size(600, 400));
                sourceWs.Arrange(new Rect(0, 0, 600, 400));

                bool copied = sourceWs.CopyCanvasToClipboardAsync().GetAwaiter().GetResult();
                Assert(copied && DrawingWorkspaceControl.SharedInAppClipboardBitmap is not null,
                    "CopyCanvasToClipboardAsync must render canvas bitmap and populate SharedInAppClipboardBitmap.");

                var targetWs = new DrawingWorkspaceControl(new DrawingDocument { Background = CanvasBackgroundKind.Grid }, compactInlineMode: false, isDarkTheme: true);
                targetWs.Measure(new Size(800, 600));
                targetWs.Arrange(new Rect(0, 0, 800, 600));
                var targetCanvas = targetWs.Canvas;

                bool pasted = targetWs.PasteImageFromClipboardAsync(new Point(40, 30), allowFilePickerFallback: false).GetAwaiter().GetResult();
                Assert(pasted && targetCanvas.Document.Strokes.Count == 1,
                    $"PasteImageFromClipboardAsync must insert 1 Image stroke, got pasted={pasted}, count={targetCanvas.Document.Strokes.Count}.");

                var pastedStroke = targetCanvas.Document.Strokes[0];
                Assert(pastedStroke.Tool == DrawingToolKind.Image &&
                       !string.IsNullOrWhiteSpace(pastedStroke.ImageBase64) &&
                       pastedStroke.Points.Count == 2,
                    "Pasted stroke must be DrawingToolKind.Image with valid Base64 image data and bounding points.");
                Assert(Math.Abs(pastedStroke.Points[0].X - 40.0) < 0.5 &&
                       Math.Abs(pastedStroke.Points[0].Y - 30.0) < 0.5 &&
                       pastedStroke.Points[1].X > pastedStroke.Points[0].X + 20 &&
                       pastedStroke.Points[1].Y > pastedStroke.Points[0].Y + 20,
                    $"Expected positive pasted image bounds anchored at (40, 30), got ({pastedStroke.Points[0].X},{pastedStroke.Points[0].Y})->({pastedStroke.Points[1].X},{pastedStroke.Points[1].Y}).");

                var roundTripped = DrawingDocument.FromJson(targetCanvas.Document.ToJson());
                Assert(roundTripped.Strokes.Count == 1 &&
                       roundTripped.Strokes[0].Tool == DrawingToolKind.Image &&
                       string.Equals(roundTripped.Strokes[0].ImageBase64, pastedStroke.ImageBase64, StringComparison.Ordinal),
                    "DrawingDocument JSON round-trip must preserve pasted Image stroke and ImageBase64.");

                string svg = targetCanvas.Document.ExportToSvg();
                Assert(svg.Contains("<image ", StringComparison.Ordinal) &&
                       svg.Contains("data:image/png;base64,", StringComparison.Ordinal),
                    $"ExportToSvg must embed pasted PNG image as <image href=\"data:image/png;base64,...\">, got:\n{svg}");

                var used = targetCanvas.Document.GetUsedBounds(16.0);
                Assert(used.X <= 40.0 && used.Right >= pastedStroke.Points[1].X && used.Bottom >= pastedStroke.Points[1].Y,
                    "GetUsedBounds must enclose pasted image bounds.");
                using var renderedBmp = targetCanvas.RenderToBitmap();
                Assert(renderedBmp.PixelSize.Width >= 64 && renderedBmp.PixelSize.Height >= 48,
                    "RenderToBitmap must render canvas containing pasted image.");

                // Drag pasted image with Move/Pan tool (SelectPan)
                double origX = pastedStroke.Points[0].X;
                double origY = pastedStroke.Points[0].Y;
                targetCanvas.ActiveTool = DrawingToolKind.SelectPan;
                var mousePtr = new Avalonia.Input.Pointer(77, PointerType.Mouse, isPrimary: true);
                var pressProps = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
                var moveProps = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other);
                var releaseProps = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);

                Point hitInside = new(origX + 15, origY + 15);
                targetCanvas.RaiseEvent(new PointerPressedEventArgs(
                    targetCanvas, mousePtr, targetCanvas, hitInside, 1, pressProps, KeyModifiers.None, 1));
                targetCanvas.RaiseEvent(new PointerEventArgs(
                    InputElement.PointerMovedEvent, targetCanvas, mousePtr, targetCanvas, new Point(hitInside.X + 50, hitInside.Y + 35), 2, moveProps, KeyModifiers.None));
                targetCanvas.RaiseEvent(new PointerReleasedEventArgs(
                    targetCanvas, mousePtr, targetCanvas, new Point(hitInside.X + 50, hitInside.Y + 35), 3, releaseProps, KeyModifiers.None, MouseButton.Left));

                Assert(Math.Abs(targetCanvas.Document.Strokes[0].Points[0].X - (origX + 50)) < 1.0 &&
                       Math.Abs(targetCanvas.Document.Strokes[0].Points[0].Y - (origY + 35)) < 1.0,
                    $"Moving pasted image with SelectPan must shift image by (50, 35), got ({targetCanvas.Document.Strokes[0].Points[0].X},{targetCanvas.Document.Strokes[0].Points[0].Y}).");

                // Undo move, then Undo paste, then Redo paste
                Assert(targetCanvas.Undo(), "First Undo must revert pasted image drag move.");
                Assert(Math.Abs(targetCanvas.Document.Strokes[0].Points[0].X - origX) < 1.0,
                    "Undo after move must restore original image position.");
                Assert(targetCanvas.Undo() && targetCanvas.Document.Strokes.Count == 0,
                    "Second Undo must remove the pasted image.");
                Assert(targetCanvas.Redo() && targetCanvas.Document.Strokes.Count == 1,
                    "Redo must restore the pasted image.");
            }
            finally
            {
                DrawingWorkspaceControl.SharedInAppClipboardBitmap = previousShared;
            }
        }

        private static void TestCanvasMovePositionToolPanning()
        {
            var workspace = new DrawingWorkspaceControl(compactInlineMode: false, isDarkTheme: true);
            workspace.Measure(new Size(800, 600));
            workspace.Arrange(new Rect(0, 0, 800, 600));
            var canvas = workspace.Canvas;

            canvas.AddStroke(new DrawingStroke
            {
                Tool = DrawingToolKind.Pen,
                ColorHex = "#0F172A",
                Thickness = 4.0,
                Opacity = 1.0,
                Points = new List<DrawingPoint> { new(80, 80), new(180, 120) }
            });

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.G, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.MovePosition,
                $"Pressing G must select MovePosition, got {canvas.ActiveTool}.");

            var ptr = new Avalonia.Input.Pointer(201, PointerType.Mouse, isPrimary: true);
            var press = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            var move = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other);
            var release = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);

            // Start drag directly on top of the existing stroke: MovePosition must pan viewport instead of dragging stroke
            canvas.RaiseEvent(new PointerPressedEventArgs(canvas, ptr, canvas, new Point(100, 90), 1, press, KeyModifiers.None, 1));
            canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas, ptr, canvas, new Point(165, 130), 2, move, KeyModifiers.None));
            canvas.RaiseEvent(new PointerReleasedEventArgs(canvas, ptr, canvas, new Point(165, 130), 3, release, KeyModifiers.None, MouseButton.Left));

            Assert(Math.Abs(canvas.PanOffset.X - 65.0) < 0.5 && Math.Abs(canvas.PanOffset.Y - 40.0) < 0.5,
                $"MovePosition drag must shift PanOffset by (65, 40), got ({canvas.PanOffset.X},{canvas.PanOffset.Y}).");
            Assert(Math.Abs(canvas.Document.Strokes[0].Points[0].X - 80.0) < 0.1 &&
                   Math.Abs(canvas.Document.Strokes[0].Points[0].Y - 80.0) < 0.1,
                "MovePosition must NOT move strokes underneath the drag point.");
        }

        private static void TestCanvasSelectAreaRestrictsDrawingAndCancels()
        {
            var workspace = new DrawingWorkspaceControl(compactInlineMode: false, isDarkTheme: true);
            workspace.Measure(new Size(800, 600));
            workspace.Arrange(new Rect(0, 0, 800, 600));
            var canvas = workspace.Canvas;

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.S, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.SelectArea,
                $"Pressing S must select SelectArea, got {canvas.ActiveTool}.");

            var ptr = new Avalonia.Input.Pointer(202, PointerType.Mouse, isPrimary: true);
            var press = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            var move = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other);
            var release = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);

            // Select (50, 40) -> (210, 180)
            canvas.RaiseEvent(new PointerPressedEventArgs(canvas, ptr, canvas, new Point(50, 40), 1, press, KeyModifiers.None, 1));
            canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas, ptr, canvas, new Point(210, 180), 2, move, KeyModifiers.None));
            canvas.RaiseEvent(new PointerReleasedEventArgs(canvas, ptr, canvas, new Point(210, 180), 3, release, KeyModifiers.None, MouseButton.Left));

            Assert(canvas.HasSelectionArea && canvas.SelectionArea is { } sel &&
                   Math.Abs(sel.X - 50) < 0.5 && Math.Abs(sel.Y - 40) < 0.5 &&
                   Math.Abs(sel.Width - 160) < 0.5 && Math.Abs(sel.Height - 140) < 0.5,
                $"SelectArea must set 160x140 selection at (50, 40), got {canvas.SelectionArea}.");

            // Attempt drawing outside selection (at 20, 20) -> must be rejected
            canvas.ActiveTool = DrawingToolKind.Pen;
            canvas.RaiseEvent(new PointerPressedEventArgs(canvas, ptr, canvas, new Point(20, 20), 4, press, KeyModifiers.None, 1));
            canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas, ptr, canvas, new Point(35, 30), 5, move, KeyModifiers.None));
            canvas.RaiseEvent(new PointerReleasedEventArgs(canvas, ptr, canvas, new Point(35, 30), 6, release, KeyModifiers.None, MouseButton.Left));
            Assert(canvas.Document.Strokes.Count == 0,
                $"Drawing started outside active SelectionArea must be blocked, got count={canvas.Document.Strokes.Count}.");

            // Draw inside selection -> allowed and clipped to SelectionArea
            canvas.RaiseEvent(new PointerPressedEventArgs(canvas, ptr, canvas, new Point(80, 70), 7, press, KeyModifiers.None, 1));
            canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas, ptr, canvas, new Point(260, 220), 8, move, KeyModifiers.None));
            canvas.RaiseEvent(new PointerReleasedEventArgs(canvas, ptr, canvas, new Point(260, 220), 9, release, KeyModifiers.None, MouseButton.Left));
            Assert(canvas.Document.Strokes.Count == 1 && canvas.Document.Strokes[0].ClipRect is { } clip &&
                   Math.Abs(clip.X - 50) < 0.5 && Math.Abs(clip.Width - 160) < 0.5,
                "Drawing inside SelectionArea must succeed and store ClipRect matching SelectionArea.");

            // Double-click in SelectArea cancels selection and allows drawing anywhere again
            canvas.ActiveTool = DrawingToolKind.SelectArea;
            canvas.RaiseEvent(new PointerPressedEventArgs(canvas, ptr, canvas, new Point(90, 90), 10, press, KeyModifiers.None, 2));
            Assert(!canvas.HasSelectionArea && canvas.SelectionArea is null,
                "Double-clicking with SelectArea must cancel active selection.");

            canvas.ActiveTool = DrawingToolKind.Pen;
            canvas.RaiseEvent(new PointerPressedEventArgs(canvas, ptr, canvas, new Point(15, 15), 11, press, KeyModifiers.None, 1));
            canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas, ptr, canvas, new Point(35, 25), 12, move, KeyModifiers.None));
            canvas.RaiseEvent(new PointerReleasedEventArgs(canvas, ptr, canvas, new Point(35, 25), 13, release, KeyModifiers.None, MouseButton.Left));
            Assert(canvas.Document.Strokes.Count == 2 && canvas.Document.Strokes[1].ClipRect is null,
                "After canceling SelectionArea, drawing anywhere must succeed without ClipRect.");

            // Drag smaller than 5x5 cancels selection too
            canvas.ActiveTool = DrawingToolKind.SelectArea;
            canvas.SetSelectionArea(new Rect(50, 40, 120, 100));
            Assert(canvas.HasSelectionArea, "Precondition: SelectionArea should be active before <5x5 test.");
            canvas.RaiseEvent(new PointerPressedEventArgs(canvas, ptr, canvas, new Point(60, 60), 14, press, KeyModifiers.None, 1));
            canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas, ptr, canvas, new Point(63, 63), 15, move, KeyModifiers.None));
            canvas.RaiseEvent(new PointerReleasedEventArgs(canvas, ptr, canvas, new Point(63, 63), 16, release, KeyModifiers.None, MouseButton.Left));
            Assert(!canvas.HasSelectionArea && canvas.SelectionArea is null,
                "Selection drag smaller than 5x5 must cancel SelectionArea.");
        }

        private static void TestCanvasMoveSelectionToolMovesAreaAndStrokes()
        {
            var workspace = new DrawingWorkspaceControl(compactInlineMode: false, isDarkTheme: true);
            workspace.Measure(new Size(800, 600));
            workspace.Arrange(new Rect(0, 0, 800, 600));
            var canvas = workspace.Canvas;

            canvas.AddStroke(new DrawingStroke
            {
                Tool = DrawingToolKind.Rectangle,
                ColorHex = "#2563EB",
                Thickness = 3.0,
                Opacity = 1.0,
                ClipRect = new DrawingRect(50, 50, 120, 100),
                Points = new List<DrawingPoint> { new(70, 70), new(140, 120) }
            });
            canvas.AddStroke(new DrawingStroke
            {
                Tool = DrawingToolKind.Line,
                ColorHex = "#EF4444",
                Thickness = 3.0,
                Opacity = 1.0,
                Points = new List<DrawingPoint> { new(300, 300), new(360, 340) }
            });
            canvas.SetSelectionArea(new Rect(50, 50, 120, 100));

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.W, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.MoveSelection,
                $"Pressing W must select MoveSelection, got {canvas.ActiveTool}.");

            var ptr = new Avalonia.Input.Pointer(203, PointerType.Mouse, isPrimary: true);
            var press = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            var move = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other);
            var release = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);

            canvas.RaiseEvent(new PointerPressedEventArgs(canvas, ptr, canvas, new Point(100, 100), 1, press, KeyModifiers.None, 1));
            canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas, ptr, canvas, new Point(160, 145), 2, move, KeyModifiers.None));
            canvas.RaiseEvent(new PointerReleasedEventArgs(canvas, ptr, canvas, new Point(160, 145), 3, release, KeyModifiers.None, MouseButton.Left));

            Assert(canvas.SelectionArea is { } movedSel &&
                   Math.Abs(movedSel.X - 110) < 0.5 && Math.Abs(movedSel.Y - 95) < 0.5,
                $"MoveSelection must shift SelectionArea by (60, 45) to (110, 95), got {canvas.SelectionArea}.");

            var inside = canvas.Document.Strokes[0];
            Assert(Math.Abs(inside.Points[0].X - 130) < 0.5 && Math.Abs(inside.Points[0].Y - 115) < 0.5 &&
                   inside.ClipRect is not null && Math.Abs(inside.ClipRect.X - 110) < 0.5,
                "MoveSelection must shift enclosed stroke points and ClipRect cleanly.");

            var outside = canvas.Document.Strokes[1];
            Assert(Math.Abs(outside.Points[0].X - 300) < 0.5 && Math.Abs(outside.Points[0].Y - 300) < 0.5,
                "MoveSelection must NOT move strokes outside the selection area.");

            Assert(canvas.Undo(), "Undo must revert MoveSelection drag.");
            Assert(Math.Abs(canvas.Document.Strokes[0].Points[0].X - 70) < 0.5 &&
                   canvas.SelectionArea is { } undoneSel && Math.Abs(undoneSel.X - 50) < 0.5,
                "Undo after MoveSelection must restore both enclosed strokes and SelectionArea.");
        }

        private static void TestCanvasResizeSelectionToolResizesAreaAndStrokes()
        {
            var workspace = new DrawingWorkspaceControl(compactInlineMode: false, isDarkTheme: true);
            workspace.Measure(new Size(800, 600));
            workspace.Arrange(new Rect(0, 0, 800, 600));
            var canvas = workspace.Canvas;

            canvas.AddStroke(new DrawingStroke
            {
                Tool = DrawingToolKind.Rectangle,
                ColorHex = "#10B981",
                Thickness = 3.0,
                Opacity = 1.0,
                ClipRect = new DrawingRect(40, 40, 100, 80),
                Points = new List<DrawingPoint> { new(60, 60), new(100, 100) }
            });
            canvas.SetSelectionArea(new Rect(40, 40, 100, 80));

            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Q, Source = canvas });
            Assert(canvas.ActiveTool == DrawingToolKind.ResizeSelection,
                $"Pressing Q must select ResizeSelection, got {canvas.ActiveTool}.");

            var ptr = new Avalonia.Input.Pointer(204, PointerType.Mouse, isPrimary: true);
            var press = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            var move = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other);
            var release = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);

            // Drag BottomRight handle from (140, 120) -> (240, 200) (2x width & 2x height)
            canvas.RaiseEvent(new PointerPressedEventArgs(canvas, ptr, canvas, new Point(140, 120), 1, press, KeyModifiers.None, 1));
            canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas, ptr, canvas, new Point(240, 200), 2, move, KeyModifiers.None));
            canvas.RaiseEvent(new PointerReleasedEventArgs(canvas, ptr, canvas, new Point(240, 200), 3, release, KeyModifiers.None, MouseButton.Left));

            Assert(canvas.SelectionArea is { } resized &&
                   Math.Abs(resized.X - 40) < 0.5 && Math.Abs(resized.Y - 40) < 0.5 &&
                   Math.Abs(resized.Width - 200) < 0.5 && Math.Abs(resized.Height - 160) < 0.5,
                $"ResizeSelection must scale SelectionArea to 200x160 at (40, 40), got {canvas.SelectionArea}.");

            var scaledStroke = canvas.Document.Strokes[0];
            Assert(Math.Abs(scaledStroke.Points[0].X - 80) < 0.5 && Math.Abs(scaledStroke.Points[0].Y - 80) < 0.5 &&
                   Math.Abs(scaledStroke.Points[1].X - 160) < 0.5 && Math.Abs(scaledStroke.Points[1].Y - 160) < 0.5 &&
                   scaledStroke.ClipRect is not null && Math.Abs(scaledStroke.ClipRect.Width - 200) < 0.5,
                $"ResizeSelection must scale enclosed stroke points proportionally to (80,80)-(160,160), got ({scaledStroke.Points[0].X},{scaledStroke.Points[0].Y})-({scaledStroke.Points[1].X},{scaledStroke.Points[1].Y}).");

            // Minimum size clamp >= 5x5 when dragged past opposite edge
            canvas.RaiseEvent(new PointerPressedEventArgs(canvas, ptr, canvas, new Point(240, 200), 4, press, KeyModifiers.None, 1));
            canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas, ptr, canvas, new Point(10, 10), 5, move, KeyModifiers.None));
            canvas.RaiseEvent(new PointerReleasedEventArgs(canvas, ptr, canvas, new Point(10, 10), 6, release, KeyModifiers.None, MouseButton.Left));
            Assert(canvas.SelectionArea is { Width: >= 5.0, Height: >= 5.0 },
                $"ResizeSelection must enforce minimum 5x5 selection size, got {canvas.SelectionArea}.");

            Assert(canvas.Undo(), "Undo must revert ResizeSelection.");
            Assert(canvas.SelectionArea is { } restoredSel && Math.Abs(restoredSel.Width - 200) < 0.5,
                $"Undo after ResizeSelection must restore previous 200x160 selection, got {canvas.SelectionArea}.");
        }

        private static void TestCanvasSideToolbarIconsAndCommandPattern()
        {
            var workspace = new DrawingWorkspaceControl(compactInlineMode: false, isDarkTheme: true);
            workspace.Measure(new Size(900, 620));
            workspace.Arrange(new Rect(0, 0, 900, 620));

            Assert(workspace.SideToolBar.IsVisible,
                "DrawingWorkspaceControl must expose a visible vertical SideToolBar.");
            Assert(workspace.SideToolBar.Width <= 56 && workspace.SideToolBar.Child is ScrollViewer,
                "SideToolBar must be compact vertically and scrollable.");
            bool hasDuplicateUndoRedo = CollectControlTypes<Button>(workspace).Any(b =>
            {
                string text = b.Content?.ToString() ?? string.Empty;
                return text.StartsWith("↶ Undo", StringComparison.Ordinal) ||
                       text.StartsWith("↷ Redo", StringComparison.Ordinal);
            });
            Assert(!hasDuplicateUndoRedo,
                "DrawingWorkspaceControl should rely on the main top toolbar rather than duplicate Undo/Redo buttons.");

            var registryTools = NoteDraw.Drawing.Tools.DrawingToolRegistry.Default.All;
            Assert(registryTools.Count == 14,
                $"DrawingToolRegistry must register all 14 modular canvas tools, got {registryTools.Count}.");

            var distinctTypes = new HashSet<Type>();
            foreach (var tool in registryTools)
            {
                Assert(distinctTypes.Add(tool.GetType()),
                    $"Each tool must be its own class; duplicate type {tool.GetType().Name}.");

                Assert(workspace.ToolCommands.TryGetValue(tool.Kind, out var cmd) && cmd is not null,
                    $"SelectDrawingToolCommand missing for {tool.Kind}.");
                Assert(workspace.ToolButtons.TryGetValue(tool.Kind, out var btns) && btns is not null && btns.Count >= 2,
                    $"Expected SideToolBar and context-menu icon buttons for {tool.Kind}.");

                var sideBtn = btns![0];
                Assert(ReferenceEquals(sideBtn.Command, cmd),
                    $"Side toolbar button for {tool.Kind} must be bound to SelectDrawingToolCommand.");
                Assert(sideBtn.Content is PathIcon icon && icon.Data is not null,
                    $"Side toolbar button for {tool.Kind} must render a vector PathIcon instead of text.");

                Assert(cmd!.CanExecute(null), $"SelectDrawingToolCommand.CanExecute must return true for {tool.Kind}.");
                cmd.Execute(null);
                Assert(workspace.Canvas.ActiveTool == tool.Kind,
                    $"Executing command for {tool.Kind} must switch Canvas.ActiveTool.");
            }

            // Verify CanvasCommandHistory executes, undoes, and redoes ICanvasCommand mutations
            var canvas = workspace.Canvas;
            var initialDoc = canvas.Document.Clone();
            var withStrokeDoc = canvas.Document.Clone();
            withStrokeDoc.Strokes.Add(new DrawingStroke
            {
                Tool = DrawingToolKind.Rectangle,
                ColorHex = "#3B82F6",
                Thickness = 3.0,
                Opacity = 1.0,
                Points = new List<DrawingPoint> { new(20, 20), new(100, 80) }
            });

            var history = new NoteDraw.Drawing.Commands.CanvasCommandHistory();
            var addCmd = new NoteDraw.Drawing.Commands.AddStrokeCommand(DrawingToolKind.Rectangle, initialDoc, withStrokeDoc);
            history.Execute(addCmd, canvas);
            Assert(canvas.Document.Strokes.Count == 1 && history.UndoCount == 1 && history.CanUndo,
                "CanvasCommandHistory.Execute must apply command and push onto undo stack.");
            Assert(history.Undo(canvas) && canvas.Document.Strokes.Count == 0 && history.RedoCount == 1,
                "CanvasCommandHistory.Undo must revert state and push onto redo stack.");
            Assert(history.Redo(canvas) && canvas.Document.Strokes.Count == 1 && history.UndoCount == 1,
                "CanvasCommandHistory.Redo must reapply command and restore stroke.");
        }
    }
}
