# Text & `$LaTeX$` Label Tool (`DrawingToolKind.TextLabel`)

## Overview
Places plain text annotations or rendered mathematical LaTeX labels directly onto the vector canvas at any clicked coordinate.

## Activation
- **Toolbar / Sub-Menu**: Click **`Text/$LaTeX$`**.
- **Keyboard Shortcuts**: `T`, `9`, or `NumPad9`.

## Controls & Behavior
- **Placement**: Clicking the canvas with `Text/$LaTeX$` active opens the inline Canvas Note / LaTeX prompt bar (`_textPromptBox`) anchored at that canvas position.
- **Plain vs. LaTeX Auto-Detection**:
  - Text wrapped in `$...$` or `$$...$$`, or containing LaTeX backslashes (`\frac`, `\int`, etc.), is automatically flagged as `IsLatex = true` and rendered using NoteDraw's native `LatexParser` + `LatexMathRenderer` inside a clean pill badge.
  - Plain strings render cleanly with medium-weight text in the active color.
- **Commit / Cancel**: Press `Enter` (or click `Place on Canvas`) to commit the label onto the canvas with full Undo/Redo support; press `Escape` to cancel.

## Implementation Reference
- Enum: `DrawingToolKind.TextLabel` in `Drawing/DrawingModels.cs`
- Placement & Rendering: `AddTextLabel`, `TextLabelPlacementRequested`, and `DrawStroke` in `Drawing/DrawingCanvasControl.cs`
- Prompt Bar UI: `_textPromptBar` and `CommitTextPrompt` in `Drawing/DrawingWorkspaceControl.cs`
