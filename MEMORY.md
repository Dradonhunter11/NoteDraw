# NoteDraw — Agent Memory (`MEMORY.md`)

Persistent architectural notes, decisions, and platform constraints for future agents working on **NoteDraw**.

## Project & User Decisions
- **Single Design File**: Only keep one `DESIGN.md` at the repository root (`c:\Users\alexa\Documents\Github\NoteDraw\DESIGN.md`). Do not create a duplicate `Design/Design.md`.
- **Core UX Approach**: Approach A — Obsidian-style local folder Vault containing `.md` notes (Markdown + LaTeX + embedded ```` ```drawing ```` blocks) and standalone `.ndraw` vector drawing files.
- **Closable Views & `View` Toolbar Tab**: Every panel (`Vault Explorer`, `Outline & Tags`, `Formatting Sub-Toolbar`, `Editor Pane`, `Live LaTeX Code Preview`, `Document Preview Pane`, `Status Bar`) must be independently closable via a header `×` button and restorable from the **`View`** tab in the top toolbar (plus a `Reset All Views` action).
- **Live LaTeX Preview While Typing**: While typing inside Obsidian-style ```` ```latex ````, ```` ```math ````, `$$...$$`, or `$...$` blocks, the live preview must render the formula immediately without waiting for manual refresh or breaking on half-typed expressions.

## Tech Stack & Dependency Notes
- **Runtime & UI**: `.NET 10.0` (`net10.0`), `Avalonia 12.0.4`, `Avalonia.Desktop 12.0.4`, `Avalonia.Themes.Fluent 12.0.4`, `Avalonia.Fonts.Inter 12.0.4`.
- **Avalonia 12 API Notes**:
  - `TextBox` uses `PlaceholderText` (not obsolete `Watermark`).
  - `IClipboard.SetTextAsync` is an extension method in `Avalonia.Input.Platform` (`ClipboardExtensions`).
  - `ForegroundProperty` on `Button` resolves via `Button.ForegroundProperty` (`TemplatedControl.ForegroundProperty`).
- **Approved Extra Library (Rule 2)**: `Markdig` (`1.6.0`) for Markdown + Math + Pipe Tables + Task Lists AST parsing.
- **SkiaSharp Compatibility Warning**: `Avalonia.Skia 12.0.4` pulls in `SkiaSharp 3.119.4`. Do **NOT** install `CSharpMath.SkiaSharp` (compiled against SkiaSharp 2.80, throws `MissingMethodException` for `SKPaint.TextSize` on SkiaSharp 3.x). LaTeX rendering is implemented natively on Avalonia `DrawingContext` in `Rendering/Latex/`.
- **Avalonia 12 Compiled Bindings**: `NoteDraw.csproj` sets `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>`. Prefer strongly-typed C# UI construction or explicit `x:DataType` bindings to avoid XAML compiled binding issues.

## Key Implementation Invariants
- **Inline Drawing Sync Guard**: When `MarkdownPreviewControl.InlineDrawingUpdated` updates the Markdown source in `MainWindow`, `_suppressPreviewRebuild = true` must be set so the active embedded `DrawingWorkspaceControl` is not destroyed/recreated mid-stroke.
- **Headless Self-Tests**: `dotnet run --project NoteDraw.csproj -- --self-test` initializes Avalonia platform services via `BuildAvaloniaApp().SetupWithoutStarting()` and runs `SelfTestRunner.RunAll()`.

## Git & Documentation Conventions
- Create a dedicated `feature/<name>` branch for each feature, commit completed work on that branch, and merge (`--no-ff`) into `master`.
- Maintenance and extension guides live under `docs/Generated/` (Rule 5).
- Always update `AGENT.md` `# Project Structure` when adding directories or major modules.
