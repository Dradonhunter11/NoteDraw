# Ellipse Tool (`DrawingToolKind.Ellipse`)

## Overview
Draws crisp vector ovals and circles inscribed within the bounding box defined by dragging from `(x0, y0)` to `(x1, y1)`.

## Activation
- **Toolbar / Sub-Menu**: Click **`Ellipse`**.
- **Keyboard Shortcuts**: `O`, `C`, `8`, or `NumPad8`.

## Controls & Behavior
- **Click & Drag**: Center is calculated at `((x0 + x1) / 2, (y0 + y1) / 2)` with radii `rx = |x1 - x0| / 2` and `ry = |y1 - y0| / 2`.
- **Styling**: Uses the active stroke color (`ActiveColorHex`) and stroke width (`ActiveThickness`).
- **Serialization**: Stored identically to rectangles using two corner `DrawingPoint`s in `.ndraw` JSON and exported as `<ellipse>` in SVG.

## Implementation Reference
- Enum: `DrawingToolKind.Ellipse` in `Drawing/DrawingModels.cs`
- Rendering & SVG Export: `DrawingCanvasControl.DrawStroke` (`DrawEllipse`) and `<ellipse>` in `DrawingDocument.ExportToSvg`
