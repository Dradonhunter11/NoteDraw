# Agent Operating Guide
Entry point for **any** agent working on this repository. Everything in this file must be read carefully before proceeding.

# Subagent Usage
As a student experiment to learn how agentic work, this project is meant to test the capacity of the agent reading this to create am application using a set amount of rule and respecting a design defined in `DESIGN.md`.

# Project Overview
- **Objective**: You are making an application that is made to take notes, it must support hand drawing, LaTeX and markdown (Similar to how obsidian have it). More details will be precised in `DESIGN.md`
- **Minimum Spec:**  MacOS, Linux and Windows 10 or 11 support
- **Tech Stack:** 
- C#
- Avalonia UI

# Quality Assurance
The user making the project is a professional QA and will test everything.

# Rules:
1. The application need to be user friendly to use.
2. If you need to add a library, before doing it, provide an explanation on why you need it and wait for user confirmation.
3. You are not allowed to delete any of the prototype content in the **Design/Prototype/** folder if it exists
4. If you come with any design deviation from **DESIGN.md**, please create a `.md` file under `Design/` with the design in there. You also must wait for the user approval before implementing it. You are allowed to generate a render if needed.
5. For any documentation, put it in `docs/Generated/`, you must document part where a user can fill or add stuff in the future for maintenance purpose.
6. If the user point a mistake, you must update this file to remind you (the agent) or any agent to avoid similar mistake in the future. 
7. For new feature you must make a branch before putting it in the master/main.
8. If there is anything important you (the agent) need to remember, put it in `MEMORY.md`
9. Follow `TEST.md` for all automated tests: write one small, isolated test per individual feature or bugfix (never one monolithic test per branch). Unit test might be added by the users in the future.

# Project Structure
Update this code block section as the project goes.
```
AGENT.md
DESIGN.md
MEMORY.md
TEST.md
NoteDraw.csproj
NoteDraw.slnx
dark.json
light.json
Program.cs
App.axaml / App.axaml.cs
MainWindow.axaml / MainWindow.axaml.cs
Drawing/
  DrawingModels.cs
  DrawingCanvasControl.cs
  DrawingWorkspaceControl.cs
Rendering/
  FoldableEditorControl.cs
  SyntaxGuideSubMenuControl.cs
  Latex/
    LatexAst.cs
    LatexLiveDetector.cs
    LatexMathControl.cs
    LatexMathRenderer.cs
    LatexParser.cs
    LatexSymbolTable.cs
  Markdown/
    MarkdownPreviewControl.cs
Services/
  DocumentAssetService.cs
  EditorFoldManager.cs
  GitService.cs
  SelfTestRunner.cs
  TextUndoManager.cs
  ThemeConfigService.cs
  VaultService.cs
  ViewLayoutState.cs
docs/
  Generated/
    ARCHITECTURE.md
    EXTENDING_LATEX_AND_MARKDOWN.md
    EXTENDING_DRAWING_TOOLS_AND_VIEWS.md
```

# Lessons Learned (Rule 6)
- **Never set fixed `LineHeight` on Avalonia `SelectableTextBlock` / `TextBlock` containing inline controls (`InlineUIContainer`)**: doing so clips the bottom half of wrapped lines and checklists. Set `MinHeight = 0` on inline `Button` pills (`[[WikiLink]]`, `#tag`) and use native `Run` with `FontFamily`/`Background` for inline code (`CodeInline`) so checklist rows render completely without clipping.
- **Normalize `$$...$$` and `\[...\]` math blocks before Markdig parsing**: Markdig's `MathBlockParser` requires opening and closing `$$` on their own lines; pre-normalize inline-started `$$\begin{...}` blocks and auto-wrap LaTeX Assistant snippets (`$...$` / ```` ```latex ````) when the caret is outside a math block.
- **Multi-character text & full drawing Undo/Redo**: Disable Avalonia's 1-character `TextBox` built-in undo (`IsUndoEnabled = false`), use `TextUndoManager` to coalesce typing/deletion bursts, and keep `DrawingCanvasControl` Undo/Redo (`Ctrl+Z` / `Ctrl+Y` + prominent toolbar buttons) synced with inline ```` ```drawing ```` histories across preview refreshes.
- **Obsidian Vault Embeds & Fault-Tolerant Vault Traversal**: In `BuildEditorSegments` and `ResolveImagePath`, classify `![[...]]` image extensions before checking `.ndraw` drawings, strip `|width`, `#section`, `<...>`, and `%20` wrappers cleanly, search vault subfolders for shortest-path Obsidian image names, never auto-create `.ndraw` files for non-`.ndraw` embeds (`TryLoadExistingCanvas`), sanitize canvas filenames before `Path.Combine`, and guard directory/file reads against dot-directories and locked files.
- **One Small Isolated Test Per Feature (Never One Big Test Per Branch — See `TEST.md`)**: Bundling multiple features or fixes from a branch into one large test method makes the test brittle whenever one feature changes or is modified later. Follow `TEST.md`: create one atomic, independent test method per individual feature/bugfix in `Services/SelfTestRunner.cs`, allocate unique temporary directories per test, restore any shared config files in `finally`, and avoid multi-feature branch mega-tests.
- **Prevent Scroll-Gesture Hijacking & Stroke Loss on Drawing Canvas**: In Avalonia `ScrollViewer`, `ScrollGestureRecognizer` captures `PointerType.Pen` and `PointerType.Touch` drags and routes `PenUp` away from child controls (`PointerCaptureLost`). Always call `e.PreventGestureRecognition()` inside `DrawingCanvasControl` / canvas-frame Tunnel handlers, block `ScrollGestureEvent` only when a press started inside the canvas, commit `_activeStroke` in both `OnPointerReleased` and `OnPointerCaptureLost`, and sync `seg.EndLine` / `ShiftSegmentsAfter` inside `UpdateInlineFenceMarkdownInPlace`.
- **Preserve Scroll Offset Across Preview Re-renders & Support Picture-Adjacent Cursors**: Clearing `_rootStack.Children` inside `MarkdownPreviewControl.RenderMarkdown` resets `ScrollViewer` extent/offset and `TextBox.Focus()` triggers `BringIntoViewOnFocusChange` / `RequestBringIntoViewEvent`, scrolling the clicked line to the bottom of the viewport. Disable `BringIntoViewOnFocusChange`, suppress `RequestBringIntoViewEvent` during activation, re-measure `_rootStack`, and restore `ScrollViewer.Offset` both synchronously and at `DispatcherPriority.Loaded`. Wrap collapsed picture cards with left/right hit-testable cursor zones (`WrapPictureCardWithAdjacentCursor`) so clicking beside a picture places a classic cursor right next to it instead of jumping to the end of the file.

AI Agent specific project structure:
**DESIGN.md :** Single authoritative design specification at the repository root (user-approved; do not duplicate into `Design/Design.md`). If `Design/Prototype/` exists, consult it and never delete its contents.
**TEST.md :** Mandatory testing guideline for all agents (`One Test Per Feature, Never One Test Per Branch`).
**AGENT.md**

# Github rule
1. For each feature done, you must commit to the github repository.
2. For each new feature, you must create a branch for it. 