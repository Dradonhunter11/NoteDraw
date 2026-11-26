# Extending Drawing Tools & Closable Workspace Views

> Generated maintenance documentation per `AGENT.md` Rule 5.

This guide documents how to add new hand-drawing tools, canvas backgrounds, export formats, and closable workspace views.

---

## 1. Adding a New Vector Drawing Tool
1. **Enum**: Add the new tool enum value to `DrawingToolKind` in [DrawingModels.cs](file:///c:/Users/alexa/Documents/Github/NoteDraw/Drawing/DrawingModels.cs).
2. **Tool Class (`Drawing/Tools/`)**: Create a class implementing `IDrawingTool` (or extending `DrawingToolBase` / `FreehandToolBase` / `ShapeToolBase`) defining `Kind`, `DisplayName`, `Tooltip`, 24×24 vector `IconPathData`, `Group`, `ShortcutKeys`, and pointer handlers (`OnPointerPressed`, `OnPointerMoved`, `OnPointerReleased`, `OnPointerCaptureLost`, `Cancel`).
3. **Registry**: Register the tool in [DrawingToolRegistry.cs](file:///c:/Users/alexa/Documents/Github/NoteDraw/Drawing/Tools/DrawingToolRegistry.cs) (`CreateOrderedToolList`). Both the vertical **Side Tool Bar** (`SideToolBar`) and the right-click context sub-menu automatically create `PathIcon` buttons bound to `SelectDrawingToolCommand` and register keyboard shortcuts from the registry.
4. **Commands & Rendering**: Record an undoable `ICanvasCommand` (`Drawing/Commands/CanvasCommands.cs`) via `ctx.RecordUndoCommand(...)` / `ctx.CommitActiveStroke()`, render the primitive in `DrawingCanvasControl.DrawStrokeCore(...)`, update `HitTestStroke(...)`, and add SVG export support inside `DrawingDocument.ExportToSvg(...)`.
5. **User Documentation (`docs/canvas/` & `docs/shortcut.md`)**: Add `<tool>.md` and link it in [docs/canvas/README.md](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/README.md) and [docs/shortcut.md](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/shortcut.md).

---

## 2. Adding a Canvas Background Style or Color Swatch
- **Backgrounds**: Add a value to `CanvasBackgroundKind` in `DrawingModels.cs`, add rendering in `DrawingCanvasControl.DrawBackgroundPattern(...)`, and include it in `_backgroundCombo.ItemsSource` inside `DrawingWorkspaceControl.cs`.
- **Palette Swatches**: Add `(Hex, Tooltip)` entries to the `palette` array inside `DrawingWorkspaceControl.cs`.

---

## 3. Adding a New Closable View & `View` Tab Toggle
Every panel in NoteDraw can be closed with its `×` button and restored from the **`View`** tab in the top toolbar:
1. **Register the View ID**: Add a member to `WorkspaceViewKind` and a friendly label in `ViewLayoutState.GetDisplayName(...)` inside [ViewLayoutState.cs](file:///c:/Users/alexa/Documents/Github/NoteDraw/Services/ViewLayoutState.cs).
2. **Create the Panel with a Close Button**: In [MainWindow.axaml.cs](file:///c:/Users/alexa/Documents/Github/NoteDraw/MainWindow.axaml.cs), wrap the panel's header with `CreateViewHeader("TITLE", WorkspaceViewKind.<YourView>)` (or place `CreateCloseViewButton(WorkspaceViewKind.<YourView>, ...)` on custom bars).
3. **Apply Layout Visibility**: In `MainWindow.RefreshViewLayout()`, toggle `.IsVisible` and adjust the parent `Grid`'s `ColumnDefinitions` / `RowDefinitions` when `_viewState.IsVisible(WorkspaceViewKind.<YourView>)` changes.
4. **Automatic `View` Tab Integration**: `BuildViewTabRibbon()` and `UpdateViewToggleLabels()` iterate over `Enum.GetValues<WorkspaceViewKind>()`, so the new view automatically appears in the **`View`** toolbar tab and participates in **`Reset All Views`**.

---

## 4. Verification
After any extension, add a corresponding check in [SelfTestRunner.cs](file:///c:/Users/alexa/Documents/Github/NoteDraw/Services/SelfTestRunner.cs) and run:
```bash
dotnet run --project NoteDraw.csproj -- --self-test
```
