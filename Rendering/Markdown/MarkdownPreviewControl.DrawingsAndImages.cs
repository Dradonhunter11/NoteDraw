using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdInline = Markdig.Syntax.Inlines.Inline;
using NoteDraw.Drawing;
using NoteDraw.Rendering.Latex;
using NoteDraw.Services;

namespace NoteDraw.Rendering.Markdown
{
    public sealed partial class MarkdownPreviewControl
    {
        public static RenderTargetBitmap RenderCanvasToBitmap(DrawingDocument doc, int? width = null, int? height = null)
        {
            var used = doc.GetUsedBounds(padding: 16.0);
            int targetW = Math.Clamp(width ?? (int)Math.Ceiling(used.Width), 64, 2048);
            int targetH = Math.Clamp(height ?? (int)Math.Ceiling(used.Height), 48, 2048);

            var canvas = new DrawingCanvasControl
            {
                IsDarkTheme = false,
                Width = targetW,
                Height = targetH
            };
            canvas.LoadDocument(doc);
            if (!width.HasValue && !height.HasValue)
                canvas.SetPanOffset(new Point(-used.X, -used.Y));
            canvas.ApplyPaletteColors("#FFFFFF", "#E5E5E5");
            canvas.Measure(new Size(targetW, targetH));
            canvas.Arrange(new Rect(0, 0, targetW, targetH));

            var bitmap = new RenderTargetBitmap(new PixelSize(targetW, targetH), new Vector(96, 96));
            bitmap.Render(canvas);
            return bitmap;
        }

        private void UpdateInlineFenceMarkdownInPlace(int openFenceLine, string updatedJson)
        {
            _markdown = MainWindow.ReplaceFencedBlockBody(_markdown, openFenceLine, updatedJson);
            var seg = _lastSegments.FirstOrDefault(s => s.StartLine == openFenceLine);
            if (seg is not null)
            {
                int oldEndLine = seg.EndLine;
                seg.RawText = MainWindow.ReplaceFencedBlockBody(seg.RawText, 0, updatedJson);
                int newLineCount = seg.RawText.Replace("\r\n", "\n").Split('\n').Length;
                int newEndLine = seg.StartLine + Math.Max(1, newLineCount) - 1;
                int delta = newEndLine - oldEndLine;
                seg.EndLine = newEndLine;
                if (delta != 0)
                    ShiftSegmentsAfter(seg, delta);
            }
        }

        private static ContextMenu BuildImageContextMenu(
            Action? onEdit,
            Action? onCopy,
            Action onDelete,
            Action? onOpenInSeparateTab = null)
        {
            var menu = new ContextMenu();

            if (onEdit is not null)
            {
                var editItem = new MenuItem { Header = "✎ Edit Image" };
                editItem.Click += (_, _) => onEdit();
                menu.Items.Add(editItem);
            }

            if (onOpenInSeparateTab is not null)
            {
                var openTabItem = new MenuItem { Header = "🗗 Open in a seperate tab" };
                openTabItem.Click += (_, _) => onOpenInSeparateTab();
                menu.Items.Add(openTabItem);
            }

            var copyItem = new MenuItem
            {
                Header = "📋 Copy Image",
                IsEnabled = onCopy is not null
            };
            if (onCopy is not null)
                copyItem.Click += (_, _) => onCopy();
            menu.Items.Add(copyItem);

            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "🗑 Delete Image" };
            deleteItem.Click += (_, _) => onDelete();
            menu.Items.Add(deleteItem);

            return menu;
        }

        private Control RenderInlineDrawingCard(string json, int openFenceLine, int? closeFenceLine = null)
        {
            var doc = DrawingDocument.FromJson(json);
            bool isEditorOpen = _openInlineImageEditors.Contains(openFenceLine);
            DrawingWorkspaceControl? activeWorkspace = null;
            int resolvedEndLine = closeFenceLine ?? (_lastSegments.FirstOrDefault(s => s.StartLine == openFenceLine)?.EndLine ?? openFenceLine);

            void SyncInlineDrawing(DrawingWorkspaceControl ws)
            {
                _inlineDrawingHistories[openFenceLine] = ws.Canvas.GetHistoryState();
                string updatedJson = ws.Canvas.Document.ToJson();
                UpdateInlineFenceMarkdownInPlace(openFenceLine, updatedJson);
                SaveInlineCanvasToFolder?.Invoke(openFenceLine, ws.Canvas.Document);
                InlineDrawingUpdated?.Invoke(this, new InlineDrawingUpdateEventArgs(openFenceLine, updatedJson));
            }

            return BuildInteractiveDrawingImageBlock(
                doc,
                isEditorOpen,
                $"Inline Drawing Image (Line {openFenceLine + 1})",
                startLineZeroBased: openFenceLine,
                endLineZeroBased: resolvedEndLine,
                onOpenEditor: () => OpenInlineImageEditor(openFenceLine),
                onCloseEditor: () =>
                {
                    if (activeWorkspace is not null)
                        SyncInlineDrawing(activeWorkspace);
                    CloseInlineImageEditor(openFenceLine);
                },
                sourceJumpLine: openFenceLine + 1,
                getLatestDoc: () => activeWorkspace?.Canvas.Document ?? doc,
                wireWorkspace: workspace =>
                {
                    activeWorkspace = workspace;
                    if (_inlineDrawingHistories.TryGetValue(openFenceLine, out var savedHistory))
                        workspace.Canvas.RestoreHistoryState(savedHistory.Undo, savedHistory.Redo);

                    void Activate()
                    {
                        LastActiveInlineCanvas = workspace.Canvas;
                        InlineCanvasActivated?.Invoke(this, workspace.Canvas);
                    }

                    workspace.Canvas.PointerPressed += (_, _) => Activate();
                    workspace.Canvas.GotFocus += (_, _) => Activate();
                    workspace.Canvas.HistoryChanged += (_, _) =>
                    {
                        _inlineDrawingHistories[openFenceLine] = workspace.Canvas.GetHistoryState();
                        Activate();
                    };
                    workspace.DocumentChanged += (_, _) =>
                    {
                        Activate();
                        SyncInlineDrawing(workspace);
                    };
                    Activate();
                },
                getEndLineZeroBased: () => _lastSegments.FirstOrDefault(s => s.StartLine == openFenceLine)?.EndLine ?? resolvedEndLine,
                onOpenInSeparateTab: () =>
                {
                    if (activeWorkspace is not null)
                        SyncInlineDrawing(activeWorkspace);
                    _openInlineImageEditors.Remove(openFenceLine);
                    OpenCanvasInSeparateTabRequested?.Invoke(
                        this,
                        new OpenCanvasInTabEventArgs(null, openFenceLine, (activeWorkspace?.Canvas.Document ?? doc).Clone()));
                });
        }

        private Control RenderEmbeddedNdrawCard(string targetName, int startLine = 0, int? endLine = null)
        {
            var doc = _embeddedDrawingCache.TryGetValue(targetName, out var cached)
                ? cached.Clone()
                : (ResolveEmbeddedDrawing?.Invoke(targetName) ?? new DrawingDocument());
            bool isEditorOpen = _openEmbeddedImageEditors.Contains(targetName);
            DrawingWorkspaceControl? activeWorkspace = null;
            int resolvedEndLine = endLine ?? startLine;

            void SyncEmbeddedDrawing(DrawingWorkspaceControl ws)
            {
                _embeddedDrawingCache[targetName] = ws.Canvas.Document.Clone();
                ExternalDrawingUpdated?.Invoke(this, new ExternalDrawingUpdateEventArgs(targetName, ws.Canvas.Document.ToJson()));
            }

            return BuildInteractiveDrawingImageBlock(
                doc,
                isEditorOpen,
                $"Embedded Drawing Image: {targetName}",
                startLineZeroBased: startLine,
                endLineZeroBased: resolvedEndLine,
                onOpenEditor: () =>
                {
                    PrepareEmbeddedCanvasForOpen(targetName);
                    RenderMarkdown(_markdown);
                },
                onCloseEditor: () =>
                {
                    if (activeWorkspace is not null)
                        SyncEmbeddedDrawing(activeWorkspace);
                    _activeSegmentStartLine = -1;
                    _pendingCaretOffset = -1;
                    _activePictureCursorIsBefore = false;
                    _activeSegmentTextBox = null;
                    if (_openEmbeddedImageEditors.Remove(targetName))
                        RenderMarkdown(_markdown);
                },
                sourceJumpLine: null,
                getLatestDoc: () => activeWorkspace?.Canvas.Document ?? _embeddedDrawingCache.GetValueOrDefault(targetName) ?? doc,
                wireWorkspace: workspace =>
                {
                    activeWorkspace = workspace;
                    void Activate()
                    {
                        LastActiveInlineCanvas = workspace.Canvas;
                        InlineCanvasActivated?.Invoke(this, workspace.Canvas);
                    }

                    workspace.Canvas.PointerPressed += (_, _) => Activate();
                    workspace.Canvas.GotFocus += (_, _) => Activate();
                    workspace.Canvas.HistoryChanged += (_, _) => Activate();
                    workspace.DocumentChanged += (_, _) =>
                    {
                        Activate();
                        SyncEmbeddedDrawing(workspace);
                    };
                    Activate();
                },
                onOpenInSeparateTab: () =>
                {
                    if (activeWorkspace is not null)
                        SyncEmbeddedDrawing(activeWorkspace);
                    _openEmbeddedImageEditors.Remove(targetName);
                    var latest = activeWorkspace?.Canvas.Document ?? _embeddedDrawingCache.GetValueOrDefault(targetName) ?? doc;
                    OpenCanvasInSeparateTabRequested?.Invoke(
                        this,
                        new OpenCanvasInTabEventArgs(targetName, null, latest.Clone()));
                });
        }

        private Control BuildInteractiveDrawingImageBlock(
            DrawingDocument doc,
            bool isEditorOpen,
            string title,
            int startLineZeroBased,
            int endLineZeroBased,
            Action onOpenEditor,
            Action onCloseEditor,
            int? sourceJumpLine,
            Func<DrawingDocument> getLatestDoc,
            Action<DrawingWorkspaceControl> wireWorkspace,
            Func<int>? getEndLineZeroBased = null,
            Action? onOpenInSeparateTab = null)
        {
            Action deleteAction = () => DeleteImageBlockAtLineRange(startLineZeroBased, getEndLineZeroBased?.Invoke() ?? endLineZeroBased);
            Action copyAction = () => CopyImageRequested?.Invoke(this, RenderCanvasToBitmap(getLatestDoc()));

            if (isEditorOpen)
            {
                return BuildOpenDrawingEditorBlock(
                    doc, title, onCloseEditor, sourceJumpLine, copyAction, deleteAction, wireWorkspace, onOpenInSeparateTab);
            }

            return BuildCollapsedDrawingCard(
                doc, title, startLineZeroBased, endLineZeroBased, onOpenEditor, copyAction, deleteAction, getEndLineZeroBased, onOpenInSeparateTab);
        }

        private Control BuildOpenDrawingEditorBlock(
            DrawingDocument doc,
            string title,
            Action onCloseEditor,
            int? sourceJumpLine,
            Action copyAction,
            Action deleteAction,
            Action<DrawingWorkspaceControl> wireWorkspace,
            Action? onOpenInSeparateTab)
        {
            var workspace = new DrawingWorkspaceControl(doc, compactInlineMode: true, isDarkTheme: _isDarkTheme);
            workspace.ApplyThemePalette(_palette, _isDarkTheme);
            wireWorkspace(workspace);

            var headerRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 6,
                Background = Brushes.Transparent,
                ContextMenu = BuildImageContextMenu(null, copyAction, deleteAction, onOpenInSeparateTab),
                Margin = new Thickness(0, 0, 0, 4)
            };
            headerRow.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = AccentBrush(),
                VerticalAlignment = VerticalAlignment.Center
            });

            var doneBtn = new Button
            {
                Content = "✓ Done Editing Image",
                FontSize = 11,
                Padding = new Thickness(8, 3),
                CornerRadius = new CornerRadius(4),
                Background = ThemeConfigService.ParseBrush(_palette.AccentColor, "#0078D4"),
                Foreground = Brushes.White
            };
            doneBtn.Click += (_, _) => onCloseEditor();

            var copyBtn = new Button
            {
                Content = "📋 Copy Image",
                FontSize = 11,
                Padding = new Thickness(7, 3),
                CornerRadius = new CornerRadius(4)
            };
            copyBtn.Click += (_, _) => copyAction();

            var deleteBtn = new Button
            {
                Content = "🗑 Delete Image",
                FontSize = 11,
                Padding = new Thickness(7, 3),
                CornerRadius = new CornerRadius(4)
            };
            deleteBtn.Click += (_, _) => deleteAction();

            var openActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { doneBtn }
            };

            if (onOpenInSeparateTab is not null)
            {
                var openTabBtn = new Button
                {
                    Content = "🗗 Open in a seperate tab",
                    FontSize = 11,
                    Padding = new Thickness(7, 3),
                    CornerRadius = new CornerRadius(4)
                };
                openTabBtn.Click += (_, _) => onOpenInSeparateTab();
                openActions.Children.Add(openTabBtn);
            }

            openActions.Children.Add(copyBtn);
            openActions.Children.Add(deleteBtn);

            if (sourceJumpLine.HasValue)
            {
                int targetLine = sourceJumpLine.Value;
                var jumpBtn = new Button
                {
                    Content = "Source",
                    FontSize = 11,
                    Padding = new Thickness(7, 3),
                    CornerRadius = new CornerRadius(4)
                };
                jumpBtn.Click += (_, _) => LineFocusRequested?.Invoke(this, targetLine);
                openActions.Children.Add(jumpBtn);
            }

            Grid.SetColumn(openActions, 1);
            headerRow.Children.Add(openActions);

            return new StackPanel
            {
                Spacing = 4,
                Children = { headerRow, workspace }
            };
        }

        private Control BuildCollapsedDrawingCard(
            DrawingDocument doc,
            string title,
            int startLineZeroBased,
            int endLineZeroBased,
            Action onOpenEditor,
            Action copyAction,
            Action deleteAction,
            Func<int>? getEndLineZeroBased,
            Action? onOpenInSeparateTab)
        {
            var used = doc.GetUsedBounds(padding: 20.0);
            const double maxPreviewW = 860.0;
            const double maxPreviewH = 420.0;
            double fitScale = Math.Min(1.0, Math.Min(maxPreviewW / Math.Max(1.0, used.Width), maxPreviewH / Math.Max(1.0, used.Height)));
            double previewW = Math.Clamp(Math.Ceiling(used.Width * fitScale), 160.0, maxPreviewW);
            double previewH = Math.Clamp(Math.Ceiling(used.Height * fitScale), 96.0, maxPreviewH);

            var previewCanvas = new DrawingCanvasControl
            {
                IsDarkTheme = false,
                IsHitTestVisible = false,
                Width = previewW,
                Height = previewH
            };
            previewCanvas.LoadDocument(doc);
            if (fitScale < 0.999)
                previewCanvas.SetZoom(fitScale);
            previewCanvas.SetPanOffset(new Point(-used.X * fitScale, -used.Y * fitScale));
            previewCanvas.ApplyPaletteColors("#FFFFFF", "#E5E5E5");
            previewCanvas.AttachedToVisualTree += (_, _) => previewCanvas.InvalidateVisual();

            var overlayHint = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 4),
                Margin = new Thickness(0, 0, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Color.FromArgb(225, 15, 23, 42)),
                Child = new TextBlock
                {
                    Text = "🎨 Click to edit • Right-click for menu",
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White
                }
            };

            var clickableOverlay = new Grid
            {
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = new Cursor(StandardCursorType.Hand),
                Children = { previewCanvas, overlayHint }
            };
            clickableOverlay.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(clickableOverlay).Properties.IsLeftButtonPressed)
                {
                    onOpenEditor();
                    e.Handled = true;
                }
            };

            var imageFrame = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = BorderBrushColor(),
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                ClipToBounds = true,
                Focusable = true,
                ContextMenu = BuildImageContextMenu(onOpenEditor, copyAction, deleteAction, onOpenInSeparateTab),
                Child = clickableOverlay
            };
            imageFrame.KeyDown += (_, ke) =>
            {
                if (ke.Key is Key.Delete or Key.Back)
                {
                    deleteAction();
                    ke.Handled = true;
                }
            };

            var collapsedHeader = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 6,
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 0, 0, 3)
            };
            collapsedHeader.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = MutedBrush(),
                VerticalAlignment = VerticalAlignment.Center
            });

            var copyCollapsedBtn = new Button { Content = "📋 Copy Image", FontSize = 11, Padding = new Thickness(7, 2), CornerRadius = new CornerRadius(4) };
            copyCollapsedBtn.Click += (_, _) => copyAction();

            var editImgBtn = new Button { Content = "Edit Image", FontSize = 11, Padding = new Thickness(7, 2), CornerRadius = new CornerRadius(4) };
            editImgBtn.Click += (_, _) => onOpenEditor();

            var deleteCollapsedBtn = new Button { Content = "🗑 Delete", FontSize = 11, Padding = new Thickness(7, 2), CornerRadius = new CornerRadius(4) };
            deleteCollapsedBtn.Click += (_, _) => deleteAction();

            var collapsedActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children = { copyCollapsedBtn, editImgBtn }
            };
            if (onOpenInSeparateTab is not null)
            {
                var openTabCollapsedBtn = new Button
                {
                    Content = "🗗 Open in a seperate tab",
                    FontSize = 11,
                    Padding = new Thickness(7, 2),
                    CornerRadius = new CornerRadius(4)
                };
                openTabCollapsedBtn.Click += (_, _) => onOpenInSeparateTab();
                collapsedActions.Children.Add(openTabCollapsedBtn);
            }
            collapsedActions.Children.Add(deleteCollapsedBtn);
            Grid.SetColumn(collapsedActions, 1);
            collapsedHeader.Children.Add(collapsedActions);

            return WrapPictureCardWithAdjacentCursor(
                collapsedHeader,
                imageFrame,
                startLineZeroBased,
                getEndLineZeroBased ?? (() => endLineZeroBased),
                deleteAction);
        }

        private Control WrapPictureCardWithAdjacentCursor(
            Control? headerControl,
            Control pictureFrame,
            int startLineZeroBased,
            Func<int> getEndLineZeroBased,
            Action deleteAction)
        {
            bool isCursorHere = _activeSegmentStartLine == startLineZeroBased;

            if (headerControl is not null)
            {
                headerControl.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
                {
                    if (!e.GetCurrentPoint(headerControl).Properties.IsLeftButtonPressed)
                        return;
                    if (IsInteractiveChildControl(e.Source as Visual, headerControl))
                        return;
                    ActivatePictureAdjacentCursor(startLineZeroBased, beforePicture: false);
                    _recoverEditorFocusOnPointerRelease = true;
                    e.Handled = true;
                }, RoutingStrategies.Tunnel);
            }

            var leftCursorZone = new Border
            {
                Width = 16,
                MinWidth = 16,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Stretch,
                Focusable = false,
                Cursor = new Cursor(StandardCursorType.Ibeam)
            };
            if (isCursorHere && _activePictureCursorIsBefore)
            {
                leftCursorZone.Child = CreatePictureAdjacentCursorTextBox(
                    startLineZeroBased, getEndLineZeroBased, beforePicture: true, deleteAction);
            }
            leftCursorZone.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                if (!e.GetCurrentPoint(leftCursorZone).Properties.IsLeftButtonPressed)
                    return;
                if (IsInteractiveChildControl(e.Source as Visual, leftCursorZone))
                    return;
                ActivatePictureAdjacentCursor(startLineZeroBased, beforePicture: true);
                _recoverEditorFocusOnPointerRelease = true;
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
            leftCursorZone.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left &&
                    !IsInteractiveChildControl(e.Source as Visual, leftCursorZone))
                {
                    FocusActiveEditorTextBox();
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel);

            var rightCursorZone = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                MinWidth = 24,
                Background = Brushes.Transparent,
                Padding = new Thickness(4, 0, 0, 0),
                Focusable = false,
                Cursor = new Cursor(StandardCursorType.Ibeam)
            };
            if (isCursorHere && !_activePictureCursorIsBefore)
            {
                rightCursorZone.Child = CreatePictureAdjacentCursorTextBox(
                    startLineZeroBased, getEndLineZeroBased, beforePicture: false, deleteAction);
            }
            rightCursorZone.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                if (!e.GetCurrentPoint(rightCursorZone).Properties.IsLeftButtonPressed)
                    return;
                if (IsInteractiveChildControl(e.Source as Visual, rightCursorZone))
                    return;
                ActivatePictureAdjacentCursor(startLineZeroBased, beforePicture: false);
                _recoverEditorFocusOnPointerRelease = true;
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
            rightCursorZone.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left &&
                    !IsInteractiveChildControl(e.Source as Visual, rightCursorZone))
                {
                    FocusActiveEditorTextBox();
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel);

            var pictureRowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
                Margin = new Thickness(-16, 0, 0, 0),
                Background = Brushes.Transparent
            };
            Grid.SetColumn(leftCursorZone, 0);
            Grid.SetColumn(pictureFrame, 1);
            Grid.SetColumn(rightCursorZone, 2);
            pictureRowGrid.Children.Add(leftCursorZone);
            pictureRowGrid.Children.Add(pictureFrame);
            pictureRowGrid.Children.Add(rightCursorZone);

            if (headerControl is null)
                return pictureRowGrid;

            return new StackPanel
            {
                Spacing = 3,
                Children = { headerControl, pictureRowGrid }
            };
        }

        private TextBox CreatePictureAdjacentCursorTextBox(
            int startLineZeroBased,
            Func<int> getEndLineZeroBased,
            bool beforePicture,
            Action deleteAction)
        {
            var adjBox = new TextBox
            {
                Text = string.Empty,
                ContextMenu = null,
                AcceptsReturn = false,
                IsUndoEnabled = false,
                FontSize = _baseFontSize,
                Foreground = TextBrush(),
                CaretBrush = TextBrush(),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = beforePicture ? new Thickness(2, 2, 2, 2) : new Thickness(4, 2, 2, 2),
                MinWidth = 4,
                MinHeight = Math.Max(22, _baseFontSize * 1.6),
                TextAlignment = beforePicture ? TextAlignment.Right : TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Resources =
                {
                    ["TextControlBackground"] = Brushes.Transparent,
                    ["TextControlBackgroundPointerOver"] = Brushes.Transparent,
                    ["TextControlBackgroundFocused"] = Brushes.Transparent,
                    ["TextControlBorderBrush"] = Brushes.Transparent,
                    ["TextControlBorderBrushPointerOver"] = Brushes.Transparent,
                    ["TextControlBorderBrushFocused"] = Brushes.Transparent,
                    ["TextControlThemeMinWidth"] = 4.0,
                    ["TextControlThemeMinHeight"] = 16.0
                }
            };

            _activeSegmentTextBox = adjBox;
            _pendingActiveEditorCaret = 0;
            _pendingActiveCursorDisplayLine = beforePicture ? startLineZeroBased + 1 : getEndLineZeroBased() + 1;

            bool suppressSelf = false;
            void CommitTypedText()
            {
                if (suppressSelf)
                    return;
                string typed = (adjBox.Text ?? string.Empty).Replace("\r\n", "\n").TrimEnd('\n');
                if (string.IsNullOrEmpty(typed))
                    return;

                suppressSelf = true;
                string lineEnding = _markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var lines = string.IsNullOrEmpty(_markdown)
                    ? new List<string>()
                    : _markdown.Replace("\r\n", "\n").Split('\n').ToList();
                int endLine = getEndLineZeroBased();
                int insertIdx = Math.Clamp(beforePicture ? startLineZeroBased : endLine + 1, 0, lines.Count);
                lines.Insert(insertIdx, typed);

                _activeSegmentStartLine = insertIdx;
                _activePictureCursorIsBefore = false;
                _pendingCaretOffset = typed.Length;
                RenderMarkdown(string.Join(lineEnding, lines));
                DocumentEditedFromPreview?.Invoke(this, _markdown);
            }

            adjBox.TextChanged += (_, _) => CommitTypedText();
            adjBox.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBox.TextProperty)
                    CommitTypedText();
            };

            adjBox.AddHandler(InputElement.KeyDownEvent, (_, ke) =>
            {
                if (HandlePictureAdjacentKeyDown(startLineZeroBased, getEndLineZeroBased, beforePicture, deleteAction, ke, () => suppressSelf = true))
                    ke.Handled = true;
            }, RoutingStrategies.Tunnel);

            return adjBox;
        }

        private bool HandlePictureAdjacentKeyDown(
            int startLineZeroBased,
            Func<int> getEndLineZeroBased,
            bool beforePicture,
            Action deleteAction,
            KeyEventArgs ke,
            Action suppressTextChanged)
        {
            if (PlatformShortcuts.HasPrimaryModifier(ke.KeyModifiers))
            {
                if (ke.Key == Key.V && TryHandleImagePasteCallback is not null)
                {
                    int pasteTargetLine = beforePicture ? Math.Max(0, startLineZeroBased - 1) : getEndLineZeroBased();
                    return TryHandleImagePasteCallback(pasteTargetLine);
                }
                return false;
            }

            if (ke.Key == Key.Back)
                return HandlePictureAdjacentBackspace(startLineZeroBased, beforePicture, deleteAction, suppressTextChanged);

            if (ke.Key == Key.Delete)
            {
                suppressTextChanged();
                deleteAction();
                return true;
            }

            if (ke.Key == Key.Left)
            {
                if (!beforePicture)
                    ActivatePictureAdjacentCursor(startLineZeroBased, beforePicture: true);
                else
                    TryActivateRelativeSegment(startLineZeroBased, -1, caretAtAbsoluteEnd: true);
                return true;
            }

            if (ke.Key == Key.Right)
            {
                if (beforePicture)
                    ActivatePictureAdjacentCursor(startLineZeroBased, beforePicture: false);
                else
                    TryActivateRelativeSegment(startLineZeroBased, +1, caretAtAbsoluteStart: true);
                return true;
            }

            if (ke.Key == Key.Up)
                return TryActivateRelativeSegment(startLineZeroBased, -1, caretAtAbsoluteEnd: true);

            if (ke.Key == Key.Down)
                return TryActivateRelativeSegment(startLineZeroBased, +1, caretAtAbsoluteStart: true);

            if (ke.Key == Key.Enter)
            {
                suppressTextChanged();
                string lineEnding = _markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var lines = string.IsNullOrEmpty(_markdown)
                    ? new List<string>()
                    : _markdown.Replace("\r\n", "\n").Split('\n').ToList();
                int insertIdx = Math.Clamp(beforePicture ? startLineZeroBased : getEndLineZeroBased() + 1, 0, lines.Count);
                lines.Insert(insertIdx, string.Empty);
                _activeSegmentStartLine = insertIdx;
                _activePictureCursorIsBefore = false;
                _pendingCaretOffset = 0;
                RenderMarkdown(string.Join(lineEnding, lines));
                DocumentEditedFromPreview?.Invoke(this, _markdown);
                return true;
            }

            if (ke.Key == Key.Escape)
            {
                DeactivateActiveSegment();
                return true;
            }

            return false;
        }

        private bool HandlePictureAdjacentBackspace(
            int startLineZeroBased, bool beforePicture, Action deleteAction, Action suppressTextChanged)
        {
            suppressTextChanged();
            if (!beforePicture)
            {
                deleteAction();
                return true;
            }

            if (startLineZeroBased > 0)
            {
                string lineEnding = _markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var lines = _markdown.Replace("\r\n", "\n").Split('\n').ToList();
                if (startLineZeroBased - 1 < lines.Count && string.IsNullOrEmpty(lines[startLineZeroBased - 1]))
                {
                    lines.RemoveAt(startLineZeroBased - 1);
                    _activeSegmentStartLine = startLineZeroBased - 1;
                    _activePictureCursorIsBefore = true;
                    _pendingCaretOffset = 0;
                    RenderMarkdown(string.Join(lineEnding, lines));
                    DocumentEditedFromPreview?.Invoke(this, _markdown);
                }
                else
                {
                    TryActivateRelativeSegment(startLineZeroBased, -1, caretAtAbsoluteEnd: true);
                }
                return true;
            }

            deleteAction();
            return true;
        }


        public static string ExtractCleanEmbedTarget(string rawInner, out string? pipeParam, out int? customWidth)
        {
            customWidth = null;
            string cleanTarget = DocumentAssetService.CleanObsidianImageTarget(rawInner, out pipeParam);
            if (!string.IsNullOrWhiteSpace(pipeParam))
            {
                string dimToken = pipeParam.Trim();
                int xIdx = dimToken.IndexOfAny(new[] { 'x', 'X' });
                string widthPart = xIdx > 0 ? dimToken[..xIdx] : dimToken;
                if (int.TryParse(widthPart, out int parsedW) && parsedW > 0)
                    customWidth = Math.Clamp(parsedW, 32, 1600);
            }
            return cleanTarget;
        }

        private static bool TryParseMarkdownImageLine(string trimmed, out string alt, out string url, out int? customWidth)
        {
            alt = string.Empty;
            url = string.Empty;
            customWidth = null;

            var m = MarkdownImageLineRegex.Match(trimmed);
            if (!m.Success)
                return false;

            string rawAlt = m.Groups[1].Value.Trim();
            string rawUrl = m.Groups[2].Value.Trim();

            int pipeIdx = rawAlt.IndexOf('|');
            if (pipeIdx >= 0)
            {
                string right = rawAlt[(pipeIdx + 1)..].Trim();
                rawAlt = rawAlt[..pipeIdx].Trim();
                int xIdx = right.IndexOfAny(new[] { 'x', 'X' });
                string widthPart = xIdx > 0 ? right[..xIdx] : right;
                if (int.TryParse(widthPart, out int parsedW) && parsedW > 0)
                    customWidth = Math.Clamp(parsedW, 32, 1600);
            }

            alt = rawAlt;
            url = DocumentAssetService.CleanObsidianImageTarget(rawUrl, out _);
            return true;
        }

        private static bool IsImageExtension(string path)
        {
            string cleaned = DocumentAssetService.CleanObsidianImageTarget(path, out _);
            try
            {
                string ext = Path.GetExtension(cleaned).ToLowerInvariant();
                return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".svg";
            }
            catch
            {
                return false;
            }
        }

        private string? TryResolveImageFile(string urlOrTarget)
        {
            if (string.IsNullOrWhiteSpace(urlOrTarget))
                return null;
            string cleaned = DocumentAssetService.CleanObsidianImageTarget(urlOrTarget, out _);
            string? resolved = ResolveVaultImagePath?.Invoke(cleaned);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                return resolved;
            try
            {
                return File.Exists(cleaned) ? Path.GetFullPath(cleaned) : null;
            }
            catch
            {
                return null;
            }
        }

        private Control RenderMarkdownImageCard(string urlOrTarget, string altText, int origStart, int origEnd, int? requestedWidth = null)
        {
            string? resolvedFile = TryResolveImageFile(urlOrTarget);
            Action deleteAction = () => DeleteImageBlockAtLineRange(origStart, origEnd);

            Control imageBody;
            Bitmap? loadedBitmap = null;
            double estimatedWidth = 200;
            if (!string.IsNullOrWhiteSpace(resolvedFile) && !resolvedFile.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    loadedBitmap = new Bitmap(resolvedFile);
                    double natW = Math.Max(1, loadedBitmap.PixelSize.Width);
                    double natH = Math.Max(1, loadedBitmap.PixelSize.Height);
                    double maxW = requestedWidth.HasValue ? Math.Clamp(requestedWidth.Value, 32.0, 960.0) : 760.0;
                    double scale = requestedWidth.HasValue ? maxW / natW : 1.0;
                    if (natW * scale > maxW)
                        scale = maxW / natW;
                    if (natH * scale > 520.0)
                        scale = Math.Min(scale, 520.0 / natH);
                    double displayW = Math.Max(32.0, Math.Round(natW * scale));
                    double displayH = Math.Max(24.0, Math.Round(natH * scale));
                    estimatedWidth = displayW;

                    imageBody = new Image
                    {
                        Source = loadedBitmap,
                        Width = displayW,
                        Height = displayH,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top
                    };
                }
                catch
                {
                    imageBody = BuildMissingImageVisual(urlOrTarget, altText);
                }
            }
            else
            {
                imageBody = BuildMissingImageVisual(urlOrTarget, altText);
            }

            Action? copyAction = loadedBitmap is not null
                ? () => CopyImageRequested?.Invoke(this, loadedBitmap)
                : null;

            var imgFrame = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(loadedBitmap is null ? 1 : 0),
                BorderBrush = BorderBrushColor(),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Left,
                ClipToBounds = true,
                Focusable = false,
                Cursor = new Cursor(StandardCursorType.Ibeam),
                ContextMenu = BuildImageContextMenu(onEdit: null, copyAction, deleteAction),
                Child = imageBody
            };
            if (loadedBitmap is null)
                imgFrame.MinHeight = 64;

            imgFrame.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                if (!e.GetCurrentPoint(imgFrame).Properties.IsLeftButtonPressed)
                    return;
                double frameW = imgFrame.Bounds.Width > 0 ? imgFrame.Bounds.Width : estimatedWidth;
                bool beforePicture = e.GetPosition(imgFrame).X < frameW * 0.5;
                ActivatePictureAdjacentCursor(origStart, beforePicture);
                _recoverEditorFocusOnPointerRelease = true;
                e.Handled = true;
            }, RoutingStrategies.Tunnel);

            imgFrame.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left)
                {
                    FocusActiveEditorTextBox();
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel);

            return WrapPictureCardWithAdjacentCursor(
                null,
                imgFrame,
                origStart,
                () => origEnd,
                deleteAction);
        }

        private static Control BuildMissingImageVisual(string urlOrTarget, string altText) => new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock
                {
                    Text = $"🖼 {(string.IsNullOrWhiteSpace(altText) ? "Markdown Image" : altText)}",
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(urlOrTarget) ? "(no path set)" : urlOrTarget,
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(Color.Parse("#475569")),
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        };
    }
}
