# Pipette Color Picker Tool (`DrawingToolKind.Pipette`)

## Overview
Samples the color of any existing stroke, shape, or text/LaTeX label on the canvas—or the canvas background color when clicking empty space—and sets it as the active drawing color without creating a new stroke.

## Activation
- **Toolbar / Quick Menu**: Click **`Pipette`**.
- **Color Selector Sub-Menu**: Right-click on the canvas (or click `Color (#RRGGBB)`) and click **`🧪 Pipette`** next to the Hex field.
- **Keyboard Shortcuts**: `I`, `K`, `4`, or `NumPad4`.

## Controls & Behavior
- **Live Color Readout**: Hovering over the canvas displays a color-filled preview ring (`SampleColorHexAtCanvasPoint`) and shows the hovered `#RRGGBB` hex code in the bottom-right HUD pill (`Pipette (#RRGGBB) • (x, y) • zoom%`).
- **Stroke & Background Sampling**: Left-clicking calls `PickColorAtCanvasPoint`, hit-testing the topmost stroke within `10 / Zoom` logical pixels.
  - Clicking a colored stroke, shape, or label picks its resolved `#RRGGBB` color.
  - Clicking an eraser stroke or empty canvas background picks the effective canvas background color (e.g. `#FFFFFF`).
- **Automatic Persistence**: Raising `ColorPickedByPipette` invokes `DrawingWorkspaceControl.SelectCustomColor`, updating the active color button, HSV spectrum, RGB sliders, Hex box, and `RememberedColors` quick swatches.

## Implementation Reference
- Enum: `DrawingToolKind.Pipette` in `Drawing/DrawingModels.cs`
- Methods & Event: `SampleColorHexAtCanvasPoint`, `PickColorAtCanvasPoint`, `ColorPickedByPipette` in `Drawing/DrawingCanvasControl.cs`
- Workspace Sync: `_canvas.ColorPickedByPipette += (_, hex) => SelectCustomColor(hex)` in `Drawing/DrawingWorkspaceControl.cs`
