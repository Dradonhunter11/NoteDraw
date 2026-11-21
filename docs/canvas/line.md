# Line Tool (`DrawingToolKind.Line`)

## Overview
Draws clean straight vector segments between a pointer press origin `(x0, y0)` and release endpoint `(x1, y1)` with real-time rubber-band preview while dragging.

## Activation
- **Toolbar / Sub-Menu**: Click **`Line`**.
- **Keyboard Shortcuts**: `L`, `5`, or `NumPad5`.

## Controls & Behavior
- **Click & Drag**: Press at the start point, drag to preview the live segment, and release to commit a `DrawingStroke` with two `DrawingPoint`s.
- **Appearance**: Uses active ink color (`ActiveColorHex`) and stroke width (`ActiveThickness`) with round line caps.
- **Hit Testing**: Can be moved later with the [Move / Pan](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/move-pan.md) tool or sampled with [Pipette](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/pipette.md).

## Implementation Reference
- Enum: `DrawingToolKind.Line` in `Drawing/DrawingModels.cs`
- Rendering & SVG Export: `DrawingCanvasControl.DrawStroke` and `<line>` in `DrawingDocument.ExportToSvg`
