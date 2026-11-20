# NoteDraw — Design Specification

> **Status**: Approved Design Specification (Single source of truth at repository root: `DESIGN.md`)
> **Target Platforms**: Windows 10/11, macOS, Linux (`net10.0`, `Avalonia 12.0.4`)

---

## 1. Vision & Core UX Approach
**NoteDraw** is a user-friendly, local-first, cross-platform note-taking desktop application combining **Obsidian-style Markdown notes**, **real-time LaTeX math typesetting**, and **vector hand drawing** inside a single cohesive workspace.

### Core UX Model: Vault + Markdown/LaTeX + Embedded & Standalone Canvases
- **Vault-Based Workspace**: All notes and drawings reside in a user-selectable local folder ("Vault").
  - **Markdown Notes (`.md`)**: Plain-text Markdown documents supporting headings, rich formatting, checklists, tables, blockquotes, code blocks, `#tags`, `[[WikiLinks]]`, inline/block LaTeX, and embedded interactive ```` ```drawing ```` blocks.
  - **Standalone Drawings (`.ndraw`)**: JSON-serialized vector canvases for dedicated freehand diagrams, sketches, and annotated math/note sheets that can also be embedded via `![[Name.ndraw]]`.
- **User-Friendliness First**:
  - Clean ribbon/tabbed toolbar for formatting, LaTeX templates, drawing actions, and view visibility.
  - Pre-populated `SampleVault` on first launch so users immediately experience Markdown, live LaTeX blocks, and an interactive hand-drawn diagram.
  - Light and Dark theme support with adaptive stroke/canvas contrast.

---

## 2. Closable Views & Toolbar `View` Tab
Every workspace panel ("view") is independently closable when not needed and can be restored from the top toolbar:

### Closable Panels (Each displays a header with a `×` Close button)
1. **Vault Explorer** — Left-top file/folder tree, search filter, `+ Note`, `+ Drawing`, rename/delete actions.
2. **Outline & Tags** — Left-bottom clickable heading outline (`H1`–`H6`) and `#tag` badges extracted from the active note.
3. **Formatting Sub-Toolbar** — Context ribbon row below the ribbon tabs (Markdown formatting, layout mode toggles, LaTeX snippet buttons, Drawing triggers).
4. **Editor Pane** — Monospace Markdown + LaTeX source editor with cursor line/column tracking and formatting helpers.
5. **Live LaTeX Code Preview** — Real-time preview pane attached below the Editor Pane for instant visual feedback while typing LaTeX expressions.
6. **Document Preview Pane** — Live rendered Markdown + LaTeX cards + interactive inline ```` ```drawing ```` canvases (or full-screen canvas for `.ndraw` files).
7. **Status Bar** — Bottom bar showing save state, visible view count, cursor position, active LaTeX block context, word/character count, and zoom controls.

### Top Toolbar `View` Tab
- The top ribbon includes a dedicated **`View`** tab alongside `Edit & Format`, `LaTeX Assistant`, and `Drawing Tools`.
- Inside the **`View`** tab:
  - Toggle buttons with `[✓]` / `[ ]` indicators for all 7 views above.
  - Closing a view via its `×` button updates its `View` tab toggle automatically.
  - Clicking a closed view's toggle in the `View` tab immediately restores it.
  - A **`Reset All Views`** button restores the complete default workspace layout.

---

## 3. Real-Time LaTeX Engine & Obsidian-Style Live Code Preview
NoteDraw natively parses and renders LaTeX formulas using Avalonia vector graphics (`DrawingContext`), with zero dependency on external TeX distributions or WebViews:

### Supported Delimiters & Blocks
- **Fenced Code Blocks**: ```` ```latex ... ``` ```` and ```` ```math ... ``` ```` (Obsidian-style fenced blocks)
- **Display Math**: `$$ ... $$` (single-line or multi-line)
- **Inline Math**: `$ ... $`

### Live Typing Preview Experience
- As the user moves the caret or types inside any ```` ```latex ````, ```` ```math ````, `$$...$$`, or `$...$` block in the Editor Pane, `LatexLiveDetector` detects the active expression and updates the **Live LaTeX Code Preview** panel immediately on every keystroke.
- Fault-tolerant parsing never crashes or blanks out on half-typed syntax (e.g., `\frac{1}{`); instead, it renders the partial expression cleanly and displays a helpful non-blocking diagnostic hint.
- Includes an interactive **LaTeX Scratchpad** input inside the Live LaTeX Preview panel so users can test or copy any formula anytime.
- **Document Preview** renders fenced ```` ```latex ```` / ```` ```math ```` and `$$...$$` blocks as styled Math Cards with a `"LaTeX"` badge and click-to-focus editor jump.

### Supported Math Constructs
- Lowercase & uppercase Greek letters, binary operators, relation symbols, logic/calculus/arrow symbols, blackboard bold (`\mathbb{R}`, `\mathbb{N}`, `\mathbb{Z}`, `\mathbb{Q}`, `\mathbb{C}`), and named math functions (`\sin`, `\cos`, `\lim`, etc.).
- Fractions (`\frac`, `\dfrac`, `\tfrac`, `\binom`), roots (`\sqrt{x}`, `\sqrt[n]{x}`).
- Subscripts/superscripts (`_`, `^`) and stacked limits on big operators (`\sum`, `\int`, `\iint`, `\iiint`, `\oint`, `\prod`, `\lim`).
- Scalable delimiters (`\left( ... \right)`, `[...]`, `\{...\}`, `|...|`) and multi-row environments (`matrix`, `pmatrix`, `bmatrix`, `Bmatrix`, `vmatrix`, `Vmatrix`, `cases`, `aligned`, `array`).
- Accents (`\hat`, `\bar`, `\vec`, `\dot`, `\ddot`, `\tilde`, `\overline`, `\underline`, `\overbrace`, `\underbrace`) and text styling (`\text`, `\mathbf`, `\mathrm`, `\mathit`, `\mathbb`, `\mathcal`, `\color`).

---

## 4. Vector Hand Drawing Engine
Available both as **standalone `.ndraw` files** and **inline ```` ```drawing ```` blocks** (or `![[file.ndraw]]` embeds) inside Markdown notes:

### Tools & Features
- **Tools**:
  - `Pen`: Smoothed quadratic-Bezier freehand strokes with variable thickness/pressure.
  - `Highlighter`: Semi-transparent wide ink strokes (`Opacity = 0.38`).
  - `Eraser`: Hit-tests and removes strokes along the pointer drag path.
  - `Shapes`: `Line`, `Arrow` (with computed directional arrowhead wings), `Rectangle`, and `Ellipse` with live rubber-band preview during drag.
  - `Text / LaTeX Label`: Click to place plain text or `$...$` / LaTeX math annotations directly on the canvas (rendered via `LatexMathRenderer`).
  - `Select / Pan`: Move existing strokes or pan the viewport; scroll wheel zooms cleanly.
- **Customization & Actions**:
  - Color swatches (adaptive ink auto-contrast + Blue, Red, Green, Orange, Purple, Teal, Yellow).
  - Thickness presets (`2px`, `4px`, `8px`, `14px`) + continuous slider.
  - Canvas backgrounds (`Grid`, `Dotted`, `Ruled`, `Blank`).
  - Full `Undo` / `Redo` stacks, `Clear`, `Zoom -/+`, `Reset Zoom`, `Export PNG` (`RenderTargetBitmap`), and `Export SVG`.
- **Persistence**:
  - Auto-synchronizes inline ```` ```drawing ```` JSON blocks back to the Markdown note source and standalone `.ndraw` files to disk.

---

## 5. Productivity & Navigation
- **Multi-Tab Workspace**: Open multiple `.md` notes and `.ndraw` drawings simultaneously with dirty indicator (`●`) and per-tab close buttons.
- **Quick Switcher (`Ctrl+P`)**: Keyboard-accessible filter overlay to jump instantly to any note, drawing, or `#tag` in the Vault.
- **Wiki-Links (`[[Note Name]]`)**: Clicking a wiki-link opens the target note or offers/creates it if missing.
- **Interactive Checklists**: Clicking `- [ ]` / `- [x]` checkboxes in Document Preview toggles the source Markdown directly.
- **Keyboard Shortcuts**:
  - `Ctrl+S`: Save active document
  - `Ctrl+P`: Quick Switcher
  - `Ctrl+N`: New Markdown Note
  - `Ctrl+Shift+D`: New Drawing
  - `Ctrl+Z` / `Ctrl+Y`: Undo / Redo on active canvas
