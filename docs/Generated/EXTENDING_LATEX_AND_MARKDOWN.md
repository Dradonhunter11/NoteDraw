# Extending LaTeX & Markdown Support

> Generated maintenance documentation per `AGENT.md` Rule 5.

This guide documents the exact places where maintainers can add new LaTeX commands, symbols, Assistant templates, and Markdown block types.

---

## 1. Adding New LaTeX Symbols, Functions, or Spacing
Edit [LatexSymbolTable.cs](file:///c:/Users/alexa/Documents/Github/NoteDraw/Rendering/Latex/LatexSymbolTable.cs):
- **Simple Greek / Operator / Arrow / Relation Symbols**: Add an entry to `LatexSymbolTable.Symbols` (key without leading `\`, value = Unicode glyph).
  - *Example*: `["triangleq"] = "\u225C"`
  - If the symbol should have relation spacing around it (`=`-like padding), also add the command name to `IsRelationCommand` in `LatexParser.cs`.
- **Big Operators** (like `\sum`, `\int`): Add to `LatexSymbolTable.BigOperators` with `(Glyph, StackLimits)`.
- **Upright Named Functions** (like `\sin`, `\log`): Add the command name to `LatexSymbolTable.NamedFunctions`.
- **Blackboard Bold Characters** (`\mathbb{...}`): Add entries to `LatexSymbolTable.BlackboardBold`.

---

## 2. Adding a New Structural LaTeX Command (AST + Layout)
1. **Define an AST node** (if existing nodes do not cover it) in [LatexAst.cs](file:///c:/Users/alexa/Documents/Github/NoteDraw/Rendering/Latex/LatexAst.cs) as a `sealed record` inheriting `LatexNode`.
2. **Parse the command** inside `LatexParser.ParseCommand()` in [LatexParser.cs](file:///c:/Users/alexa/Documents/Github/NoteDraw/Rendering/Latex/LatexParser.cs).
   - Always use `ParseRequiredArg(...)` or `ReadBraceLiteral()` so incomplete input while typing emits a non-blocking entry in `_diagnostics` instead of throwing.
3. **Measure & Render the node** inside `LatexMathRenderer.MeasureNode(...)` in [LatexMathRenderer.cs](file:///c:/Users/alexa/Documents/Github/NoteDraw/Rendering/Latex/LatexMathRenderer.cs).
   - Return a `MathBox(width, height, baseline, drawAction)` so baselines align properly with neighboring text.

---

## 3. Adding Snippets to the LaTeX Assistant Ribbon
Open [MainWindow.axaml.cs](file:///c:/Users/alexa/Documents/Github/NoteDraw/MainWindow.axaml.cs) and find `BuildLatexAssistantRibbon()`.
- Add a `(string Label, string Code)` tuple to the `snippets` array.
- Clicking the button inserts `Code` at the editor caret and immediately updates both the **Live LaTeX Code Preview** and **Document Preview**.

---

## 4. Extending Live LaTeX Block Detection
Open [LatexLiveDetector.cs](file:///c:/Users/alexa/Documents/Github/NoteDraw/Rendering/Latex/LatexLiveDetector.cs):
- `DetectFencedBlock(...)` controls which fenced code block languages trigger the Live LaTeX Preview (`"latex"`, `"math"`, `"tex"`).
- `DetectDisplayDollarBlock(...)` and `DetectInlineMathOnLine(...)` handle `$$...$$` and `$...$`.

---

## 5. Adding New Fenced Code Block Handlers or Markdown Elements
Open [MarkdownPreviewControl.cs](file:///c:/Users/alexa/Documents/Github/NoteDraw/Rendering/Markdown/MarkdownPreviewControl.cs):
- **Fenced Block Types** (e.g., callouts, custom diagrams): Add a branch inside `RenderFencedBlock(FencedCodeBlock fenced)` matching `fenced.Info`.
- **Markdig Block / Inline Types**: Add cases to `RenderBlock(Block block)` or `AppendInline(...)`.
- **New `[[...]]` / `#...` Inline Tokens**: Extend `InlineTokenRegex` and `AppendLiteralWithObsidianTokens(...)`.
