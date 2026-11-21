# Move & Pan Tool (`DrawingToolKind.SelectPan`)

## Overview
Allows repositioning existing strokes, shapes, or text/LaTeX labels on the canvas—or panning the infinite viewport when dragging empty canvas space.

## Activation
- **Toolbar / Sub-Menu**: Click **`Move/Pan`**.
- **Keyboard Shortcuts**: `V` or `M`.
- **Universal Middle-Mouse Pan**: Holding the middle mouse button pans from any active tool without switching away from your current brush or shape tool.

## Controls & Behavior
- **Drag an Object**: Click within `12 / Zoom` logical pixels of an existing stroke, shape edge, or label to translate all of its points smoothly across the canvas (`_pushedMoveUndo` pushes one clean undo snapshot per drag).
- **Drag Empty Space**: Dragging where no stroke is hit translates `PanOffset` smoothly across the viewport.
- **Reset View**: Click **`1:1`** or press **`Ctrl+0`** to reset `Zoom = 1.0` and `PanOffset = (0, 0)`.

## Implementation Reference
- Enum: `DrawingToolKind.SelectPan` in `Drawing/DrawingModels.cs`
- Hit Testing & Panning: `HitTestStroke`, `SetPanOffset`, and `OnPointerPressed` / `OnPointerMoved` in `Drawing/DrawingCanvasControl.cs`
