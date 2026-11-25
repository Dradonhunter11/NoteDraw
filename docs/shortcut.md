# NoteDraw Keyboard & Pointer Shortcuts

Complete categorized reference for all keyboard shortcuts, modifier clicks, and pointer gestures in NoteDraw.

> [!NOTE]
> **Platform Modifier Convention (`PlatformShortcuts`)**
> - **Windows & Linux**: Use `Ctrl` (`KeyModifiers.Control`) exclusively.
> - **macOS**: Use `Cmd` (`KeyModifiers.Meta`) exclusively (`Ctrl` does not trigger application command shortcuts on macOS).
> - Menu headers and tooltips automatically display `Cmd+...` on macOS and `Ctrl+...` on Windows/Linux.

---

## Table of Contents

1. [Global & File Operations](#1-global--file-operations)
2. [Global Editing & Clipboard](#2-global-editing--clipboard)
3. [Vault Explorer](#3-vault-explorer)
4. [Unified Markdown Editor & Preview](#4-unified-markdown-editor--preview)
5. [Drawing Mode — Tool Selection](#5-drawing-mode--tool-selection)
6. [Drawing Mode — Canvas Actions, Zoom & Pointer Controls](#6-drawing-mode--canvas-actions-zoom--pointer-controls)
7. [Dialogs & Overlays](#7-dialogs--overlays)

---

## 1. Global & File Operations

| Action | Windows / Linux | macOS | Notes |
| --- | --- | --- | --- |
| New Markdown Note | `Ctrl+N` | `Cmd+N` | Opens the new note prompt |
| New Standalone Drawing (`.ndraw`) | `Ctrl+Shift+D` | `Cmd+Shift+D` | Opens the new drawing prompt |
| Save Active Tab | `Ctrl+S` | `Cmd+S` | Saves the current note or `.ndraw` canvas |
| Save All Unsaved Tabs | `Ctrl+Shift+S` | `Cmd+Shift+S` | Saves every dirty open tab |
| Quick Switcher | `Ctrl+P` | `Cmd+P` | Jump to any note or drawing by name |
| Open Git Source Control Overlay | `Ctrl+Shift+G` | `Cmd+Shift+G` | Opens the Git commit, branch, and diff overlay |
| Toggle Markdown & LaTeX Syntax Guide | `F1` | `F1` | Opens or closes the searchable syntax reference |
| Close Active Modal Overlay | `Esc` | `Esc` | Closes Syntax Guide, Git Overlay, Unsaved Exit Dialog, Quick Switcher, or Prompt |

---

## 2. Global Editing & Clipboard

| Action | Windows / Linux | macOS | Notes |
| --- | --- | --- | --- |
| Undo | `Ctrl+Z` | `Cmd+Z` | Undoes in the active Markdown editor or drawing canvas |
| Redo | `Ctrl+Y` or `Ctrl+Shift+Z` | `Cmd+Y` or `Cmd+Shift+Z` | Redoes in the active Markdown editor or drawing canvas |
| Paste Image | `Ctrl+V` | `Cmd+V` | Pastes clipboard image into the active note (stored in vault `images/`) or active canvas (when no text input is focused) |
| Copy Active Canvas Bitmap | `Ctrl+C` | `Cmd+C` | Copies the rendered canvas image when canvas is active and no text input is focused |

---

## 3. Vault Explorer

These shortcuts apply when focus is inside the Vault Explorer panel:

| Action | Windows / Linux | macOS | Notes |
| --- | --- | --- | --- |
| Toggle File in Multi-Selection | `Ctrl+Click` | `Cmd+Click` | Adds or removes individual files from selection |
| Select Contiguous File Range | `Shift+Click` | `Shift+Click` | Selects all files between anchor and clicked item |
| Select All Files | `Ctrl+A` | `Cmd+A` | Selects all files in the vault tree |
| Copy Selected File(s) | `Ctrl+C` | `Cmd+C` | Copies selected vault file(s) to internal clipboard |
| Cut Selected File(s) | `Ctrl+X` | `Cmd+X` | Marks selected vault file(s) to move on paste |
| Paste Copied/Cut File(s) | `Ctrl+V` | `Cmd+V` | Pastes or moves into active target folder |
| Delete Selected File(s) | `Delete` | `Delete` | Deletes all selected files and closes matching tabs |

---

## 4. Unified Markdown Editor & Preview

| Action | Windows / Linux | macOS | Notes |
| --- | --- | --- | --- |
| Move to Previous / Next Segment | `Up` / `Down` | `Up` / `Down` | Transitions when caret is on the first (`Up`) or last (`Down`) line of the active segment |
| Cross Segment / Switch Picture Side | `Left` / `Right` | `Left` / `Right` | Moves across segment boundary at start/end, or switches cursor before/after an inline picture |
| Split Line / Insert Blank Line Beside Picture | `Enter` | `Enter` | Stays inside multi-line fenced code blocks, `$$`, or `\[` math blocks |
| Newline Without Splitting Segment | `Shift+Enter` | `Shift+Enter` | Inserts a newline inside the current inline segment |
| Indent 4 Spaces | `Tab` | `Tab` | Inserts 4 spaces inside fenced code blocks |
| Join with Previous Line / Delete Preceding Image | `Backspace` | `Backspace` | Triggered when caret is at the start of a line |
| Delete Focused Image or Canvas Card | `Delete` or `Backspace` | `Delete` or `Backspace` | Deletes the focused inline image or canvas block |
| Exit Active Inline Segment | `Esc` | `Esc` | Returns the active segment to its rendered preview state |
| Paste Image at Cursor / Beside Picture | `Ctrl+V` | `Cmd+V` | Inserts clipboard image at the active Markdown line or picture-adjacent cursor |

---

## 5. Drawing Mode — Tool Selection

> Single-key tool shortcuts work whenever focus is inside the drawing workspace or canvas. They are automatically ignored while typing in text inputs (such as the LaTeX label prompt, hex color input, or canvas width/height boxes) or when `Ctrl`, `Cmd`, or `Alt` is held.

See [canvas/README.md](canvas/README.md) for detailed per-tool documentation.

### Brush, Highlight, Erase & Color Picking

| Tool | Shortcuts | Guide |
| --- | --- | --- |
| Pen | `P`, `1`, `NumPad 1` | [canvas/pen.md](canvas/pen.md) |
| Highlighter | `H`, `2`, `NumPad 2` | [canvas/highlighter.md](canvas/highlighter.md) |
| Eraser | `E`, `3`, `NumPad 3` | [canvas/eraser.md](canvas/eraser.md) |
| Pipette Color Picker | `I`, `K`, `4`, `NumPad 4` | [canvas/pipette.md](canvas/pipette.md) |

### Geometric Shapes

| Tool | Shortcuts | Guide |
| --- | --- | --- |
| Line | `L`, `5`, `NumPad 5` | [canvas/line.md](canvas/line.md) |
| Arrow | `A`, `6`, `NumPad 6` | [canvas/arrow.md](canvas/arrow.md) |
| Rectangle | `R`, `7`, `NumPad 7` | [canvas/rectangle.md](canvas/rectangle.md) |
| Ellipse | `O`, `C`, `8`, `NumPad 8` | [canvas/ellipse.md](canvas/ellipse.md) |

### Annotation, Selection & Viewport

| Tool | Shortcuts | Guide |
| --- | --- | --- |
| Text & `$LaTeX$` Label | `T`, `9`, `NumPad 9` | [canvas/text-latex.md](canvas/text-latex.md) |
| Move & Pan Object | `V`, `M` | [canvas/move-pan.md](canvas/move-pan.md) |
| Move Position (Viewport Pan) | `G` | [canvas/move-position.md](canvas/move-position.md) |
| Select Area (Mask & Clip) | `S` | [canvas/select-area.md](canvas/select-area.md) |
| Move Selection | `W` | [canvas/move-selection.md](canvas/move-selection.md) |
| Resize Selection | `Q` | [canvas/resize-selection.md](canvas/resize-selection.md) |

---

## 6. Drawing Mode — Canvas Actions, Zoom & Pointer Controls

| Action | Windows / Linux | macOS | Notes |
| --- | --- | --- | --- |
| Undo Canvas Action | `Ctrl+Z` | `Cmd+Z` | Undoes strokes, pasted images, clears, moves, and resizes |
| Redo Canvas Action | `Ctrl+Y` or `Ctrl+Shift+Z` | `Cmd+Y` or `Cmd+Shift+Z` | Reapplies undone canvas actions |
| Copy Canvas Bitmap | `Ctrl+C` | `Cmd+C` | Copies rendered canvas image to the system clipboard |
| Paste Image onto Canvas | `Ctrl+V` | `Cmd+V` | Places pasted bitmap or image file at cursor or canvas center |
| Zoom In (`×1.15`) | `+` / `NumPad +` | `+` / `NumPad +` | Works smoothly even mid-stroke |
| Zoom Out (`÷1.15`) | `-` / `NumPad -` | `-` / `NumPad -` | Works smoothly even mid-stroke |
| Reset Zoom (`100%`) & Pan | `Ctrl+0` (`Ctrl+NumPad 0`) | `Cmd+0` (`Cmd+NumPad 0`) | Resets zoom to `1:1` and clears viewport pan offset |
| Zoom Under Cursor (`25%`–`400%`) | `Mouse Wheel` / `Trackpad Pinch` | `Mouse Wheel` / `Trackpad Pinch` | Anchors zoom to the current pointer coordinates |
| Pan Viewport (Any Tool) | `Middle-Mouse Drag` | `Middle-Mouse Drag` | Pans the canvas view regardless of the selected tool |
| Color & Canvas Quick Menu | `Right-Click` on canvas | `Right-Click` on canvas | Opens floating color picker, palette, Pipette, Copy/Paste, and Clear options |
| Clear Active Selection Area | `Esc` or `Double-Click` | `Esc` or `Double-Click` | With `Select Area`, double-clicking or dragging `< 5×5` also clears the selection |
| Commit / Cancel Text & `$LaTeX$` Prompt | `Enter` / `Esc` | `Enter` / `Esc` | Places or cancels the label inside the text prompt bar |
| Apply Canvas Width / Height | `Enter` | `Enter` | Applies dimensions entered in the canvas size boxes |

---

## 7. Dialogs & Overlays

| Context | Action | Windows / Linux & macOS | Notes |
| --- | --- | --- | --- |
| Quick Switcher | Open top match / Close | `Enter` / `Esc` | Selects first filtered note/drawing or dismisses switcher |
| Prompt Dialog | Submit / Cancel | `Enter` / `Esc` | Used for New Note, New Drawing, and Rename prompts |
| Git Commit Input | Commit / Close overlay | `Enter` / `Esc` | Commits staged/all vault changes with entered message |
