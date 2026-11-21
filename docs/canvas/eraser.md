# Eraser Tool (`DrawingToolKind.Eraser`)

## Overview
Erases drawn content using freehand eraser paths rendered in the active canvas background color while faintly preserving grid/dotted/ruled guides underneath.

## Activation
- **Toolbar / Sub-Menu**: Click **`Eraser`**.
- **Keyboard Shortcuts**: `E`, `3`, or `NumPad3`.
- **Hardware Support**: Drawing tablet stylus eraser tips (`PointerPointProperties.IsEraser`) automatically act as `Eraser` during contact.

## Controls & Behavior
- **Width Scaling**: Uses `Math.Max(6.0, ActiveThickness * 2.0)` for fast area clearing.
- **Background Preservation**: When at least one eraser stroke exists and the canvas background is `Grid`, `Dotted`, or `Ruled`, `DrawingCanvasControl.Render` re-draws the pattern at subtle opacity (`0.45`) so guides remain usable.
- **Hover Preview**: Renders a dual-ring red/slate eraser indicator matching the zoomed eraser diameter.
- **Undo / Redo**: Each erase stroke pushes a full undo snapshot (`Ctrl+Z` restores erased ink cleanly).

## Implementation Reference
- Enum: `DrawingToolKind.Eraser` in `Drawing/DrawingModels.cs`
- Rendering & Input: `DrawingCanvasControl` (`DrawStroke`, `HasEraserStrokes`)
- Used Bounds: Excluded by default in `DrawingDocument.GetUsedBounds` when non-eraser strokes exist so erased margins do not bloat cropped previews
