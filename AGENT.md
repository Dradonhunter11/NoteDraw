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
9. If test are needed to confirm results, you are allowed to do so. Unit test might be added by the users in the future.

# Project Structure
Update this code block section as the project goes.
```
AGENT.md
DESIGN.md
MEMORY.md
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
  EditorFoldManager.cs
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

AI Agent specific project structure:
**DESIGN.md :** Single authoritative design specification at the repository root (user-approved; do not duplicate into `Design/Design.md`). If `Design/Prototype/` exists, consult it and never delete its contents.
**AGENT.md**

# Github rule
1. For each feature done, you must commit to the github repository.
2. For each new feature, you must create a branch for it. 