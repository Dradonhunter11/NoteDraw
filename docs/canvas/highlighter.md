# Highlighter Tool (`DrawingToolKind.Highlighter`)

## Overview
Draws wide, translucent strokes (`Opacity = 0.36`) designed to emphasize diagrams, handwritten notes, or formulas without obscuring underlying ink.

## Activation
- **Toolbar / Sub-Menu**: Click **`Highlight`**.
- **Keyboard Shortcuts**: `H`, `2`, or `NumPad2`.

## Controls & Behavior
- **Width Scaling**: Automatically uses `Math.Max(12.0, ActiveThickness * 3.0)` for broad marker coverage.
- **Default Ink**: If `ActiveColorHex` is `"AUTO"`, defaults to highlight yellow (`#FACC15`); otherwise uses the selected custom color with `36%` opacity.
- **Smooth Rendering**: Multi-point paths are smoothed using quadratic Bezier interpolation in `DrawingCanvasControl.DrawStroke`.
- **Hover Preview**: Displays the enlarged translucent marker footprint scaled to the current zoom level.

## Implementation Reference
- Enum: `DrawingToolKind.Highlighter` in `Drawing/DrawingModels.cs`
- Rendering & Input: `DrawingCanvasControl`
- SVG Output: Translucent `<path>` or `<circle>` with `stroke-opacity="0.36"` in `DrawingDocument.ExportToSvg`
