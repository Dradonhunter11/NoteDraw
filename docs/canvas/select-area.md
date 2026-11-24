# Select Area Tool (`DrawingToolKind.SelectArea`)

## Overview
Defines a rectangular clipping selection on the canvas (`SelectionArea`) so new freehand ink, highlights, erasures, shapes, and text/LaTeX labels are strictly restricted to and clipped inside the selected region.

## Activation
- **Toolbar / Quick Menu**: Click **`Select Area`**.
- **Keyboard Shortcut**: `S`.

## Controls & Behavior
- **Create Selection**: Click and drag diagonally to draw the dashed marquee box (`>= 5 × 5` logical pixels). Once released, the area outside the selection dims subtly to highlight the active drawing mask.
- **Restrict & Clip Drawing**:
  - Strokes, shapes, and text/LaTeX labels cannot start outside the active selection box.
  - Any brush stroke or shape started inside the selection receives a persistent `DrawingStroke.ClipRect` so lines extending toward or past the marquee border are cleanly clipped to the selected area in live rendering, `GetUsedBounds()`, PNG bitmaps, and exported SVGs (`<clipPath>`).
- **Cancel Selection**: Any of the following immediately cancels the selection area so you can draw anywhere again:
  - Dragging or clicking a selection smaller than **`5 × 5`** pixels (`Width < 5` or `Height < 5`).
  - **Double-clicking** on the canvas (`e.ClickCount >= 2`).
  - Pressing **`Escape`** on the keyboard.

## Implementation Reference
- Enum & Model: `DrawingToolKind.SelectArea`, `DrawingRect`, and `DrawingStroke.ClipRect` in `Drawing/DrawingModels.cs`
- Selection State & Clipping: `SelectionArea`, `SetSelectionArea`, `ClearSelectionArea`, `FinalizeAreaSelection`, `DrawStroke`, and `DrawSelectionOverlay` in `Drawing/DrawingCanvasControl.cs`
