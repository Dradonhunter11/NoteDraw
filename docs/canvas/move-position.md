# Move Position Tool (`DrawingToolKind.MovePosition`)

## Overview
Pans the viewport smoothly across the canvas whenever the left mouse button (or touch/stylus) is held and dragged—even when starting over existing strokes, shapes, labels, or pasted images.

## Activation
- **Toolbar / Quick Menu**: Click **`Move Pos`**.
- **Keyboard Shortcut**: `G`.

## Controls & Behavior
- **Click & Drag Anywhere**: Holding the left mouse button down and dragging translates `PanOffset` by the exact pointer movement without selecting, modifying, or accidentally moving any strokes underneath the cursor.
- **Combine With Zoom**: Works smoothly at any zoom level (`25%`–`400%`); click **`1:1`** or press **`Ctrl+0`** to reset `Zoom` and `PanOffset`.
- **Contrast With Move/Pan (`V`/`M`)**: Use **`Move/Pan`** (`V`/`M`) to drag an individual stroke or pasted image; use **`Move Pos`** (`G`) when you always want pure viewport panning regardless of objects beneath your cursor.

## Implementation Reference
- Enum: `DrawingToolKind.MovePosition` in `Drawing/DrawingModels.cs`
- Panning Handler: `OnPointerPressed` / `OnPointerMoved` / `OnPointerReleased` (`_isPanning`) in `Drawing/DrawingCanvasControl.cs`
