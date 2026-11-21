# Rectangle Tool (`DrawingToolKind.Rectangle`)

## Overview
Draws clean axis-aligned vector rectangles with subtly rounded corners (`4px` corner radius) from any drag corner to the opposite corner.

## Activation
- **Toolbar / Sub-Menu**: Click **`Rect`**.
- **Keyboard Shortcuts**: `R`, `7`, or `NumPad7`.

## Controls & Behavior
- **Click & Drag in Any Direction**: Supports dragging left-to-right, right-to-left, top-to-bottom, or bottom-to-top using normalized `Math.Min` / `Math.Abs` bounds.
- **Hit Testing**: `HitTestStroke` checks proximity to all four rectangle edges so shapes can be dragged cleanly with [Move / Pan](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/move-pan.md) or sampled with [Pipette](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/pipette.md).

## Implementation Reference
- Enum: `DrawingToolKind.Rectangle` in `Drawing/DrawingModels.cs`
- Rendering & SVG Export: `DrawingCanvasControl.DrawStroke` (`DrawRectangle`) and `<rect>` in `DrawingDocument.ExportToSvg`
