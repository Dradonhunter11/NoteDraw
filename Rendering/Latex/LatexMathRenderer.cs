using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace NoteDraw.Rendering.Latex
{
    /// <summary>
    /// Measured mathematical layout box ready for vector drawing via Avalonia <see cref="DrawingContext"/>.
    /// </summary>
    public sealed class MathBox
    {
        public double Width { get; }
        public double Height { get; }
        public double Baseline { get; }
        private readonly Action<DrawingContext, Point> _draw;

        public MathBox(double width, double height, double baseline, Action<DrawingContext, Point> draw)
        {
            Width = Math.Max(0, width);
            Height = Math.Max(0, height);
            Baseline = Math.Clamp(baseline, 0, Math.Max(0, height));
            _draw = draw;
        }

        public static readonly MathBox Empty = new(0, 0, 0, static (_, _) => { });

        public void Draw(DrawingContext context, Point origin) => _draw(context, origin);
    }

    /// <summary>
    /// Native Avalonia vector LaTeX math layout and rendering engine.
    /// Converts <see cref="LatexNode"/> ASTs into aligned <see cref="MathBox"/> trees.
    /// Maintainers: Extend <see cref="MeasureNode"/> to add rendering for new AST node types.
    /// </summary>
    public static class LatexMathRenderer
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private static readonly Typeface MathItalicTypeface =
            new(new FontFamily("Cambria, Times New Roman, Georgia, serif"), FontStyle.Italic, FontWeight.Normal);

        private static readonly Typeface MathRegularTypeface =
            new(new FontFamily("Cambria, Times New Roman, Georgia, serif"), FontStyle.Normal, FontWeight.Normal);

        private static readonly Typeface MathBoldTypeface =
            new(new FontFamily("Cambria, Times New Roman, Georgia, serif"), FontStyle.Normal, FontWeight.Bold);

        private static readonly Typeface TextSansTypeface =
            new(FontFamily.Default, FontStyle.Normal, FontWeight.Normal);

        private static readonly Typeface MonoTypeface =
            new(new FontFamily("Cascadia Code, Consolas, Courier New, monospace"), FontStyle.Normal, FontWeight.Normal);

        /// <summary>
        /// Measures an entire parsed LaTeX root node at the specified font size and brush.
        /// </summary>
        public static MathBox Measure(LatexNode root, double fontSize, IBrush foreground, bool isDisplayMode = true)
        {
            return MeasureNode(root, fontSize, foreground, isDisplayMode, forcedStyle: null);
        }

        private static MathBox MeasureNode(
            LatexNode node,
            double fontSize,
            IBrush brush,
            bool isDisplayMode,
            string? forcedStyle)
        {
            return node switch
            {
                SequenceNode seq => MeasureSequence(seq, fontSize, brush, isDisplayMode, forcedStyle),
                SymbolNode sym => MeasureSymbol(sym, fontSize, brush, isDisplayMode, forcedStyle),
                FractionNode frac => MeasureFraction(frac, fontSize, brush, isDisplayMode, forcedStyle),
                RootNode root => MeasureRoot(root, fontSize, brush, isDisplayMode, forcedStyle),
                ScriptNode script => MeasureScript(script, fontSize, brush, isDisplayMode, forcedStyle),
                MatrixNode mat => MeasureMatrix(mat, fontSize, brush, isDisplayMode, forcedStyle),
                AccentNode acc => MeasureAccent(acc, fontSize, brush, isDisplayMode, forcedStyle),
                DelimiterNode del => MeasureDelimiter(del, fontSize, brush, isDisplayMode, forcedStyle),
                StyledNode styled => MeasureStyled(styled, fontSize, brush, isDisplayMode),
                SpaceNode sp => new MathBox(Math.Max(0, sp.EmWidth * fontSize), 0, 0, static (_, _) => { }),
                _ => MathBox.Empty
            };
        }

        private static MathBox MeasureSequence(
            SequenceNode seq,
            double fontSize,
            IBrush brush,
            bool isDisplayMode,
            string? forcedStyle)
        {
            if (seq.Items.Count == 0)
                return new MathBox(0, fontSize * 1.15, fontSize * 0.88, static (_, _) => { });

            var boxes = new List<MathBox>(seq.Items.Count);
            var spacings = new List<double>(seq.Items.Count);

            for (int i = 0; i < seq.Items.Count; i++)
            {
                var item = seq.Items[i];
                var box = MeasureNode(item, fontSize, brush, isDisplayMode, forcedStyle);
                boxes.Add(box);

                if (i + 1 < seq.Items.Count)
                    spacings.Add(GetGapBetween(item, seq.Items[i + 1], fontSize));
            }

            double totalWidth = 0;
            double maxAscent = 0;
            double maxDescent = 0;

            for (int i = 0; i < boxes.Count; i++)
            {
                totalWidth += boxes[i].Width;
                if (i < spacings.Count)
                    totalWidth += spacings[i];
                maxAscent = Math.Max(maxAscent, boxes[i].Baseline);
                maxDescent = Math.Max(maxDescent, boxes[i].Height - boxes[i].Baseline);
            }

            double totalHeight = Math.Max(fontSize * 1.1, maxAscent + maxDescent);
            double baseline = maxAscent;

            return new MathBox(totalWidth, totalHeight, baseline, (ctx, origin) =>
            {
                double x = origin.X;
                for (int i = 0; i < boxes.Count; i++)
                {
                    var b = boxes[i];
                    double y = origin.Y + (baseline - b.Baseline);
                    b.Draw(ctx, new Point(x, y));
                    x += b.Width;
                    if (i < spacings.Count)
                        x += spacings[i];
                }
            });
        }

        private static double GetGapBetween(LatexNode left, LatexNode right, double fontSize)
        {
            if (left is SymbolNode { Kind: SymbolKind.Relation } || right is SymbolNode { Kind: SymbolKind.Relation })
                return fontSize * 0.24;

            if (left is SymbolNode { Kind: SymbolKind.Operator } || right is SymbolNode { Kind: SymbolKind.Operator })
                return fontSize * 0.16;

            if (left is SymbolNode { Kind: SymbolKind.FunctionName } && right is not SymbolNode { Kind: SymbolKind.Punctuation })
                return fontSize * 0.15;

            if (left is SymbolNode { Kind: SymbolKind.BigOperator } || left is ScriptNode { Base: SymbolNode { Kind: SymbolKind.BigOperator } })
                return fontSize * 0.18;

            if (left is SymbolNode { Text: "," or ";" })
                return fontSize * 0.18;

            return fontSize * 0.04;
        }

        private static MathBox MeasureSymbol(
            SymbolNode sym,
            double fontSize,
            IBrush brush,
            bool isDisplayMode,
            string? forcedStyle)
        {
            double size = fontSize;
            Typeface tf = MathRegularTypeface;

            if (sym.Kind == SymbolKind.BigOperator)
            {
                bool isIntegralOrSum = sym.Text is "\u222B" or "\u222C" or "\u222D" or "\u222E" or "\u2211" or "\u220F" or "\u2210" or "\u22C3" or "\u22C2" or "\u2A01" or "\u2A02";
                if (isIntegralOrSum)
                    size = fontSize * (isDisplayMode ? 1.55 : 1.25);
            }
            else if (forcedStyle is not null)
            {
                tf = forcedStyle switch
                {
                    "textbf" or "mathbf" => MathBoldTypeface,
                    "textit" or "mathit" => MathItalicTypeface,
                    "mathsf" or "text" or "mbox" => TextSansTypeface,
                    "mathtt" => MonoTypeface,
                    _ => MathRegularTypeface
                };
            }
            else
            {
                tf = sym.Kind switch
                {
                    SymbolKind.Identifier => IsSingleAsciiLetter(sym.Text) ? MathItalicTypeface : MathRegularTypeface,
                    SymbolKind.Text => TextSansTypeface,
                    _ => MathRegularTypeface
                };
            }

            if (string.IsNullOrEmpty(sym.Text))
                return new MathBox(0, fontSize * 1.1, fontSize * 0.85, static (_, _) => { });

            var ft = new FormattedText(sym.Text, Invariant, FlowDirection.LeftToRight, tf, size, brush);
            double width = Math.Max(ft.WidthIncludingTrailingWhitespace, ft.Width);
            double height = Math.Max(ft.Height, size);
            double baseline = ft.Baseline;

            // Center tall big operators vertically around the math axis
            if (sym.Kind == SymbolKind.BigOperator && size > fontSize * 1.2)
            {
                double mathAxis = fontSize * 0.28;
                double centeredBaseline = (height / 2.0) + mathAxis;
                return new MathBox(width, height, centeredBaseline, (ctx, origin) =>
                {
                    ctx.DrawText(ft, origin);
                });
            }

            return new MathBox(width, height, baseline, (ctx, origin) =>
            {
                ctx.DrawText(ft, origin);
            });
        }

        private static MathBox MeasureFraction(
            FractionNode frac,
            double fontSize,
            IBrush brush,
            bool isDisplayMode,
            string? forcedStyle)
        {
            double childScale = (isDisplayMode && !frac.IsInline) ? 0.86 : 0.72;
            double childSize = Math.Max(9.0, fontSize * childScale);

            var numBox = MeasureNode(frac.Numerator, childSize, brush, isDisplayMode: false, forcedStyle);
            var denBox = MeasureNode(frac.Denominator, childSize, brush, isDisplayMode: false, forcedStyle);

            double hPad = Math.Max(3.0, fontSize * 0.18);
            double vGap = Math.Max(2.0, fontSize * 0.14);
            double barThickness = Math.Max(1.0, fontSize * 0.065);

            double contentWidth = Math.Max(numBox.Width, denBox.Width);
            double totalWidth = contentWidth + hPad * 2;
            double barY = numBox.Height + vGap;
            double totalHeight = barY + (frac.ShowBar ? barThickness : 0) + vGap + denBox.Height;
            double mathAxisFromBaseline = fontSize * 0.28;
            double baseline = barY + (barThickness / 2.0) + mathAxisFromBaseline;

            return new MathBox(totalWidth, totalHeight, baseline, (ctx, origin) =>
            {
                double numX = origin.X + (totalWidth - numBox.Width) / 2.0;
                numBox.Draw(ctx, new Point(numX, origin.Y));

                if (frac.ShowBar)
                {
                    var pen = new Pen(brush, barThickness);
                    double lineY = origin.Y + barY + barThickness / 2.0;
                    ctx.DrawLine(pen, new Point(origin.X + 1, lineY), new Point(origin.X + totalWidth - 1, lineY));
                }

                double denX = origin.X + (totalWidth - denBox.Width) / 2.0;
                double denY = origin.Y + barY + (frac.ShowBar ? barThickness : 0) + vGap;
                denBox.Draw(ctx, new Point(denX, denY));
            });
        }

        private static MathBox MeasureRoot(
            RootNode root,
            double fontSize,
            IBrush brush,
            bool isDisplayMode,
            string? forcedStyle)
        {
            var bodyBox = MeasureNode(root.Radicand, fontSize, brush, isDisplayMode, forcedStyle);
            MathBox? degreeBox = root.Degree is not null
                ? MeasureNode(root.Degree, Math.Max(8.0, fontSize * 0.56), brush, false, forcedStyle)
                : null;

            double topPad = Math.Max(3.0, fontSize * 0.16);
            double rightPad = Math.Max(2.0, fontSize * 0.12);
            double radicalW = Math.Max(11.0, fontSize * 0.62);
            double degreeLead = degreeBox is not null ? Math.Max(0, degreeBox.Width - radicalW * 0.45) : 0;
            double strokeWidth = Math.Max(1.1, fontSize * 0.065);

            double totalWidth = degreeLead + radicalW + bodyBox.Width + rightPad;
            double bodyTop = topPad + (degreeBox is not null ? Math.Max(0, degreeBox.Height - bodyBox.Height * 0.45) : 0);
            double totalHeight = bodyTop + bodyBox.Height + 2.0;
            double baseline = bodyTop + bodyBox.Baseline;

            return new MathBox(totalWidth, totalHeight, baseline, (ctx, origin) =>
            {
                double rx = origin.X + degreeLead;
                double bodyStartX = rx + radicalW;
                double topLineY = origin.Y + bodyTop - topPad * 0.45;
                double bottomY = origin.Y + totalHeight - 2.0;
                double midY = origin.Y + bodyTop + bodyBox.Height * 0.55;

                if (degreeBox is not null)
                {
                    double degX = origin.X + Math.Max(0, (rx + radicalW * 0.45) - degreeBox.Width);
                    double degY = Math.Max(origin.Y, midY - degreeBox.Height - 2.0);
                    degreeBox.Draw(ctx, new Point(degX, degY));
                }

                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(rx + 1.0, midY), isFilled: false);
                    g.LineTo(new Point(rx + radicalW * 0.25, midY - fontSize * 0.08));
                    g.LineTo(new Point(rx + radicalW * 0.52, bottomY));
                    g.LineTo(new Point(bodyStartX - 1.5, topLineY));
                    g.LineTo(new Point(origin.X + totalWidth - 1.0, topLineY));
                    g.EndFigure(isClosed: false);
                }

                var pen = new Pen(brush, strokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                ctx.DrawGeometry(null, pen, geo);

                bodyBox.Draw(ctx, new Point(bodyStartX, origin.Y + bodyTop));
            });
        }

        private static MathBox MeasureScript(
            ScriptNode script,
            double fontSize,
            IBrush brush,
            bool isDisplayMode,
            string? forcedStyle)
        {
            var baseBox = MeasureNode(script.Base, fontSize, brush, isDisplayMode, forcedStyle);
            double scriptSize = Math.Max(8.5, fontSize * 0.66);

            var subBox = script.Subscript is not null
                ? MeasureNode(script.Subscript, scriptSize, brush, false, forcedStyle)
                : null;
            var supBox = script.Superscript is not null
                ? MeasureNode(script.Superscript, scriptSize, brush, false, forcedStyle)
                : null;

            if (script.StackLimits)
            {
                double gap = Math.Max(1.5, fontSize * 0.1);
                double width = baseBox.Width;
                if (supBox is not null) width = Math.Max(width, supBox.Width);
                if (subBox is not null) width = Math.Max(width, subBox.Width);

                double supH = supBox is not null ? supBox.Height + gap : 0;
                double subH = subBox is not null ? subBox.Height + gap : 0;
                double totalHeight = supH + baseBox.Height + subH;
                double baseline = supH + baseBox.Baseline;

                return new MathBox(width, totalHeight, baseline, (ctx, origin) =>
                {
                    if (supBox is not null)
                    {
                        supBox.Draw(ctx, new Point(origin.X + (width - supBox.Width) / 2.0, origin.Y));
                    }

                    baseBox.Draw(ctx, new Point(origin.X + (width - baseBox.Width) / 2.0, origin.Y + supH));

                    if (subBox is not null)
                    {
                        subBox.Draw(ctx, new Point(origin.X + (width - subBox.Width) / 2.0, origin.Y + supH + baseBox.Height + gap));
                    }
                });
            }

            // Standard side sub/superscript placement
            double supRise = supBox is not null ? Math.Max(0, (supBox.Height - baseBox.Baseline * 0.45)) : 0;
            double subDrop = subBox is not null ? Math.Max(0, (subBox.Height - (baseBox.Height - baseBox.Baseline) * 0.45)) : 0;
            double baseTop = supRise * 0.65;
            double scriptWidth = Math.Max(subBox?.Width ?? 0, supBox?.Width ?? 0);
            double xGap = fontSize * 0.04;
            double totalW = baseBox.Width + xGap + scriptWidth;

            double supY = Math.Max(0, baseTop + baseBox.Baseline * 0.35 - (supBox?.Height ?? 0));
            double subY = baseTop + baseBox.Baseline - fontSize * 0.18;

            double maxBottom = Math.Max(baseTop + baseBox.Height, subBox is not null ? subY + subBox.Height : 0);
            double totalH = Math.Max(baseBox.Height, maxBottom) + (subDrop * 0.1);
            double outBaseline = baseTop + baseBox.Baseline;

            return new MathBox(totalW, totalH, outBaseline, (ctx, origin) =>
            {
                baseBox.Draw(ctx, new Point(origin.X, origin.Y + baseTop));
                double sx = origin.X + baseBox.Width + xGap;
                supBox?.Draw(ctx, new Point(sx, origin.Y + supY));
                subBox?.Draw(ctx, new Point(sx, origin.Y + subY));
            });
        }

        private static MathBox MeasureMatrix(
            MatrixNode matrix,
            double fontSize,
            IBrush brush,
            bool isDisplayMode,
            string? forcedStyle)
        {
            int rowCount = matrix.Rows.Count;
            int colCount = 0;
            foreach (var r in matrix.Rows)
                colCount = Math.Max(colCount, r.Count);

            if (rowCount == 0 || colCount == 0)
                return MathBox.Empty;

            var grid = new MathBox[rowCount, colCount];
            var colWidths = new double[colCount];
            var rowAscents = new double[rowCount];
            var rowDescents = new double[rowCount];

            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    var cellBox = c < matrix.Rows[r].Count
                        ? MeasureNode(matrix.Rows[r][c], fontSize * 0.92, brush, isDisplayMode: false, forcedStyle)
                        : MathBox.Empty;
                    grid[r, c] = cellBox;
                    colWidths[c] = Math.Max(colWidths[c], cellBox.Width);
                    rowAscents[r] = Math.Max(rowAscents[r], cellBox.Baseline);
                    rowDescents[r] = Math.Max(rowDescents[r], cellBox.Height - cellBox.Baseline);
                }
            }

            double colGap = fontSize * 0.65;
            double rowGap = fontSize * 0.28;
            double innerW = 0;
            for (int c = 0; c < colCount; c++)
            {
                innerW += colWidths[c];
                if (c + 1 < colCount) innerW += colGap;
            }

            double innerH = 0;
            for (int r = 0; r < rowCount; r++)
            {
                innerH += Math.Max(fontSize * 0.9, rowAscents[r] + rowDescents[r]);
                if (r + 1 < rowCount) innerH += rowGap;
            }

            bool leftAlign = matrix.EnvironmentName is "cases" or "aligned" or "aligned_implicit";
            double innerBaseline = (innerH / 2.0) + fontSize * 0.28;

            var contentBox = new MathBox(innerW, innerH, innerBaseline, (ctx, origin) =>
            {
                double y = origin.Y;
                for (int r = 0; r < rowCount; r++)
                {
                    double rowH = Math.Max(fontSize * 0.9, rowAscents[r] + rowDescents[r]);
                    double x = origin.X;
                    for (int c = 0; c < colCount; c++)
                    {
                        var cell = grid[r, c];
                        double cellX = leftAlign ? x : x + (colWidths[c] - cell.Width) / 2.0;
                        double cellY = y + (rowAscents[r] - cell.Baseline);
                        cell.Draw(ctx, new Point(cellX, cellY));
                        x += colWidths[c] + colGap;
                    }
                    y += rowH + rowGap;
                }
            });

            (string leftDelim, string rightDelim) = matrix.EnvironmentName switch
            {
                "pmatrix" => ("(", ")"),
                "bmatrix" => ("[", "]"),
                "Bmatrix" => ("{", "}"),
                "vmatrix" => ("|", "|"),
                "Vmatrix" => ("\u2016", "\u2016"),
                "cases" => ("{", "."),
                _ => (".", ".")
            };

            if (leftDelim == "." && rightDelim == ".")
                return contentBox;

            return WrapWithDelimiters(leftDelim, contentBox, rightDelim, fontSize, brush);
        }

        private static MathBox MeasureAccent(
            AccentNode accent,
            double fontSize,
            IBrush brush,
            bool isDisplayMode,
            string? forcedStyle)
        {
            var body = MeasureNode(accent.Body, fontSize, brush, isDisplayMode, forcedStyle);
            bool isUnderline = accent.AccentCommand is "underline" or "underbrace";
            double ornamentH = Math.Max(4.0, fontSize * 0.28);
            double strokeW = Math.Max(1.0, fontSize * 0.06);
            double width = Math.Max(body.Width, fontSize * 0.45);
            double height = body.Height + ornamentH;
            double baseline = isUnderline ? body.Baseline : ornamentH + body.Baseline;

            return new MathBox(width, height, baseline, (ctx, origin) =>
            {
                double bodyY = isUnderline ? origin.Y : origin.Y + ornamentH;
                double bodyX = origin.X + (width - body.Width) / 2.0;
                body.Draw(ctx, new Point(bodyX, bodyY));

                var pen = new Pen(brush, strokeW, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                double midX = origin.X + width / 2.0;

                if (isUnderline)
                {
                    double y = origin.Y + body.Height + ornamentH * 0.35;
                    ctx.DrawLine(pen, new Point(origin.X + 1, y), new Point(origin.X + width - 1, y));
                    return;
                }

                double topY = origin.Y + ornamentH * 0.72;
                switch (accent.AccentCommand)
                {
                    case "hat":
                    case "widehat":
                    {
                        double span = Math.Min(width * 0.45, Math.Max(4, fontSize * 0.28));
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(new Point(midX - span, topY + 2), false);
                            g.LineTo(new Point(midX, origin.Y + 1.5));
                            g.LineTo(new Point(midX + span, topY + 2));
                            g.EndFigure(false);
                        }
                        ctx.DrawGeometry(null, pen, geo);
                        break;
                    }
                    case "vec":
                    {
                        double x1 = origin.X + 1.5;
                        double x2 = origin.X + width - 1.5;
                        ctx.DrawLine(pen, new Point(x1, topY), new Point(x2, topY));
                        ctx.DrawLine(pen, new Point(x2 - 3.5, topY - 2.5), new Point(x2, topY));
                        ctx.DrawLine(pen, new Point(x2 - 3.5, topY + 2.5), new Point(x2, topY));
                        break;
                    }
                    case "dot":
                        ctx.DrawEllipse(brush, null, new Point(midX, topY - 1), strokeW * 1.1, strokeW * 1.1);
                        break;
                    case "ddot":
                        ctx.DrawEllipse(brush, null, new Point(midX - strokeW * 2.2, topY - 1), strokeW * 1.0, strokeW * 1.0);
                        ctx.DrawEllipse(brush, null, new Point(midX + strokeW * 2.2, topY - 1), strokeW * 1.0, strokeW * 1.0);
                        break;
                    case "tilde":
                    case "widetilde":
                    {
                        double span = Math.Min(width * 0.45, Math.Max(5, fontSize * 0.32));
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(new Point(midX - span, topY + 1), false);
                            g.CubicBezierTo(
                                new Point(midX - span * 0.3, topY - 3.5),
                                new Point(midX + span * 0.3, topY + 4.5),
                                new Point(midX + span, topY));
                            g.EndFigure(false);
                        }
                        ctx.DrawGeometry(null, pen, geo);
                        break;
                    }
                    default: // \bar, \overline, \overbrace
                        ctx.DrawLine(pen, new Point(origin.X + 1, topY), new Point(origin.X + width - 1, topY));
                        break;
                }
            });
        }

        private static MathBox MeasureDelimiter(
            DelimiterNode del,
            double fontSize,
            IBrush brush,
            bool isDisplayMode,
            string? forcedStyle)
        {
            var body = MeasureNode(del.Body, fontSize, brush, isDisplayMode, forcedStyle);
            return WrapWithDelimiters(del.LeftDelimiter, body, del.RightDelimiter, fontSize, brush);
        }

        private static MathBox WrapWithDelimiters(
            string leftDelim,
            MathBox body,
            string rightDelim,
            double fontSize,
            IBrush brush)
        {
            double targetHeight = Math.Max(body.Height, fontSize * 1.15);
            var leftBox = CreateDelimiterBox(leftDelim, targetHeight, fontSize, brush);
            var rightBox = CreateDelimiterBox(rightDelim, targetHeight, fontSize, brush);

            double pad = fontSize * 0.1;
            double width = leftBox.Width + (leftBox.Width > 0 ? pad : 0) + body.Width + (rightBox.Width > 0 ? pad : 0) + rightBox.Width;
            double height = Math.Max(body.Height, Math.Max(leftBox.Height, rightBox.Height));
            double baseline = body.Baseline + (height - body.Height) / 2.0;

            return new MathBox(width, height, baseline, (ctx, origin) =>
            {
                double x = origin.X;
                if (leftBox.Width > 0)
                {
                    leftBox.Draw(ctx, new Point(x, origin.Y + (height - leftBox.Height) / 2.0));
                    x += leftBox.Width + pad;
                }

                body.Draw(ctx, new Point(x, origin.Y + (height - body.Height) / 2.0));
                x += body.Width + (rightBox.Width > 0 ? pad : 0);

                if (rightBox.Width > 0)
                {
                    rightBox.Draw(ctx, new Point(x, origin.Y + (height - rightBox.Height) / 2.0));
                }
            });
        }

        private static MathBox CreateDelimiterBox(string delim, double targetHeight, double fontSize, IBrush brush)
        {
            if (string.IsNullOrEmpty(delim) || delim == ".")
                return MathBox.Empty;

            if (targetHeight <= fontSize * 1.35)
            {
                var ft = new FormattedText(delim, Invariant, FlowDirection.LeftToRight, MathRegularTypeface, fontSize, brush);
                return new MathBox(ft.Width, Math.Max(ft.Height, targetHeight), ft.Baseline, (ctx, p) => ctx.DrawText(ft, p));
            }

            double w = Math.Max(8.0, fontSize * 0.45);
            double stroke = Math.Max(1.2, fontSize * 0.068);
            double baseline = targetHeight / 2.0 + fontSize * 0.28;

            return new MathBox(w, targetHeight, baseline, (ctx, origin) =>
            {
                var pen = new Pen(brush, stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                double x = origin.X;
                double y0 = origin.Y + 1.5;
                double y1 = origin.Y + targetHeight - 1.5;
                double ym = (y0 + y1) / 2.0;

                switch (delim)
                {
                    case "(":
                    {
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(new Point(x + w * 0.85, y0), false);
                            g.QuadraticBezierTo(new Point(x + w * 0.05, ym), new Point(x + w * 0.85, y1));
                            g.EndFigure(false);
                        }
                        ctx.DrawGeometry(null, pen, geo);
                        break;
                    }
                    case ")":
                    {
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(new Point(x + w * 0.15, y0), false);
                            g.QuadraticBezierTo(new Point(x + w * 0.95, ym), new Point(x + w * 0.15, y1));
                            g.EndFigure(false);
                        }
                        ctx.DrawGeometry(null, pen, geo);
                        break;
                    }
                    case "[":
                    {
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(new Point(x + w * 0.85, y0), false);
                            g.LineTo(new Point(x + w * 0.25, y0));
                            g.LineTo(new Point(x + w * 0.25, y1));
                            g.LineTo(new Point(x + w * 0.85, y1));
                            g.EndFigure(false);
                        }
                        ctx.DrawGeometry(null, pen, geo);
                        break;
                    }
                    case "]":
                    {
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(new Point(x + w * 0.15, y0), false);
                            g.LineTo(new Point(x + w * 0.75, y0));
                            g.LineTo(new Point(x + w * 0.75, y1));
                            g.LineTo(new Point(x + w * 0.15, y1));
                            g.EndFigure(false);
                        }
                        ctx.DrawGeometry(null, pen, geo);
                        break;
                    }
                    case "{":
                    {
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(new Point(x + w * 0.88, y0), false);
                            g.QuadraticBezierTo(new Point(x + w * 0.45, y0), new Point(x + w * 0.45, ym - 4));
                            g.LineTo(new Point(x + w * 0.12, ym));
                            g.LineTo(new Point(x + w * 0.45, ym + 4));
                            g.QuadraticBezierTo(new Point(x + w * 0.45, y1), new Point(x + w * 0.88, y1));
                            g.EndFigure(false);
                        }
                        ctx.DrawGeometry(null, pen, geo);
                        break;
                    }
                    case "}":
                    {
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(new Point(x + w * 0.12, y0), false);
                            g.QuadraticBezierTo(new Point(x + w * 0.55, y0), new Point(x + w * 0.55, ym - 4));
                            g.LineTo(new Point(x + w * 0.88, ym));
                            g.LineTo(new Point(x + w * 0.55, ym + 4));
                            g.QuadraticBezierTo(new Point(x + w * 0.55, y1), new Point(x + w * 0.12, y1));
                            g.EndFigure(false);
                        }
                        ctx.DrawGeometry(null, pen, geo);
                        break;
                    }
                    case "|":
                        ctx.DrawLine(pen, new Point(x + w * 0.5, y0), new Point(x + w * 0.5, y1));
                        break;
                    case "\u2016":
                        ctx.DrawLine(pen, new Point(x + w * 0.35, y0), new Point(x + w * 0.35, y1));
                        ctx.DrawLine(pen, new Point(x + w * 0.65, y0), new Point(x + w * 0.65, y1));
                        break;
                    case "\u27E8":
                    {
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(new Point(x + w * 0.85, y0), false);
                            g.LineTo(new Point(x + w * 0.15, ym));
                            g.LineTo(new Point(x + w * 0.85, y1));
                            g.EndFigure(false);
                        }
                        ctx.DrawGeometry(null, pen, geo);
                        break;
                    }
                    case "\u27E9":
                    {
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(new Point(x + w * 0.15, y0), false);
                            g.LineTo(new Point(x + w * 0.85, ym));
                            g.LineTo(new Point(x + w * 0.15, y1));
                            g.EndFigure(false);
                        }
                        ctx.DrawGeometry(null, pen, geo);
                        break;
                    }
                    default:
                    {
                        var ft = new FormattedText(delim, Invariant, FlowDirection.LeftToRight, MathRegularTypeface, targetHeight * 0.72, brush);
                        ctx.DrawText(ft, new Point(x, origin.Y + (targetHeight - ft.Height) / 2.0));
                        break;
                    }
                }
            });
        }

        private static MathBox MeasureStyled(StyledNode styled, double fontSize, IBrush brush, bool isDisplayMode)
        {
            IBrush targetBrush = brush;
            if (!string.IsNullOrWhiteSpace(styled.ColorHex) && Color.TryParse(styled.ColorHex, out Color parsed))
                targetBrush = new SolidColorBrush(parsed);

            string? nextStyle = styled.StyleCommand == "color" ? null : styled.StyleCommand;
            return MeasureNode(styled.Body, fontSize, targetBrush, isDisplayMode, nextStyle);
        }

        private static bool IsSingleAsciiLetter(string text)
            => text.Length == 1 && ((text[0] >= 'a' && text[0] <= 'z') || (text[0] >= 'A' && text[0] <= 'Z'));
    }
}
