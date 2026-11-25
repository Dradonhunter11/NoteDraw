using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NoteDraw.Rendering.Latex;
using NoteDraw.Services;

namespace NoteDraw.Rendering
{
    public sealed record SyntaxGuideEntry(
        string Category,
        string Title,
        string SyntaxToType,
        string Explanation,
        string? LatexPreviewSample = null);

    /// <summary>
    /// Searchable read-only Markdown & LaTeX how-to reference sub-menu.
    /// Shows users how to write Markdown and LaTeX syntax themselves rather than inserting templates.
    /// </summary>
    public sealed class SyntaxGuideSubMenuControl : Border
    {
        public static IReadOnlyList<SyntaxGuideEntry> AllEntries { get; } = new List<SyntaxGuideEntry>
        {
            // Markdown
            new("Markdown", "Headings 1–5 (H1–H5)",
                "# Heading 1   ## Heading 2   ### Heading 3   #### Heading 4   ##### Heading 5",
                "Start a line with 1 to 5 # characters followed by a space. Click ▼/▶ on H1–H5 to fold or unfold sections."),
            new("Markdown", "Bold, Italic & Strikethrough",
                "**bold text**   *italic text*   ~~strikethrough~~",
                "Wrap text in double asterisks for bold, single asterisks for italic, or double tildes for strikethrough."),
            new("Markdown", "Inline & Fenced Code (Syntax Highlighted)",
                "`inline code`\n\n```csharp\npublic static void Main() => Console.WriteLine(\"Hi!\");\n```\n\n~~~python\ndef greet(name):\n    return f\"Hello {name}\"\n~~~",
                "Use single backticks for inline code or triple backticks (```) / tildes (~~~) with a language tag (csharp, python, js, ts, c, cpp, java, kotlin, rust, go, json, html, xml, axaml, css, sql, bash, pwsh, yaml) for syntax highlighting."),
            new("Markdown", "Lists & Checklists",
                "- bullet item\n1. numbered item\n- [ ] todo task\n- [x] completed task",
                "Start lines with -, 1., - [ ], or - [x] to create bulleted lists, numbered lists, or interactive checklists."),
            new("Markdown", "Blockquotes & Horizontal Rules",
                "> quoted line\n\n---",
                "Prefix a line with > to create a quote block, or type three dashes --- on a blank line for a divider."),
            new("Markdown", "WikiLinks & Tags",
                "[[Note Name]]   [[Note Name|Custom Label]]   #tag_name",
                "Use double brackets [[...]] to link across vault notes (with optional |Alias) and #tag for filterable tags."),
            new("Markdown", "Pipe Tables",
                "| Column A | Column B |\n| --- | --- |\n| Value 1 | Value 2 |",
                "Separate columns with | and add a header separator row using --- under the header row."),

            // Images & Drawings
            new("Images & Drawings", "Inline Drawing Canvas",
                "```drawing\n{\"strokes\":[]}\n```",
                "Create a fenced ```drawing block (click the rendered image preview in the document to open the Drawing Editor)."),
            new("Images & Drawings", "Embed Vault Drawing (.ndraw)",
                "![[Sketchpad Demo.ndraw]]",
                "Embed a standalone .ndraw vector file in your note; click the rendered image card directly to edit strokes."),
            new("Images & Drawings", "Markdown / Vault Image",
                "![Caption](path/to/image.png)   or   ![[image.png]]",
                "Reference any local image file cleanly; click the rendered image to edit its caption/path or sketch in place."),

            // LaTeX Math
            new("LaTeX Math", "Inline Math",
                "$E = mc^2$   or   $\\alpha + \\beta = \\gamma$",
                "Wrap short math formulas in single dollar signs $...$ inside any Markdown sentence.",
                "E = mc^2"),
            new("LaTeX Math", "Display Math Block",
                "$$\n\\sum_{i=1}^n i = \\frac{n(n+1)}{2}\n$$\n\n```latex\n\\int_0^\\infty e^{-x^2}\\,dx\n```",
                "Wrap centered multi-line math blocks in $$ ... $$ or Obsidian-style fenced ```latex / ```math blocks.",
                "\\sum_{i=1}^n i = \\frac{n(n+1)}{2}"),
            new("LaTeX Math", "Fractions & Binomials",
                "\\frac{a}{b}   \\dfrac{x+1}{y-1}   \\binom{n}{k}",
                "Use \\frac{num}{den} (or \\dfrac for full size) for fractions, and \\binom{n}{k} for binomial coefficients.",
                "\\frac{a+b}{c} = \\binom{n}{k}"),
            new("LaTeX Math", "Roots, Powers & Subscripts",
                "\\sqrt{x}   \\sqrt[n]{x}   x^{2}   a_{i}   x_{i}^{2}",
                "Use \\sqrt{...} (with optional [n]) for radicals, ^ for superscripts, and _ for subscripts.",
                "\\sqrt[n]{x_{i}^{2} + y^{2}}"),
            new("LaTeX Math", "Integrals, Sums, Products & Limits",
                "\\int_{a}^{b} f(x)\\,dx   \\oint   \\sum_{i=1}^{n} a_i   \\prod_{i=1}^{n} x_i   \\lim_{x \\to \\infty} f(x)",
                "Combine big operators with _{lower}^{upper} bounds to render calculus integrals, summations, products, and limits.",
                "\\int_{a}^{b} f(x)\\,dx = \\lim_{n \\to \\infty} \\sum_{i=1}^{n} f(x_i)\\Delta x"),
            new("LaTeX Math", "Derivatives & Vector Calculus",
                "\\frac{\\partial f}{\\partial x}   \\nabla^2 u   \\dot{x}   \\ddot{x}",
                "Use \\partial for partial derivatives, \\nabla for gradient/Laplacian, and \\dot / \\ddot for time derivatives.",
                "\\frac{\\partial u}{\\partial t} = \\alpha \\nabla^2 u"),
            new("LaTeX Math", "Matrices (pmatrix / bmatrix / vmatrix)",
                "\\begin{pmatrix}\na & b \\\\\nc & d\n\\end{pmatrix}   \\begin{bmatrix} 1 & 0 \\\\ 0 & 1 \\end{bmatrix}",
                "Separate columns with & and rows with \\\\ inside pmatrix, bmatrix, or vmatrix environments.",
                "\\begin{pmatrix} a & b \\\\ c & d \\end{pmatrix}"),
            new("LaTeX Math", "Piecewise Cases & Aligned Equations",
                "\\begin{cases}\nx^2 & x \\ge 0 \\\\\n-x & x < 0\n\\end{cases}",
                "Use \\begin{cases}...\\end{cases} for piecewise definitions or \\begin{aligned}...\\end{aligned} for multi-line equations.",
                "|x| = \\begin{cases} x & x \\ge 0 \\\\ -x & x < 0 \\end{cases}"),
            new("LaTeX Math", "Greek Letters",
                "\\alpha, \\beta, \\gamma, \\theta, \\lambda, \\pi, \\sigma, \\omega, \\Delta, \\Sigma, \\Omega",
                "Write backslash + English Greek letter name (capitalize for uppercase symbols like \\Delta, \\Sigma, \\Omega).",
                "\\alpha + \\beta + \\theta = \\Delta \\Sigma"),
            new("LaTeX Math", "Relations, Logic & Number Sets",
                "\\le, \\ge, \\neq, \\approx, \\in, \\forall, \\exists, \\infty, \\to, \\Rightarrow, \\mathbb{R}, \\mathbb{Z}",
                "Use standard LaTeX relation/logic commands and \\mathbb{R}, \\mathbb{N}, \\mathbb{Z}, \\mathbb{Q}, \\mathbb{C}.",
                "\\forall x \\in \\mathbb{R},\\ x^2 \\ge 0"),
            new("LaTeX Math", "Accents, Bars & Boxed Highlight",
                "\\hat{x}, \\vec{v}, \\bar{x}, \\overline{AB}, \\underline{x}, \\boxed{x+1}",
                "Decorate variables with hats, vectors, overbars, underlines, or frame final results inside \\boxed{...}.",
                "\\hat{x} + \\vec{v} = \\boxed{\\overline{AB}}")
        };

        private static readonly string[] Categories = { "All", "Markdown", "LaTeX Math", "Images & Drawings" };

        private readonly TextBox _searchBox = new();
        private readonly Dictionary<string, Button> _categoryButtons = new();
        private readonly StackPanel _entriesStack = new();
        private readonly ScrollViewer _scrollViewer = new();
        private readonly TextBlock _headerTitle = new();
        private readonly TextBlock _countBadge = new();
        private readonly Button _closeBtn = new();

        private string _activeCategory = "All";
        private ThemePalette _palette = ThemePalette.CreateClassicDark();
        private bool _isDark = true;

        public int VisibleEntryCount { get; private set; }

        public event EventHandler? CloseRequested;

        public SyntaxGuideSubMenuControl(double maxListHeight = 460)
        {
            CornerRadius = new CornerRadius(8);
            BorderThickness = new Thickness(1);
            Padding = new Thickness(12, 10);

            _headerTitle.Text = "📖 Markdown & LaTeX Syntax Guide (How-To Reference)";
            _headerTitle.FontSize = 13;
            _headerTitle.FontWeight = FontWeight.Bold;
            _headerTitle.VerticalAlignment = VerticalAlignment.Center;

            _countBadge.FontSize = 11;
            _countBadge.VerticalAlignment = VerticalAlignment.Center;
            _countBadge.Margin = new Thickness(8, 0, 8, 0);

            _closeBtn.Content = "×";
            _closeBtn.FontSize = 14;
            _closeBtn.FontWeight = FontWeight.Bold;
            _closeBtn.Padding = new Thickness(7, 2);
            _closeBtn.CornerRadius = new CornerRadius(4);
            _closeBtn.Background = Brushes.Transparent;
            _closeBtn.BorderThickness = new Thickness(0);
            ToolTip.SetTip(_closeBtn, "Close Syntax Guide Sub-Menu");
            _closeBtn.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

            var topRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            topRow.Children.Add(_headerTitle);
            Grid.SetColumn(_countBadge, 1);
            topRow.Children.Add(_countBadge);
            Grid.SetColumn(_closeBtn, 2);
            topRow.Children.Add(_closeBtn);

            _searchBox.PlaceholderText = "Filter syntax (e.g. H1, checklist, fraction, matrix, integral, drawing)...";
            _searchBox.FontSize = 12;
            _searchBox.TextChanged += (_, _) => RebuildEntries();

            var categoryWrap = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2)
            };
            foreach (string cat in Categories)
            {
                string target = cat;
                var btn = new Button
                {
                    Content = target,
                    FontSize = 11,
                    Padding = new Thickness(8, 3),
                    Margin = new Thickness(0, 0, 6, 0),
                    CornerRadius = new CornerRadius(12)
                };
                btn.Click += (_, _) =>
                {
                    _activeCategory = target;
                    RebuildEntries();
                };
                _categoryButtons[target] = btn;
                categoryWrap.Children.Add(btn);
            }

            _entriesStack.Spacing = 6;
            _scrollViewer.MaxHeight = maxListHeight;
            _scrollViewer.Content = _entriesStack;

            var rootStack = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    topRow,
                    _searchBox,
                    categoryWrap,
                    _scrollViewer
                }
            };

            Child = rootStack;
            ApplyThemePalette(_palette, _isDark);
        }

        public void SetSearchFilter(string query)
        {
            _searchBox.Text = query ?? string.Empty;
            RebuildEntries();
        }

        public void SetCategoryFilter(string category)
        {
            if (Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
            {
                _activeCategory = category;
                RebuildEntries();
            }
        }

        public void FocusSearchBox() => _searchBox.Focus();

        public void ApplyThemePalette(ThemePalette palette, bool isDark)
        {
            _palette = palette ?? (isDark ? ThemePalette.CreateClassicDark() : ThemePalette.CreateClassicLight());
            _isDark = isDark;

            Background = ThemeConfigService.ParseBrush(_palette.PanelBackground, isDark ? "#252526" : "#FFFFFF");
            BorderBrush = ThemeConfigService.ParseBrush(_palette.BorderColor, isDark ? "#3C3C3C" : "#D4D4D4");
            _headerTitle.Foreground = ThemeConfigService.ParseBrush(_palette.TextColor, isDark ? "#E0E0E0" : "#1E1E1E");
            _countBadge.Foreground = ThemeConfigService.ParseBrush(_palette.MutedTextColor, isDark ? "#9E9E9E" : "#616161");

            RebuildEntries();
        }

        private void RebuildEntries()
        {
            var accent = ThemeConfigService.ParseBrush(_palette.AccentColor, _isDark ? "#0078D4" : "#0067C0");
            var accentText = ThemeConfigService.ParseBrush(_palette.AccentTextColor, _isDark ? "#4CC2FF" : "#005FB8");
            var accentTint = ThemeConfigService.ParseBrush(_palette.AccentTintColor, _isDark ? "#263B4D" : "#D6EBFF");
            var cardBg = ThemeConfigService.ParseBrush(_palette.CardBackground, _isDark ? "#2A2D2E" : "#F8F8F8");
            var codeBg = ThemeConfigService.ParseBrush(_palette.CodeBackground, _isDark ? "#181818" : "#F0F0F0");
            var borderBrush = ThemeConfigService.ParseBrush(_palette.BorderColor, _isDark ? "#3C3C3C" : "#D4D4D4");
            var textBrush = ThemeConfigService.ParseBrush(_palette.TextColor, _isDark ? "#E0E0E0" : "#1E1E1E");
            var mutedBrush = ThemeConfigService.ParseBrush(_palette.MutedTextColor, _isDark ? "#9E9E9E" : "#616161");

            foreach (var (cat, btn) in _categoryButtons)
            {
                bool active = string.Equals(cat, _activeCategory, StringComparison.OrdinalIgnoreCase);
                if (active)
                {
                    btn.Background = accent;
                    btn.Foreground = Brushes.White;
                }
                else
                {
                    btn.ClearValue(BackgroundProperty);
                    btn.ClearValue(Button.ForegroundProperty);
                }
            }

            string query = (_searchBox.Text ?? string.Empty).Trim();
            var filtered = AllEntries.Where(entry =>
            {
                if (!string.Equals(_activeCategory, "All", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(entry.Category, _activeCategory, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.IsNullOrEmpty(query))
                    return true;

                return entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       entry.SyntaxToType.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       entry.Explanation.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       entry.Category.Contains(query, StringComparison.OrdinalIgnoreCase);
            }).ToList();

            _entriesStack.Children.Clear();
            VisibleEntryCount = filtered.Count;
            _countBadge.Text = $"{filtered.Count} / {AllEntries.Count} items (read-only guide — type syntax directly)";

            if (filtered.Count == 0)
            {
                _entriesStack.Children.Add(new TextBlock
                {
                    Text = "No matching syntax guide entries.",
                    FontSize = 12,
                    Foreground = mutedBrush,
                    Margin = new Thickness(4, 8)
                });
                return;
            }

            foreach (var entry in filtered)
            {
                var catBadge = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1),
                    Background = accentTint,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = entry.Category,
                        FontSize = 10.5,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = accentText
                    }
                };

                var titleTb = new TextBlock
                {
                    Text = entry.Title,
                    FontSize = 12.5,
                    FontWeight = FontWeight.Bold,
                    Foreground = textBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var headerRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { catBadge, titleTb }
                };

                var explainTb = new TextBlock
                {
                    Text = entry.Explanation,
                    FontSize = 11.5,
                    Foreground = mutedBrush,
                    TextWrapping = TextWrapping.Wrap
                };

                var syntaxBox = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Background = codeBg,
                    BorderThickness = new Thickness(1),
                    BorderBrush = borderBrush,
                    Padding = new Thickness(8, 5),
                    Child = new SelectableTextBlock
                    {
                        Text = $"Type:  {entry.SyntaxToType}",
                        FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace"),
                        FontSize = 12,
                        Foreground = accentText,
                        TextWrapping = TextWrapping.Wrap
                    }
                };

                Control bodyControl = syntaxBox;
                if (!string.IsNullOrWhiteSpace(entry.LatexPreviewSample))
                {
                    var previewContainer = new Border
                    {
                        CornerRadius = new CornerRadius(5),
                        Background = codeBg,
                        BorderThickness = new Thickness(1),
                        BorderBrush = borderBrush,
                        Padding = new Thickness(10, 4),
                        VerticalAlignment = VerticalAlignment.Stretch,
                        Child = new LatexMathControl
                        {
                            LatexSource = entry.LatexPreviewSample,
                            FontSize = 15,
                            IsDisplayMode = false,
                            Foreground = textBrush,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center
                        }
                    };

                    var rowGrid = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        ColumnSpacing = 8
                    };
                    Grid.SetColumn(syntaxBox, 0);
                    Grid.SetColumn(previewContainer, 1);
                    rowGrid.Children.Add(syntaxBox);
                    rowGrid.Children.Add(previewContainer);
                    bodyControl = rowGrid;
                }

                _entriesStack.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background = cardBg,
                    BorderThickness = new Thickness(1),
                    BorderBrush = borderBrush,
                    Padding = new Thickness(10, 7),
                    Child = new StackPanel
                    {
                        Spacing = 4,
                        Children = { headerRow, explainTb, bodyControl }
                    }
                });
            }
        }
    }
}
