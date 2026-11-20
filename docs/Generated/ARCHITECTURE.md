# NoteDraw Architecture Overview

> Generated maintenance documentation per `AGENT.md` Rule 5.

## High-Level Design
NoteDraw is a local-first C# / Avalonia UI (`net10.0`, `Avalonia 12.0.4`) note-taking application organized around three core subsystems:

1. **Vault & State Management (`Services/`)**:
   - `VaultService`: Reads/writes `.md` notes and `.ndraw` drawings inside the active Vault folder, initializes `SampleVault` on first launch, extracts headings/tags/wiki-links, and resolves `[[WikiLink]]` targets.
   - `ViewLayoutState`: Centralizes open/closed state for every workspace view (`VaultExplorer`, `OutlineTags`, `FormattingToolbar`, `EditorPane`, `LiveLatexPreview`, `PreviewPane`, `StatusBar`) and drives synchronization between panel `×` close buttons and the top toolbar **`View`** tab.
   - `SelfTestRunner`: Headless-capable verification suite executed via `dotnet run -- --self-test`.

2. **Native Vector LaTeX Engine (`Rendering/Latex/`)**:
   - `LatexSymbolTable`: Static lookup tables for symbols, big operators, named functions, blackboard bold characters, accents, and spacing.
   - `LatexAst` & `LatexParser`: Fault-tolerant recursive-descent parser returning `LatexParseResult` (AST + non-blocking diagnostics while typing).
   - `LatexMathRenderer` & `LatexMathControl`: Recursive `MathBox` box-layout engine drawing fractions, roots, matrices, scripts, accents, and scalable delimiters directly onto Avalonia `DrawingContext`.
   - `LatexLiveDetector`: Caret-aware scanner detecting ```` ```latex ````, ```` ```math ````, `$$...$$`, and `$...$` contexts as the user types.

3. **Markdown & Vector Hand Drawing (`Rendering/Markdown/` & `Drawing/`)**:
   - `MarkdownPreviewControl`: Uses `Markdig` (`1.6.0`) to walk the Markdown AST and emit native Avalonia controls, interactive checklists, Obsidian-style Math cards, clickable `[[WikiLinks]]` and `#tags`, and inline `DrawingWorkspaceControl` instances for ```` ```drawing ```` and `![[*.ndraw]]` embeds.
   - `DrawingModels`, `DrawingCanvasControl`, & `DrawingWorkspaceControl`: JSON-backed vector drawing engine with smooth quadratic-Bezier ink, highlighter, eraser, geometric shapes, text/LaTeX labels, pan/zoom, Undo/Redo, PNG export (`RenderTargetBitmap`), and SVG export.

4. **Workspace Shell (`MainWindow.axaml`, `MainWindow.axaml.cs`)**:
   - Hosts the top header, 4-tab ribbon (`Edit & Format`, `View`, `LaTeX Assistant`, `Drawing Tools`), left sidebar (`Vault Explorer` + `Outline & Tags`), multi-tab editor + live LaTeX preview + document preview, status bar, Quick Switcher (`Ctrl+P`), and Dark/Light theme switching.

---

## Maintainer Extension Index (Where to Add Things in the Future)

| What You Want to Add | File to Edit | Symbol / Section to Update |
| --- | --- | --- |
| New LaTeX symbol or function | `Rendering/Latex/LatexSymbolTable.cs` | `Symbols`, `BigOperators`, `NamedFunctions`, `BlackboardBold` |
| New LaTeX AST construct / command | `Rendering/Latex/LatexAst.cs`, `LatexParser.cs`, `LatexMathRenderer.cs` | `LatexNode`, `ParseCommand()`, `MeasureNode()` |
| New LaTeX Assistant snippet button | `MainWindow.axaml.cs` | `BuildLatexAssistantRibbon()` |
| New Markdown block or inline renderer | `Rendering/Markdown/MarkdownPreviewControl.cs` | `RenderBlock()`, `AppendInline()` |
| New Drawing tool or shape | `Drawing/DrawingModels.cs`, `DrawingCanvasControl.cs`, `DrawingWorkspaceControl.cs` | `DrawingToolKind`, `OnPointerPressed()`, `DrawStroke()`, `AddToolButton()` |
| New Canvas background pattern | `Drawing/DrawingModels.cs`, `DrawingCanvasControl.cs`, `DrawingWorkspaceControl.cs` | `CanvasBackgroundKind`, `DrawBackgroundPattern()` |
| New closable Workspace View | `Services/ViewLayoutState.cs`, `MainWindow.axaml.cs` | `WorkspaceViewKind`, `RefreshViewLayout()`, `BuildViewTabRibbon()` |
| Automated unit/self tests | `Services/SelfTestRunner.cs` | `RunAll()` |
