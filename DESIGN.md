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

## 2. Closable Views & `View & Mode` → `View` Submenu
Every workspace panel ("view") is independently closable when not needed and can be restored from the top toolbar's **`View & Mode`** dropdown menu:

### Closable Panels (Each displays a header with a `×` Close button)
1. **Vault Explorer** — Left-top collapsible file/folder tree (`▼`/`▶`), search filter (preserving matching & parent folders), multi-file selection (`Ctrl+Click`, `Shift+Click`, `Ctrl+A`), right-click file context menu (`Cut`, `Copy`, `Paste`, `Select All`, `Delete` + shortcuts `Ctrl+X`/`Ctrl+C`/`Ctrl+V`/`Del`/`Ctrl+A` when Vault Explorer is active), `+ Note`, `+ Folder`, per-folder `+` note creation, Vault subfolders (e.g., `Notes`), slim non-overlapping `6px` scrollbars (`AllowAutoHide="False"` with dedicated gutter margins across Vault Explorer & opened tab strip), and strict single-vault enforcement (`OpenVaultAtPath` unloads previous tabs/selection/clipboard and avoids injecting `SampleVault` starter files into user-opened vaults).
2. **Outline & Tags** — Left-bottom clickable heading outline (`H1`–`H6`) and `#tag` badges extracted from the active note.
3. **Formatting Sub-Toolbar** — Context ribbon row below the ribbon tabs (Drawing triggers and Git actions).
4. **Editor Pane** — Monospace Markdown + LaTeX source editor with cursor line/column tracking and formatting helpers.
5. **Live LaTeX Code Preview** — Real-time preview pane attached below the Editor Pane for instant visual feedback while typing LaTeX expressions.
6. **Document Preview Pane** — Live rendered Markdown + LaTeX cards + interactive inline ```` ```drawing ```` canvases (or full-screen canvas for `.ndraw` files).
7. **Status Bar** — Bottom bar showing save state, visible view count, cursor position, active LaTeX block context, word/character count, and zoom controls.

### Responsive Top Toolbar Menu (`Menu` + Wrapping Quick Controls), Ribbon Tabs & `?` Button
- The top bar combines a responsive desktop **`Menu`** (`File`, `Edit & Insert`, `View & Mode`, `Options`, `Git`, `Theme`) with wrapping quick-action controls (`+ Note`, `+ Canvas in Doc`, `📋 Paste Image`, `Save`, `Undo`, `Redo`, `Light/Dark Mode`, `?` Syntax Guide) inside a `WrapPanel` so top-bar items wrap cleanly without overlapping on smaller window widths.
- Inside **`Options`**: **`Auto-Save Documents While Editing`** toggles automatic disk saving as edits occur (persisted to `<exe>/canvas/app_options.json`).
- The ribbon below hosts **`Drawing Tools`** and **`🔀 Git`** tabs, while the Markdown & LaTeX Syntax Guide is accessible via the **`?`** help button in the top toolbar (and `F1`).
- Inside **`View & Mode`**:
  - Mode switch items (`✨ Unified Live View` and `📄 Raw Source Mode (H1–H5 Fold)`).
  - A **`View`** submenu containing toggle items with `[✓]` / `[ ]` indicators for all 7 Workspace Views above:
    - Closing a view via its `×` button updates its **`View & Mode` → `View`** menu item automatically.
    - Clicking a closed view's toggle inside **`View & Mode` → `View`** immediately restores it.
  - A **`Reset All Views`** menu item restores the complete default workspace layout.

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
- **White Canvas & Live Mouse Preview**:
  - Dedicated `#FFFFFF` white drawing surface with crisp dark default ink (`#0F172A`) and Paper/Grid patterns (`Grid`, `Dotted`, `Ruled`, `Blank`).
  - Live mouse cursor preview ring / crosshair showing active brush radius and logical cursor coordinates directly on the canvas.
- **Tools**:
  - `Pen`: Smoothed quadratic-Bezier freehand strokes with variable thickness/pressure.
  - `Highlighter`: Semi-transparent wide ink strokes (`Opacity = 0.36`).
  - `Eraser`: Freehand eraser brush matching the `Pen` drawing feel (`DrawingToolKind.Eraser`) rather than deleting entire strokes, with full Undo/Redo support.
  - `Shapes`: `Line`, `Arrow` (with computed directional arrowhead wings), `Rectangle`, and `Ellipse` with live rubber-band preview during drag.
  - `Text / LaTeX Label`: Click to place plain text or `$...$` / LaTeX math annotations directly on the canvas (rendered via `LatexMathRenderer`).
  - `Pasted Images`: Paste bitmaps or image files directly into any canvas (`Ctrl+V`, `📋 Paste`, or right-click **`📋 Paste Image`**; stored inside the vector canvas as `DrawingToolKind.Image` with Base64 PNG encoding, SVG `<image>` export, Undo/Redo, and repositionable with `Move/Pan`), and copy any canvas bitmap via `Ctrl+C`, `📋 Copy`, or right-click **`📋 Copy Canvas`**.
  - `Select / Pan`: Move existing strokes or pasted images, or pan the viewport; mouse wheel / tablet dial, trackpad pinch, `+`/`-` (`Ctrl+0` reset), toolbar & right-click sub-menu **Zoom sliders (25%–400%)** zoom smoothly around the active cursor/pen even mid-stroke, while window/document scrolling stays suppressed whenever a click/pen stroke starts inside the canvas window (scrolling only occurs when the press started outside the canvas).
- **Color Selector, Remembered Colors & Right-Click Sub-Menu**:
  - Compact color indicator button + top-bar quick-select chips for recently remembered colors (`RememberedColors` persisted to `<exe>/canvas/remembered_colors.json`), paired with a right-click floating sub-menu on the canvas (`ColorSpectrumPickerControl` 2D SV square + rainbow Hue bar, RGB sliders, `#RRGGBB` input, remembered quick colors, `+ Remember`, 12 preset chips, quick tool buttons, and brush size slider).
  - Full `Undo` / `Redo` stacks, `Clear`, `Zoom -/+`, `Reset Zoom`, **Canvas Resize** (`W × H` inputs, `Resize`, `Fit Bounds`, and bottom drag-to-resize handle with undo support), `Export PNG` (`RenderTargetBitmap`), `Export SVG`, and `📋 Copy Image` to clipboard.
- **Persistence & Used-Bounds View**:
  - Auto-synchronizes inline ```` ```drawing ```` JSON blocks and `.ndraw` embeds; document canvases created via `+ Canvas in Doc` (`InsertNewDrawingBlockAtCaret` / `PrepareEmbeddedCanvasForOpen`) and pasted/imported images are stored directly in the active vault's `<vault>/canvas` and `<vault>/image` folders (with fallback lookup in `<exe>/canvas` and `<exe>/image`), and adding or saving a document canvas immediately persists the host `.md` file on disk so closing tabs or swapping files never discards the link. Collapsed document drawing previews frame tightly around `DrawingDocument.GetUsedBounds(...)` (`HorizontalAlignment.Left`).

---

## 5. Productivity, Auto-Save, Drag-and-Drop, Git & Navigation
- **Last Opened Vault Persistence**: Startup reopens the user's most recently opened vault directory (`LastVaultPath` persisted in `<exe>/canvas/app_options.json` via `ThemeConfigService.GetSavedLastVaultPath()`) when it exists on disk, falling back cleanly to `SampleVault`.
- **Obsidian Vault Image & Crash Compatibility**: Obsidian wiki-image embeds (`![[image.png]]`, `![[folder/img.png|400]]`) and standard Markdown images (`![alt](<folder/space image.png>)`) resolve across active note directories and vault subfolders (including `Attachments/`, `assets/`, `images/`) and render directly as `Image` visuals rather than creating `.ndraw` canvases, with fault-tolerant directory traversal (`VaultService`) and non-`.ndraw` sanitization (`DocumentAssetService`).
- **Git Integration (`GitService`)**: Built-in `git` CLI source control accessible from the top **`Git`** menu, **`🔀 Git`** ribbon tab, status bar branch/change badge, or **`Ctrl+Shift+G`** panel (`_gitOverlay`) with **`Init Repo`**, **`Commit All`**, **`Commit & Push`**, **`Pull`**, **`Push`**, **`Branch`** switch/create, **`Remote`** URL setup, changed file diffs, and commit history.
- **Auto-Save Option & Unsaved Work Exit Prompt**: When **`Options` -> `Auto-Save Documents While Editing`** is active, edits in Markdown notes and drawings save automatically to disk as content is added. When Auto-Save is turned off and any document has unsaved changes (`●`), closing the application shows an **`Unsaved Work — NoteDraw`** MessageBox offering **`Save Unsaved Work`**, **`Discard Unsaved Work`**, or **`Cancel`** exiting.
- **Drag-and-Drop Images at Target Line**: Users can drag images from the Vault Explorer **`image`** folder (or external file manager) and release the mouse over any line in the document to insert `![...](image/...)` directly at the release line (`GetDocumentLineAtPoint` / `InsertImageMarkdownAtLine`), reusing files already inside `<vault>/image` (with `<exe>/image` fallback) without creating duplicates.
- **Multi-Tab Workspace**: Open multiple `.md` notes and `.ndraw` drawings simultaneously with dirty indicator (`●`) and per-tab close buttons.
- **Quick Switcher (`Ctrl+P`)**: Keyboard-accessible filter overlay to jump instantly to any note, drawing, or `#tag` in the Vault.
- **Wiki-Links (`[[Note Name]]`)**: Clicking a wiki-link opens the target note or offers/creates it if missing.
- **Interactive Checklists**: Clicking `- [ ]` / `- [x]` checkboxes in Document Preview toggles the source Markdown directly.
- **Keyboard Shortcuts**:
  - `Ctrl+S` / `Ctrl+Shift+S`: Save active document / Save all unsaved documents
  - `Ctrl+Shift+G`: Open Git Source Control Panel
  - `Ctrl+P`: Quick Switcher
  - `Ctrl+N`: New Markdown Note
  - `Ctrl+Shift+D`: New Drawing
  - `Ctrl+C` / `Ctrl+X` / `Ctrl+V` / `Del` / `Ctrl+A`: Copy, cut, paste, delete, or select files when Vault Explorer is focused (non-`TextBox`)
  - `Ctrl+Z` / `Ctrl+Y`: Undo / Redo on active canvas
  - Drawing Mode single-key tool shortcuts (when not typing in a text box): `P`/`1` (Pen), `H`/`2` (Highlight), `E`/`3` (Eraser), `I`/`K`/`4` (Pipette), `L`/`5` (Line), `A`/`6` (Arrow), `R`/`7` (Rect), `O`/`C`/`8` (Ellipse), `T`/`9` (Text/$LaTeX$), `V`/`M` (Move/Pan) — documented in `docs/canvas/`

---

## 6. Classic System Theme & Separate `dark.json` / `light.json` Files
- **Classic System Dark & Light Palettes**: Neutral dark-gray system palette (`#1E1E1E` chrome background, `#252526` panels, `#2D2D2D` headers, `#2A2D2E` cards, `#181818` code surfaces, `#3C3C3C` borders, `#E0E0E0` text, `#9E9E9E` muted text, and `#0078D4` / `#4CC2FF` accents) defined in `ThemePalette.CreateClassicDark()` alongside `ThemePalette.CreateClassicLight()`, both using `#FFFFFF` white drawing canvas defaults.
- **Separate Theme Files (`dark.json` & `light.json`)**: Stored independently in the active Vault or repository root via `ThemeConfigService`, each exposing `ThemeName`, `EditorFontSize`, `PreviewFontSize`, `EditorFontFamily`, and all palette color tokens with safe hex fallback (`ParseColor`).
- **Live Config Reload & Custom Theme Loading**: Clicking `dark.json` or `light.json` opens the respective theme file in an editor tab and saving (`Ctrl+S`) hot-reloads the palette; `Load Theme...` lets users load any custom `.json` theme file via `LoadPaletteFromFile(...)`.

---

## 7. Collapsible `H1`–`H5` Heading Sections (Editor & Preview)
- **Editor Gutter Toggles**: `FoldableEditorControl` pairs the Markdown `TextBox` with `HeadingFoldGutterControl` and a folded-sections banner so clicking side-gutter `▼` / `▶` toggles collapses or expands any `H1`–`H5` section.
- **Lossless Source Preservation**: `EditorFoldManager` reconstructs hidden section bodies via `GetFullUnfoldedText(...)` before saving, rendering preview, or running toolbar edits so folded content is never lost.
- **Preview Heading Toggles**: `MarkdownPreviewControl` also supports collapsing and expanding `H1`–`H5` sections directly in the rendered view (`TogglePreviewHeadingFold`).

---

## 8. Unified 1-Window Live-Cursor Editor, Syntax Guide & Click-to-Edit Images
- **Unified Single Window**: Instead of side-by-side split columns, NoteDraw merges editing and preview into a single unified workspace pane (`Unified Live View` / `Raw Source Mode`) with `Live LaTeX Code Preview` dockable cleanly underneath.
- **Single-Click & Keyboard Live Cursor Editing**: Clicking once on any line or navigating with arrow keys (`Up`/`Down`/`Left`/`Right`, `Enter`, `Backspace`) seamlessly reveals that element's raw Markdown source directly at the cursor (e.g., heading lines display `# {text here}` cleanly while surrounding blocks stay rendered, with instant inline LaTeX preview while typing math) without shifting the viewport scroll offset, and clicking next to a picture places a classic text-editor cursor right beside the left or right edge of the picture (`ActivatePictureAdjacentCursor` / `WrapPictureCardWithAdjacentCursor`).
- **Read-Only Markdown & LaTeX Syntax Guide (`?` Button & `F1`)**: Accessible from the **`?`** help button in the main toolbar (or `F1`), `SyntaxGuideSubMenuControl` explains how to write Markdown and LaTeX syntax with live formula previews instead of inserting snippets for you, leaving text editor right-click free.
- **Word / Obsidian-Style Regular Images, Tight Vector Drawing Bounds & Copy/Paste Images**: Regular Markdown/pasted images (`![alt](...)` / `![[...png]]`) render cleanly inline without header chrome or edit overlays; left-clicking before/left-half or after/right-half places a classic text-editor cursor right beside the image, right-clicking opens a `ContextMenu` with **`📋 Copy Image`** and **`🗑 Delete Image`**, and `Backspace`/`Delete` next to the image deletes it cleanly. Embedded ```` ```drawing ```` canvases and `![[canvas/...ndraw]]` links preserve their tight used bounds (left-aligned without full-page white padding), support inline editing (`"✓ Done Editing Image"` refreshes immediately), and include **`🗗 Open in a seperate tab`** in their `ContextMenu` and header (`OpenCanvasInSeparateTab`) to edit the canvas in a dedicated full `.ndraw` tab synchronized back to the source note (alongside `✎ Edit Image`, `📋 Copy Image`, and `🗑 Delete Image`). Drawing mode also includes a **`Pipette`** color sampler (`DrawingToolKind.Pipette`), quick tool shortcuts, and per-tool documentation under `docs/canvas/`. Images pasted (`Ctrl+V` or `📋 Paste Image`) store files automatically in `<exe>/image` and place the cursor right after the pasted image.
