# Move Selection Tool (`DrawingToolKind.MoveSelection`)

## Overview
Translates an active **`Select Area`** region (`SelectionArea`) together with every stroke, shape, label, and pasted image intersecting that area.

## Activation
- **Toolbar / Quick Menu**: Click **`Move Sel`**.
- **Keyboard Shortcut**: `W`.

## Controls & Behavior
- **Drag Selection & Contents**: Click inside the active selection rectangle (or within `10 / Zoom` pixels of its border) and drag to move both `SelectionArea` and every intersecting stroke's `Points` and `ClipRect` simultaneously.
- **Fallback When No Area Selected**: If no selection area is currently active, dragging near an individual stroke or image falls back to moving that single object.
- **Single-Step Undo / Redo**: Each drag operation records one clean undo snapshot so `Ctrl+Z` / `Ctrl+Y` reverts or replays both the strokes and the selection position together.

## Implementation Reference
- Enum: `DrawingToolKind.MoveSelection` in `Drawing/DrawingModels.cs`
- Selection Translation: `StrokeIntersectsRect`, `TranslateStroke`, and `_isMovingSelection` handling in `Drawing/DrawingCanvasControl.cs`
