# Resize Selection Tool (`DrawingToolKind.ResizeSelection`)

## Overview
Displays 8 corner and edge resize handles around the active **`Select Area`** rectangle and proportionally scales both the selection box and every stroke, shape, label, or pasted image inside it.

## Activation
- **Toolbar / Quick Menu**: Click **`Resize Sel`**.
- **Keyboard Shortcut**: `Q`.

## Controls & Behavior
- **8 Resize Handles**: Selecting **`Resize Sel`** renders 8 crisp white/blue corner and edge midpoint handles on `SelectionArea`.
- **Proportional Scaling**: Drag any corner or edge handle to scale the selection box (enforcing a minimum size of **`5 × 5`** pixels) while remapping every captured stroke's `Points` and optional `ClipRect` proportionally into the new rectangle.
- **Inside-Area Fallback**: Dragging inside the selection box adjusts the bottom-right corner smoothly if you miss an exact handle pixel.
- **Single-Step Undo / Redo**: Pushes one clean undo snapshot per resize drag (`Ctrl+Z` / `Ctrl+Y` restores both strokes and the pre-resize selection rectangle).

## Implementation Reference
- Enum: `DrawingToolKind.ResizeSelection` in `Drawing/DrawingModels.cs`
- Handle Hit Testing & Proportional Scaling: `HitTestResizeHandle`, `ComputeResizedRect`, `_isResizingSelection`, and `DrawSelectionOverlay` in `Drawing/DrawingCanvasControl.cs`
