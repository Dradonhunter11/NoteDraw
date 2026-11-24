# NoteDraw Drawing Mode (`docs/canvas/`)

Complete user and developer reference for NoteDraw's vector Drawing Mode (`DrawingWorkspaceControl` and `DrawingCanvasControl`), used both in inline document canvases (```` ```drawing ```` and `![[*.ndraw]]`) and in full-tab standalone `.ndraw` views.

---

## Tool Index & Keyboard Shortcuts

> Keyboard shortcuts work whenever focus is inside the drawing workspace/canvas (and are automatically ignored while typing inside text inputs such as the LaTeX label prompt, hex color input, or canvas width/height boxes). Modifier combinations (`Ctrl+...`, `Alt+...`) remain reserved for application commands such as `Ctrl+Z` / `Ctrl+Y`.

### Brush, Highlight, Erase & Color Picking

| Tool | Toolbar Label | Shortcuts | Guide |
| --- | --- | --- | --- |
| Pen | `Pen` | `P`, `1` | [pen.md](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/pen.md) |
| Highlighter | `Highlight` | `H`, `2` | [highlighter.md](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/highlighter.md) |
| Eraser | `Eraser` | `E`, `3` | [eraser.md](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/eraser.md) |
| Pipette Color Picker | `Pipette` | `I`, `K`, `4` | [pipette.md](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/pipette.md) |

### Geometric Shapes

| Tool | Toolbar Label | Shortcuts | Guide |
| --- | --- | --- | --- |
| Line | `Line` | `L`, `5` | [line.md](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/line.md) |
| Arrow | `Arrow` | `A`, `6` | [arrow.md](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/arrow.md) |
| Rectangle | `Rect` | `R`, `7` | [rectangle.md](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/rectangle.md) |
| Ellipse | `Ellipse` | `O`, `C`, `8` | [ellipse.md](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/ellipse.md) |

### Annotation & Viewport Tools

| Tool | Toolbar Label | Shortcuts | Guide |
| --- | --- | --- | --- |
| Text & `$LaTeX$` Label | `Text/$LaTeX$` | `T`, `9` | [text-latex.md](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/text-latex.md) |
| Move & Pan | `Move/Pan` | `V`, `M` | [move-pan.md](file:///c:/Users/alexa/Documents/Github/NoteDraw/docs/canvas/move-pan.md) |

---

## Opening a Document Canvas in a Separate Tab

When editing an inline ```` ```drawing ```` block or `![[*.ndraw]]` canvas inside a Markdown note, right-click the canvas card (or click the action button in its header) and select **`🗗 Open in a seperate tab`**:

- Opens the canvas in a dedicated `.ndraw` editor tab filling the main workspace outside the Markdown document stream.
- All edits made in the separate tab update the source `.ndraw` / inline ```` ```drawing ```` block automatically so switching back to the Markdown tab reflects your changes immediately.

---

## Global Canvas Controls & Navigation

- **Color Selector & Quick Chips**: Click the `Color (#RRGGBB)` button on the toolbar or right-click anywhere on the canvas to open the floating HSV spectrum, RGB sliders, Hex input, preset palette, `🧪 Pipette` button, and remembered quick colors (`<exe>/canvas/remembered_colors.json`).
- **Thickness Slider**: Adjusts base stroke width (`1px`–`24px`) for Pen, Highlighter, Eraser, Line, Arrow, Rectangle, and Ellipse.
- **Background Style**: Switch between `Grid`, `Dotted`, `Ruled`, and `Blank`.
- **Zoom & Pan**: Mouse wheel or trackpad pinch zooms anchored under the cursor (`25%`–`400%`); keyboard `+` / `-` zooms cleanly (even mid-stroke); `Ctrl+0` or `1:1` resets zoom and pan; middle-mouse drag pans from any tool.
- **Copy & Paste Images on Canvas**: Press `Ctrl+V` (or click **`📋 Paste`** in the toolbar / **`📋 Paste Image`** in the right-click menu) to paste a copied bitmap or image file directly onto the canvas (centered or at the mouse cursor position). Pasted images persist inside `.ndraw` / ```` ```drawing ```` JSON, export into SVGs, support Undo/Redo, and can be repositioned with **`Move/Pan`** (`V`/`M`). Press `Ctrl+C` (or click **`📋 Copy`** / **`📋 Copy Canvas`**) to copy the rendered canvas bitmap to the clipboard.
- **Undo / Redo**: `Ctrl+Z` undoes strokes, pasted images, canvas clears, and resizes; `Ctrl+Y` or `Ctrl+Shift+Z` redoes.
- **Resize & Fit Bounds**: Enter exact `Width × Height` pixel dimensions, drag the bottom resize handle, or click `Fit Bounds` to crop tightly around drawn content.
- **Export**: Click `PNG` or `SVG` in the toolbar to export your vector canvas.
