# NoteDraw Architecture Overview

> Generated maintenance documentation per `AGENT.md` Rule 5.

## High-Level Design
NoteDraw is a local-first C# / Avalonia UI (`net10.0`, `Avalonia 12.0.4`) note-taking application organized around three core subsystems:

1. **Vault, Assets & State Management (`Services/`)**:
   - `VaultService`: Reads/writes `.md` notes, `.ndraw` drawings, folders (`CreateFolder`, starter `Notes/` subfolder), and `VaultEntryKind.ImageFile` entries inside the active Vault and registered external asset folders (`RegisterExternalFolder`), preserves folders during filtered queries (`GetEntries`), extracts headings/tags/wiki-links, and resolves `[[WikiLink]]` targets.
   - `DocumentAssetService`: Manages executable-relative `<exe>/image` (`SavePastedBitmap`, `ImportImageFile`, `ResolveImagePath`) and `<exe>/canvas` (`CreateSavedCanvas`, `SaveInlineCanvasToCanvasFolder`, `LoadOrCreateCanvas`, `SaveCanvas`) storage.
   - `ThemeConfigService`: Loads and saves separate `dark.json` and `light.json` palette/typography files (`ThemePalette.CreateClassicDark()`, `ThemePalette.CreateClassicLight()`, `LoadPaletteFromFile`, `SavePaletteToFile`) with safe hex parsing (`ParseColor` / `ParseBrush`) and `#FFFFFF` white drawing canvas defaults.
   - `EditorFoldManager`: Detects `H1`–`H5` Markdown sections (`DetectSections`), manages per-heading fold/unfold state (`ToggleFoldAtLine`, `FoldSectionAtLine`, `UnfoldSectionAtLine`, `UnfoldAll`), computes gutter metadata (`GetGutterLines`), and reconstructs hidden content (`GetFullUnfoldedText`).
   - `ViewLayoutState`: Centralizes open/closed state for every workspace view (`VaultExplorer`, `OutlineTags`, `FormattingToolbar`, `EditorPane`, `LiveLatexPreview`, `PreviewPane`, `StatusBar`) and coordinates view visibility toggles with the top toolbar **`View`** tab.
   - `SelfTestRunner`: Headless-capable verification suite executed via `dotnet run -- --self-test`.

2. **Native Vector LaTeX Engine (`Rendering/Latex/`)**:
   - `LatexSymbolTable`: Static lookup tables for symbols, big operators, named functions, blackboard bold characters, accents, and spacing.
   - `LatexAst` & `LatexParser`: Fault-tolerant recursive-descent parser returning `LatexParseResult` (AST + non-blocking diagnostics while typing).
   - `LatexMathRenderer` & `LatexMathControl`: Recursive `MathBox` box-layout engine drawing fractions, roots, matrices, scripts, accents, and scalable delimiters directly onto Avalonia `DrawingContext`.
   - `LatexLiveDetector`: Caret-aware scanner detecting ```` ```latex ````, ```` ```math ````, `$$...$$`, and `$...$` contexts as the user types.

3. **Markdown, Live Cursor Editor, Syntax Guide & Vector Hand Drawing (`Rendering/` & `Drawing/`)**:
   - `FoldableEditorControl`: Pairs the Markdown `TextBox` (`InnerTextBox`) with `HeadingFoldGutterControl` and a folded-sections banner for `H1`–`H5` gutter fold toggles (`▼` / `▶`) while keeping full unfolded source synchronized via `FullText`.
   - `MarkdownPreviewControl`: Obsidian-style segment-based live-preview text editor (`BuildEditorSegments`, `ActivateSegmentAtLine`, `MoveCursorToAdjacentSegment`). Single-clicking or moving the keyboard cursor onto any line reveals raw Markdown (`# {text here}`, `- [ ]`, `$$...$$`, etc.) with inline live LaTeX preview while non-cursor lines remain rendered via `Markdig` (`1.6.0`). Also supports `H1`–`H5` folding (`TogglePreviewHeadingFold`), clickable `[[WikiLinks]]` / `#tags`, and tight-bounded click-to-edit cards for ```` ```drawing ````, `![[*.ndraw]]`, and Markdown images (`OpenInlineImageEditor`, `RenderMarkdownImageCard`, tight `RenderCanvasToBitmap`).
   - `SyntaxGuideSubMenuControl`: Searchable, categorized read-only reference sub-menu (`AllEntries`) explaining Markdown, LaTeX Math (with live `LatexMathControl` previews), and Drawing/Image syntax.
   - `DrawingModels`, `DrawingCanvasControl`, & `DrawingWorkspaceControl`: White `#FFFFFF` vector drawing engine with `GetUsedBounds(...)`, live mouse cursor preview (`DrawMousePreview`), smooth quadratic-Bezier pen, highlighter, freehand brush `Eraser`, geometric shapes, text/LaTeX labels, pan/zoom, undoable canvas resize (`ResizeCanvas`, `FitCanvasToUsedBounds`, `W × H` toolbar inputs, and bottom drag handle), right-click floating sub-menu (`ColorSpectrumPickerControl` 2D HSV + RGB/Hex selector + quick tools), Undo/Redo, PNG export (`RenderTargetBitmap`), and SVG export.

4. **Workspace Shell (`MainWindow.axaml`, `MainWindow.axaml.cs`)**:
   - Hosts the top header (including `Unified Live View`, `Raw Source Mode`, the `?` Syntax Guide button, `dark.json`, `light.json`, `Load Theme...`), 2-tab ribbon (`View`, `Drawing Tools`), left sidebar (collapsible `Vault Explorer` with `+ Note`, `+ Folder`, `Notes`, `canvas`, and `image` folders + `Outline & Tags`), single unified 1-window Editor & Preview (`_previewContentHost`) switching cleanly between live cursor Markdown/Image editor (`_markdownPreview`), raw foldable source (`_foldableEditor`), standalone drawing canvas (`_standaloneDrawing`), and image preview tabs, with Live LaTeX preview docked below, status bar, Quick Switcher (`Ctrl+P`), and separate `dark.json`/`light.json` theme hot-reload.

---

## Maintainer Extension Index (Where to Add Things in the Future)

| What You Want to Add | File to Edit | Symbol / Section to Update |
| --- | --- | --- |
| New LaTeX symbol or function | `Rendering/Latex/LatexSymbolTable.cs` | `Symbols`, `BigOperators`, `NamedFunctions`, `BlackboardBold` |
| New LaTeX AST construct / command | `Rendering/Latex/LatexAst.cs`, `LatexParser.cs`, `LatexMathRenderer.cs` | `LatexNode`, `ParseCommand()`, `MeasureNode()` |
| New Syntax Guide how-to entry | `Rendering/SyntaxGuideSubMenuControl.cs` | `AllEntries` |
| New Markdown segment, block, or image renderer | `Rendering/Markdown/MarkdownPreviewControl.cs` | `BuildEditorSegments()`, `RenderParsedBlock()`, `AppendInline()`, `RenderMarkdownImageCard()` |
| Editor `H1`–`H5` section folding / gutter rendering | `Services/EditorFoldManager.cs`, `Rendering/FoldableEditorControl.cs` | `DetectSections()`, `GetGutterLines()`, `HeadingFoldGutterControl.Render()` |
| Theme palette fields or `dark.json`/`light.json` options | `Services/ThemeConfigService.cs`, `dark.json`, `light.json` | `ThemePalette`, `ThemeConfigService.Load()`, `LoadPaletteFromFile()` |
| New Drawing tool, cursor preview, or right-click sub-menu option | `Drawing/DrawingModels.cs`, `DrawingCanvasControl.cs`, `DrawingWorkspaceControl.cs` | `DrawingToolKind`, `OnPointerPressed()`, `DrawMousePreview()`, `BuildContextSubMenuPopup()` |
| New Canvas background pattern | `Drawing/DrawingModels.cs`, `DrawingCanvasControl.cs`, `DrawingWorkspaceControl.cs` | `CanvasBackgroundKind`, `DrawBackgroundPattern()` |
| New closable Workspace View or unified mode | `Services/ViewLayoutState.cs`, `MainWindow.axaml.cs` | `WorkspaceViewKind`, `SetUnifiedWindowMode()`, `RefreshViewLayout()`, `BuildViewTabRibbon()` |
| Automated unit/self tests | `Services/SelfTestRunner.cs` | `RunAll()` |
| Pasted image & canvas folder resolution | `Services/DocumentAssetService.cs` | `SavePastedBitmap()`, `CreateSavedCanvas()`, `ResolveImagePath()`, `ResolveCanvasPath()` |
