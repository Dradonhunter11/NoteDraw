using System.Collections.Generic;

namespace NoteDraw.Rendering.Latex
{
    /// <summary>
    /// Semantic role of a leaf symbol node for typography and spacing decisions.
    /// </summary>
    public enum SymbolKind
    {
        Identifier,
        Number,
        Operator,
        Relation,
        Punctuation,
        FunctionName,
        BigOperator,
        Text
    }

    /// <summary>
    /// Base type for parsed LaTeX abstract syntax tree (AST) nodes.
    /// </summary>
    public abstract record LatexNode;

    /// <summary>Horizontal concatenation of child nodes.</summary>
    public sealed record SequenceNode(IReadOnlyList<LatexNode> Items) : LatexNode
    {
        public static readonly SequenceNode Empty = new SequenceNode(new List<LatexNode>());
    }

    /// <summary>Single rendered symbol, number, identifier, or operator.</summary>
    public sealed record SymbolNode(string Text, SymbolKind Kind) : LatexNode;

    /// <summary>Fraction or binomial stack (\frac, \dfrac, \tfrac, \binom).</summary>
    public sealed record FractionNode(LatexNode Numerator, LatexNode Denominator, bool ShowBar = true, bool IsInline = false) : LatexNode;

    /// <summary>Square or n-th root (\sqrt, \sqrt[n]{x}).</summary>
    public sealed record RootNode(LatexNode Radicand, LatexNode? Degree = null) : LatexNode;

    /// <summary>Subscript and/or superscript attached to a base expression.</summary>
    public sealed record ScriptNode(LatexNode Base, LatexNode? Subscript, LatexNode? Superscript, bool StackLimits) : LatexNode;

    /// <summary>Matrix, cases, or aligned multi-row environment.</summary>
    public sealed record MatrixNode(string EnvironmentName, IReadOnlyList<IReadOnlyList<LatexNode>> Rows) : LatexNode;

    /// <summary>Accented or over/under-lined expression (\hat, \bar, \vec, \overline, etc.).</summary>
    public sealed record AccentNode(string AccentCommand, LatexNode Body) : LatexNode;

    /// <summary>Scalable left/right delimiter wrapper (\left( ... \right)).</summary>
    public sealed record DelimiterNode(string LeftDelimiter, LatexNode Body, string RightDelimiter) : LatexNode;

    /// <summary>Font-styled or colored expression (\text, \mathbf, \mathbb, \color, etc.).</summary>
    public sealed record StyledNode(string StyleCommand, LatexNode Body, string? ColorHex = null) : LatexNode;

    /// <summary>Horizontal spacing node.</summary>
    public sealed record SpaceNode(double EmWidth) : LatexNode;

    /// <summary>
    /// Result of parsing a LaTeX expression, including non-fatal live-typing diagnostics.
    /// </summary>
    public sealed record LatexParseResult(LatexNode Root, IReadOnlyList<string> Diagnostics)
    {
        public bool HasWarnings => Diagnostics.Count > 0;
    }
}
