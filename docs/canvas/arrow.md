# Arrow Tool (`DrawingToolKind.Arrow`)

## Overview
Draws straight directional vector arrows from press origin `(x0, y0)` to release tip `(x1, y1)` with an automatically oriented arrowhead (`±30°` wings scaled by thickness).

## Activation
- **Toolbar / Sub-Menu**: Click **`Arrow`**.
- **Keyboard Shortcuts**: `A`, `6`, or `NumPad6`.

## Controls & Behavior
- **Click & Drag**: Start at the arrow tail and release at the target tip where the arrowhead should point.
- **Wing Geometry**: Arrowhead wing length scales dynamically (`Math.Max(11.0, thickness * 3.4)` in live canvas rendering) so both thin annotations and bold diagram connectors stay legible.
- **Tight Bounds**: `DrawingDocument.GetUsedBounds` accounts for arrowhead expansion so inline document previews and `Fit Bounds` do not clip arrow tips.

## Implementation Reference
- Enum: `DrawingToolKind.Arrow` in `Drawing/DrawingModels.cs`
- Rendering & SVG Export: `DrawingCanvasControl.DrawStroke` and `<line>` + `<polyline>` in `DrawingDocument.ExportToSvg`
