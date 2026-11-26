using System;
using System.IO;
using NoteDraw.Drawing;

namespace NoteDraw.Services
{
    /// <summary>
    /// Seeds starter tutorial notes, formulas, and vector diagrams for NoteDraw's default SampleVault.
    /// </summary>
    internal static class SampleVaultSeeder
    {
        public static void CreateSampleFiles(string vaultPath, Action<string, string> writeFile)
        {
            var welcomeDiagram = CreateStarterDiagram();
            string welcomeNote =
@"# Welcome to NoteDraw

#welcome #notedraw #tutorial

**NoteDraw** combines Obsidian-style **Markdown**, real-time **LaTeX math**, and **vector hand drawing** in one workspace.

## Quick Tips
- [x] **Closable Views**: Click `×` on any panel header to close it, and restore it anytime from the **View** tab in the top toolbar.
- [x] **Live LaTeX Preview**: Place your cursor inside the ```` ```latex ```` block below and edit it to see the formula render as you type!
- [ ] **Inline Hand Drawing**: Sketch directly on the interactive canvas embedded below, or open [[Sketchpad Demo.ndraw]].
- [ ] Explore more equations in [[LaTeX & Math Showcase]].

## Live LaTeX Block (Try editing inside!)

```latex
\int_0^\infty e^{-x^2}\,dx = \frac{\sqrt{\pi}}{2}
```

Inline math also renders inside sentences, such as Euler's identity $e^{i\pi} + 1 = 0$ and the quadratic formula $x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}$.

## Inline Hand Drawing Canvas

```drawing
" + welcomeDiagram.ToJson() + @"
```
";
            writeFile(Path.Combine(vaultPath, "Welcome to NoteDraw.md"), welcomeNote);

            string mathNote =
@"# LaTeX & Math Showcase

#math #calculus #algebra

Click inside any ```` ```latex ````, ```` ```math ````, `$$...$$`, or `$...$` block in the editor to trigger the **Live LaTeX Code Preview** below the editor.

## Fenced LaTeX Block (Obsidian Style)
```latex
\sum_{n=1}^{\infty} \frac{1}{n^2} = \frac{\pi^2}{6}
```

## Fenced Math Block
```math
f(x) = \begin{cases}
x^2 & x \ge 0 \\
-x & x < 0
\end{cases}
```

## Display Math (`$$...$$`)
$$
A = \begin{pmatrix}
\cos\theta & -\sin\theta \\
\sin\theta & \cos\theta
\end{pmatrix}
$$

## Vectors, Roots & Accents
- Gradient vector: $\nabla f = \left( \frac{\partial f}{\partial x}, \frac{\partial f}{\partial y} \right)$
- Unit vector & mean: $\hat{u} = \frac{\vec{v}}{\|\vec{v}\|}, \quad \bar{x} = \frac{1}{N}\sum_{i=1}^N x_i$
- $n$-th root: $\sqrt[n]{a^n + b^n}$

| Expression | Meaning |
| --- | --- |
| $\forall x \in \mathbb{R}$ | Universal quantifier |
| $\exists \delta > 0$ | Existential quantifier |
| $A \subseteq B$ | Subset relation |
";
            writeFile(Path.Combine(vaultPath, "LaTeX & Math Showcase.md"), mathNote);

            var sketchDoc = new DrawingDocument
            {
                Background = CanvasBackgroundKind.Dotted,
                Strokes =
                {
                    new DrawingStroke
                    {
                        Tool = DrawingToolKind.Rectangle,
                        ColorHex = "#3B82F6",
                        Thickness = 3,
                        Points = { new(60, 50), new(300, 170) }
                    },
                    new DrawingStroke
                    {
                        Tool = DrawingToolKind.TextLabel,
                        ColorHex = "#3B82F6",
                        TextContent = "\\frac{dy}{dx} = ky",
                        IsLatex = true,
                        Points = { new(115, 92) }
                    },
                    new DrawingStroke
                    {
                        Tool = DrawingToolKind.Arrow,
                        ColorHex = "#10B981",
                        Thickness = 3,
                        Points = { new(305, 110), new(420, 110) }
                    },
                    new DrawingStroke
                    {
                        Tool = DrawingToolKind.Ellipse,
                        ColorHex = "#A855F7",
                        Thickness = 3,
                        Points = { new(430, 55), new(620, 165) }
                    },
                    new DrawingStroke
                    {
                        Tool = DrawingToolKind.TextLabel,
                        ColorHex = "#A855F7",
                        TextContent = "y(t) = Ce^{kt}",
                        IsLatex = true,
                        Points = { new(468, 95) }
                    }
                }
            };
            writeFile(Path.Combine(vaultPath, "Sketchpad Demo.ndraw"), sketchDoc.ToJson());

            string quickRefNote =
@"# Quick Reference

#reference #shortcuts

- Press `?` in the top toolbar or `F1` to open the Markdown & LaTeX Syntax Guide.
- Paste images directly with `Ctrl+V` (saved into `image/`) or insert canvases saved into `canvas/`.
";
            writeFile(Path.Combine(vaultPath, "Notes", "Quick Reference.md"), quickRefNote);
        }

        private static DrawingDocument CreateStarterDiagram() => new()
        {
            Background = CanvasBackgroundKind.Grid,
            Strokes =
            {
                new DrawingStroke
                {
                    Tool = DrawingToolKind.Rectangle,
                    ColorHex = "#3B82F6",
                    Thickness = 2.5,
                    Points = { new(48, 48), new(228, 138) }
                },
                new DrawingStroke
                {
                    Tool = DrawingToolKind.TextLabel,
                    ColorHex = "#3B82F6",
                    TextContent = "Markdown + LaTeX",
                    IsLatex = false,
                    Points = { new(72, 82) }
                },
                new DrawingStroke
                {
                    Tool = DrawingToolKind.Arrow,
                    ColorHex = "#10B981",
                    Thickness = 3,
                    Points = { new(238, 93), new(338, 93) }
                },
                new DrawingStroke
                {
                    Tool = DrawingToolKind.Ellipse,
                    ColorHex = "#F59E0B",
                    Thickness = 2.5,
                    Points = { new(348, 44), new(548, 144) }
                },
                new DrawingStroke
                {
                    Tool = DrawingToolKind.TextLabel,
                    ColorHex = "#F59E0B",
                    TextContent = "E = mc^2",
                    IsLatex = true,
                    Points = { new(410, 80) }
                }
            }
        };
    }
}
