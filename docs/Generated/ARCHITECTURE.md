# NoteDraw Architecture Overview

> Generated maintenance documentation per `AGENT.md` Rule 5.

## High-Level Design
NoteDraw is a local-first C# / Avalonia UI (`net10.0`, `Avalonia 12.0.4`) note-taking application organized around three core subsystems:

1. **Vault & State Management (`Services/`)**:
   - `VaultService`: Reads/writes `.md` notes and `.ndraw` drawings inside the active Vault folder, initializes `SampleVault` on first launch, extracts headings/tags/wiki-links, and resolves `[[WikiLink]]` targets.
   - `ThemeConfigService`: Loads and saves separate `dark.json` and `light.json` palette/typography files (`ThemePalette.CreateClassicDark()`, `ThemePalette.CreateClassicLight()`, `LoadPaletteFromFile`, `SavePaletteToFile`) with safe hex parsing (`ParseColor` / `ParseBrush`) and `#FFFFFF` white drawing canvas defaults.
   - `EditorFoldManager`: Detects `H1`–`H5` Markdown sections (`DetectSections`), manages per-heading fold/unfold state (`ToggleFoldAtLine`, `FoldSectionAtLine`, `UnfoldSectionAtLine`, `UnfoldAll`), computes gutter metadata (`GetGutterLines`), and reconstructs hidden content (`GetFullUnfoldedText`).
   - `ViewLayoutState`: Centralizes open/closed state for every workspace view (`VaultExplorer`, `OutlineTags`, `FormattingToolbar`, `EditorPane`, `LiveLatexPreview`, `PreviewPane`, `StatusBar`) and coordinates view visibility toggles with the top toolbar **`View`** tab.
   - `SelfTestRunner`: Headless-capable verification suite executed via `dotnet run -- --self-test`.

2. **Native Vector LaTeX Engine (`Rendering/Latex/`)**:
   - `LatexSymbolTable`: Static lookup tables for symbols, big operators, named functions, blackboard bold characters, accents, and spacing.
   - `LatexAst` & `LatexParser`: Fault-tolerant recursive-descent parser returning `LatexParseResult` (AST + non-blocking diagnostics while typing).
   - `LatexMathRenderer` & `LatexMathControl`: Recursive `MathBox` box-layout engine drawing fractions, roots, matrices, scripts, accents, and scalable delimiters directly onto Avalonia `DrawingContext`.
   - `LatexLiveDetector`: Caret-aware scanner detecting ```` ```latex ````, ```` ```math ````, `$$...$$`, and `$...$` contexts as the user types.

3. **Markdown, Foldable Editor & Vector Hand Drawing (`Rendering/` & `Drawing/`)**:
   - `FoldableEditorControl`: Pairs the Markdown `TextBox` (`InnerTextBox`) with `HeadingFoldGutterControl` and a folded-sections banner for `H1`–`H5` gutter fold toggles (`▼` / `▶`) while keeping full unfolded source synchronized via `FullText`.
   - `MarkdownPreviewControl`: Uses `Markdig` (`1.6.0`) to walk the Markdown AST and emit native Avalonia controls, interactive checklists, collapsible `H1`–`H5` headings (`TogglePreviewHeadingFold`), Obsidian-style Math cards, clickable `[[WikiLinks]]` and `#tags`, in-place block/LaTeX editing (`ReplaceLineRange`, `DocumentEditedFromPreview`), a bottom quick-append card, and click-to-open inline editors for ```` ```drawing ````, `![[*.ndraw]]`, and Markdown images (`OpenInlineImageEditor`, `RenderMarkdownImageCard`).
   - `DrawingModels`, `DrawingCanvasControl`, & `DrawingWorkspaceControl`: White `#FFFFFF` vector drawing engine with live mouse cursor preview (`DrawMousePreview`), smooth quadratic-Bezier pen, highlighter, freehand brush `Eraser`, geometric shapes, text/LaTeX labels, pan/zoom, right-click floating sub-menu (`ColorSpectrumPickerControl` 2D HSV + RGB/Hex selector + quick tools), Undo/Redo, PNG export (`RenderTargetBitmap`), and SVG export.

4. **Workspace Shell (`MainWindow.axaml`, `MainWindow.axaml.cs`)**:
   - Hosts the top header (including `Unified Live View`, `Raw Source Mode`, `dark.json`, `light.json`, `Load Theme...`), 4-tab ribbon (`Edit & Format`, `View`, `LaTeX Assistant`, `Drawing Tools`), left sidebar (`Vault Explorer` + `Outline & Tags`), single unified 1-window Editor & Preview (`_previewContentHost`) switching cleanly between live editable Markdown/Image preview (`_markdownPreview`) and raw foldable source (`_foldableEditor`) with live LaTeX preview docked below, status bar, Quick Switcher (`Ctrl+P`), and separate `dark.json`/`light.json` theme hot-reload.

---

## Maintainer Extension Index (Where to Add Things in the Future)

| What You Want to Add | File to Edit | Symbol / Section to Update |
| --- | --- | --- |
| New LaTeX symbol or function | `Rendering/Latex/LatexSymbolTable.cs` | `Symbols`, `BigOperators`, `NamedFunctions`, `BlackboardBold` |
| New LaTeX AST construct / command | `Rendering/Latex/LatexAst.cs`, `LatexParser.cs`, `LatexMathRenderer.cs` | `LatexNode`, `ParseCommand()`, `MeasureNode()` |
| New LaTeX Assistant snippet button | `MainWindow.axaml.cs` | `BuildLatexAssistantRibbon()` |
| New Markdown block or inline renderer / preview editor | `Rendering/Markdown/MarkdownPreviewControl.cs` | `RenderBlock()`, `AppendInline()`, `WrapEditableBlock()`, `RenderMarkdownImageCard()` |
| Editor `H1`–`H5` section folding / gutter rendering | `Services/EditorFoldManager.cs`, `Rendering/FoldableEditorControl.cs` | `DetectSections()`, `GetGutterLines()`, `HeadingFoldGutterControl.Render()` |
| Theme palette fields or `dark.json`/`light.json` options | `Services/ThemeConfigService.cs`, `dark.json`, `light.json` | `ThemePalette`, `ThemeConfigService.Load()`, `LoadPaletteFromFile()` |
| New Drawing tool, cursor preview, or right-click sub-menu option | `Drawing/DrawingModels.cs`, `DrawingCanvasControl.cs`, `DrawingWorkspaceControl.cs` | `DrawingToolKind`, `OnPointerPressed()`, `DrawMousePreview()`, `BuildContextSubMenuPopup()` |
| New Canvas background pattern | `Drawing/DrawingModels.cs`, `DrawingCanvasControl.cs`, `DrawingWorkspaceControl.cs` | `CanvasBackgroundKind`, `DrawBackgroundPattern()` |
| New closable Workspace View or unified mode | `Services/ViewLayoutState.cs`, `MainWindow.axaml.cs` | `WorkspaceViewKind`, `SetUnifiedWindowMode()`, `RefreshViewLayout()`, `BuildViewTabRibbon()` |
| Automated unit/self tests | `Services/SelfTestRunner.cs` | `RunAll()` |
