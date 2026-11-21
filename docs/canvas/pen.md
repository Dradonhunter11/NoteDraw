# Pen Tool (`DrawingToolKind.Pen`)

## Overview
Draws smooth, anti-aliased freehand ink strokes with pressure capture (`Mouse`, `Pen`/tablet stylus, or `Touch`). Single clicks place a circular ink dot (`r = thickness / 2`); drags emit quadratic Bezier-smoothed curves.

## Activation
- **Toolbar / Sub-Menu**: Click **`Pen`**.
- **Keyboard Shortcuts**: `P`, `1`, or `NumPad1`.

## Controls & Behavior
- **Color**: Uses the active color (`ActiveColorHex`) chosen from the Color Selector, Remembered Quick Chips, or [Pipette](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/pipette.md).
- **Thickness**: Directly uses the toolbar brush size slider (`1px`–`24px`).
- **Tablet & Stylus Support**: Calling `PreventGestureRecognition()` keeps pointer capture locked on the canvas so strokes never trigger unintended parent scrolling, and any stroke is committed on both `PointerReleased` and `PointerCaptureLost`. Clicking with the stylus tail/eraser end automatically switches to the Eraser for that stroke.
- **Hover Preview**: Displays a circular cursor ring scaled by current zoom and colored to match the active ink.

## Implementation Reference
- Enum: `DrawingToolKind.Pen` in `Drawing/DrawingModels.cs`
- Rendering & Input: `DrawingCanvasControl` (`DrawStroke`, `OnPointerPressed`, `OnPointerMoved`)
- SVG Output: `<circle>` (single point) or `<path>` (multi-point stroke) in `DrawingDocument.ExportToSvg`
