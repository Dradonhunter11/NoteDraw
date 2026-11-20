# NoteDraw Testing Guidelines for Agents (`TEST.md`)

This document defines the mandatory test authoring and maintenance rules for **NoteDraw**.
All automated tests live in [Services/SelfTestRunner.cs](file:///c:/Users/alexa/Documents/Github/NoteDraw/Services/SelfTestRunner.cs) and execute via:

```powershell
dotnet run --project NoteDraw.csproj -- --self-test
```

---

## Core Principle: One Test Per Feature, Never One Test Per Branch

When implementing a branch with multiple fixes or features, **never** combine all changes into one giant test method (for example, `TestFeatureAFeatureBAndBugfixC`).

Why this rule exists:
- **Blast-radius isolation**: If one feature changes or is deprecated later, unrelated tests stay green instead of breaking a monolithic branch test.
- **Clear diagnostics**: A failure immediately names the exact broken capability instead of stopping partway through a 150-line script.
- **Order independence**: Each feature test sets up only what it needs and cleans up after itself.

---

## Mandatory Rules for Agents

### Rule 1 — One Feature = One Test Method
- Create **one dedicated test method** for each individual feature, behavior, or bugfix added in a branch.
- If a feature has distinct, separable sub-behaviors (for example, `Copy & Paste`, `Cut & Tab Remapping`, `Multi-Delete`, and `Context Menu` in Vault Explorer), write separate test methods for each behavior.
- **Warning sign**: If a test name contains multiple unrelated nouns joined by `And` or exceeds ~60 lines covering multiple sections (`// 1. ... // 2. ... // 3. ...` for different subsystems), split it.

### Rule 2 — Naming Conventions
- Register every test in `SelfTestRunner.RunAll()` with the format:
  ```csharp
  ("<Component>: <single behavior under test>", Test<Component><SpecificFeature>)
  ```
- Examples:
  - `("Vault Explorer: multi-file Ctrl+Click, Shift+Click & Ctrl+A selection", TestVaultExplorerMultiFileSelection)`
  - `("DocumentAssetService: pasted image storage in <exe>/image & tight bitmap bounds", TestPastedImageStorageAndTightCanvasBitmapBounds)`
  - `("GitService: repository init, status, commit, branch & diff", TestGitServiceRepositoryWorkflow)`

### Rule 3 — Strict Isolation & Deterministic Cleanup
- **Never share state across tests**: Each test must allocate its own unique directory in `Path.GetTempPath()` (using `Guid.NewGuid().ToString("N")`).
- **Always restore shared config/asset files**: Tests that touch `<exe>/canvas/app_options.json`, `<exe>/canvas/remembered_colors.json`, `<exe>/image`, or `<exe>/canvas` must back up and restore prior state in `finally` (use shared helpers such as `SaveAndRestoreFile(...)` and `WithTempVault(...)` in `SelfTestRunner`).
- **Zero execution-order dependency**: Every test must pass both when executed individually and in any order within `RunAll()`.

### Rule 4 — Actionable Failure Messages
- Every `Assert(condition, message)` must state **what failed** and include **actual vs. expected** values where applicable (counts, strings, paths, dimensions):
  ```csharp
  Assert(pasted.Count == 2, $"Expected 2 pasted files, got {pasted.Count}.");
  ```

### Rule 5 — Maintaining & Evolving Existing Tests
- When modifying a single feature, update only its dedicated test.
- If you encounter a legacy test in `SelfTestRunner.cs` that still bundles multiple unrelated features together and needs updates, split it into independent per-feature tests.

### Rule 6 — Pre-Merge Verification Checklist
Before committing and merging any feature branch onto `master`:
1. Build cleanly with zero warnings or errors:
   ```powershell
   dotnet build NoteDraw.csproj
   ```
2. Run the full headless self-test suite and verify 100% pass rate:
   ```powershell
   dotnet run --project NoteDraw.csproj -- --self-test
   ```
3. Run a short GUI smoke test on the built executable (`bin\Debug\net10.0\NoteDraw.exe` reaching idle state without crashing) and confirm clean `git status -sb`.

---

## Good vs. Bad Test Examples

### Bad: Monolithic Branch Test (DO NOT DO THIS)
```csharp
// Tests 4 unrelated features from the same PR inside one fragile method:
// If clipboard behavior changes later, scrollbar and single-vault coverage stop running too.
private static void TestFileExplorerMultiSelectClipboardScrollbarsAndSingleVault()
{
    // 1. Check ScrollViewer.AllowAutoHide
    // 2. Check Ctrl+Click / Shift+Click multi-select
    // 3. Check Copy / Cut / Paste / Delete
    // 4. Check OpenVaultAtPath single-vault isolation
}
```

### Good: Atomic Single-Feature Tests (DO THIS)
```csharp
("ScrollViewer & App Styles: non-overlapping slim scrollbars (AllowAutoHide=False)", TestNonOverlappingSlimScrollbars),
("Vault Explorer: multi-file Ctrl+Click, Shift+Click & Ctrl+A selection", TestVaultExplorerMultiFileSelection),
("Vault Explorer: copy & paste files into same folder and subfolders", TestVaultExplorerCopyAndPasteFiles),
("MainWindow & VaultService: single-vault enforcement without starter note pollution", TestSingleVaultIsolationAndNoStarterPollution)
```

Each method tests one cohesive behavior in 15–45 lines, isolates its temporary vault via `WithTempVault(...)`, and produces an exact regression signal if broken.
