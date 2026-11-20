using System;
using System.Collections.Generic;
using System.Text;

namespace NoteDraw.Rendering.Latex
{
    /// <summary>
    /// Fault-tolerant recursive-descent LaTeX math parser designed for real-time live preview.
    /// Never throws on incomplete input while typing; instead collects non-blocking diagnostics.
    /// Maintainers: Add new structural commands inside <see cref="ParseCommand"/>.
    /// </summary>
    public sealed class LatexParser
    {
        private readonly string _input;
        private readonly List<string> _diagnostics = new();
        private readonly bool _isDisplayMode;
        private int _pos;

        private LatexParser(string input, bool isDisplayMode)
        {
            _input = input ?? string.Empty;
            _isDisplayMode = isDisplayMode;
        }

        /// <summary>
        /// Parses a LaTeX expression string into an AST and diagnostics list.
        /// </summary>
        public static LatexParseResult Parse(string? latex, bool isDisplayMode = true)
        {
            var parser = new LatexParser(latex ?? string.Empty, isDisplayMode);
            var root = parser.ParseTopLevel();
            return new LatexParseResult(root, parser._diagnostics);
        }

        private LatexNode ParseTopLevel()
        {
            var rows = new List<IReadOnlyList<LatexNode>>();
            var currentRow = new List<LatexNode>();
            var currentCell = new List<LatexNode>();

            while (!AtEnd)
            {
                SkipWhitespace();
                if (AtEnd)
                    break;

                if (PeekIsRowBreak())
                {
                    _pos += 2;
                    SkipOptionalBracketArg();
                    currentRow.Add(Collapse(currentCell));
                    rows.Add(currentRow);
                    currentCell = new List<LatexNode>();
                    currentRow = new List<LatexNode>();
                    continue;
                }

                if (Current == '&')
                {
                    _pos++;
                    currentRow.Add(Collapse(currentCell));
                    currentCell = new List<LatexNode>();
                    continue;
                }

                if (TryParseColorDirective(out string? colorHex, out LatexNode? explicitTarget))
                {
                    if (explicitTarget is not null)
                    {
                        currentCell.Add(new StyledNode("color", explicitTarget, colorHex));
                    }
                    else
                    {
                        var rest = ParseSequenceUntil(stopOnBrace: false, stopOnRight: false, stopOnTableMarkers: true);
                        currentCell.Add(new StyledNode("color", rest, colorHex));
                    }
                    continue;
                }

                currentCell.Add(ParseScriptedAtom());
            }

            if (rows.Count == 0 && currentRow.Count == 0)
                return Collapse(currentCell);

            currentRow.Add(Collapse(currentCell));
            rows.Add(currentRow);
            return new MatrixNode("aligned_implicit", rows);
        }

        private LatexNode ParseSequenceUntil(bool stopOnBrace, bool stopOnRight, bool stopOnTableMarkers)
        {
            var items = new List<LatexNode>();

            while (!AtEnd)
            {
                SkipWhitespace();
                if (AtEnd)
                    break;

                if (stopOnBrace && Current == '}')
                    break;

                if (stopOnRight && PeekCommandName() == "right")
                    break;

                if (stopOnTableMarkers && (Current == '&' || PeekIsRowBreak() || PeekCommandName() == "end"))
                    break;

                if (TryParseColorDirective(out string? colorHex, out LatexNode? explicitTarget))
                {
                    if (explicitTarget is not null)
                    {
                        items.Add(new StyledNode("color", explicitTarget, colorHex));
                    }
                    else
                    {
                        var rest = ParseSequenceUntil(stopOnBrace, stopOnRight, stopOnTableMarkers);
                        items.Add(new StyledNode("color", rest, colorHex));
                        break;
                    }
                    continue;
                }

                items.Add(ParseScriptedAtom());
            }

            return Collapse(items);
        }

        private LatexNode ParseScriptedAtom()
        {
            LatexNode baseNode = ParseAtom(out bool defaultStackLimits);
            bool? explicitStackLimits = null;
            LatexNode? sub = null;
            LatexNode? sup = null;

            while (!AtEnd)
            {
                SkipWhitespace();
                if (AtEnd)
                    break;

                string? nextCmd = PeekCommandName();
                if (nextCmd == "limits")
                {
                    ReadCommandName();
                    explicitStackLimits = true;
                    continue;
                }
                if (nextCmd == "nolimits")
                {
                    ReadCommandName();
                    explicitStackLimits = false;
                    continue;
                }

                if (Current == '_' && sub is null)
                {
                    _pos++;
                    SkipWhitespace();
                    if (AtEnd || Current == '}')
                    {
                        _diagnostics.Add("Incomplete subscript '_' — waiting for expression");
                        sub = new SymbolNode("\u25A1", SymbolKind.Operator);
                    }
                    else
                    {
                        sub = ParseSingleArgument(allowRawText: false);
                    }
                }
                else if (Current == '^' && sup is null)
                {
                    _pos++;
                    SkipWhitespace();
                    if (AtEnd || Current == '}')
                    {
                        _diagnostics.Add("Incomplete superscript '^' — waiting for expression");
                        sup = new SymbolNode("\u25A1", SymbolKind.Operator);
                    }
                    else
                    {
                        sup = ParseSingleArgument(allowRawText: false);
                    }
                }
                else
                {
                    break;
                }
            }

            if (sub is null && sup is null)
                return baseNode;

            bool stackLimits = explicitStackLimits ?? (_isDisplayMode && defaultStackLimits);
            return new ScriptNode(baseNode, sub, sup, stackLimits);
        }

        private LatexNode ParseAtom(out bool defaultStackLimits)
        {
            defaultStackLimits = false;
            SkipWhitespace();

            if (AtEnd)
                return SequenceNode.Empty;

            char ch = Current;

            if (ch == '{')
            {
                _pos++;
                var inner = ParseSequenceUntil(stopOnBrace: true, stopOnRight: false, stopOnTableMarkers: false);
                if (!AtEnd && Current == '}')
                    _pos++;
                else
                    _diagnostics.Add("Unclosed '{' — live preview showing partial expression");
                return inner;
            }

            if (ch == '}')
            {
                _pos++;
                _diagnostics.Add("Unexpected '}' delimiter");
                return SequenceNode.Empty;
            }

            if (ch == '\\')
                return ParseCommand(out defaultStackLimits);

            if (char.IsDigit(ch))
                return ReadNumber();

            _pos++;

            if (char.IsLetter(ch))
                return new SymbolNode(ch.ToString(), SymbolKind.Identifier);

            return ch switch
            {
                '+' or '-' or '*' or '/' => new SymbolNode(ch == '-' ? "\u2212" : ch.ToString(), SymbolKind.Operator),
                '=' or '<' or '>' => new SymbolNode(ch.ToString(), SymbolKind.Relation),
                _ => new SymbolNode(ch.ToString(), SymbolKind.Punctuation)
            };
        }

        private LatexNode ReadNumber()
        {
            int start = _pos;
            while (!AtEnd && (char.IsDigit(Current) || (Current == '.' && _pos + 1 < _input.Length && char.IsDigit(_input[_pos + 1]))))
            {
                _pos++;
            }
            return new SymbolNode(_input[start.._pos], SymbolKind.Number);
        }

        private LatexNode ParseCommand(out bool defaultStackLimits)
        {
            defaultStackLimits = false;
            string cmd = ReadCommandName();

            if (string.IsNullOrEmpty(cmd))
                return SequenceNode.Empty;

            if (LatexSymbolTable.Spaces.TryGetValue(cmd, out double spaceEm))
                return new SpaceNode(spaceEm);

            switch (cmd)
            {
                case "frac":
                case "dfrac":
                case "tfrac":
                {
                    var num = ParseRequiredArg($"\\{cmd} numerator");
                    var den = ParseRequiredArg($"\\{cmd} denominator");
                    bool isInline = cmd == "tfrac";
                    return new FractionNode(num, den, ShowBar: true, IsInline: isInline);
                }

                case "binom":
                {
                    var num = ParseRequiredArg("\\binom top");
                    var den = ParseRequiredArg("\\binom bottom");
                    return new DelimiterNode("(", new FractionNode(num, den, ShowBar: false), ")");
                }

                case "sqrt":
                {
                    LatexNode? degree = null;
                    SkipWhitespace();
                    if (!AtEnd && Current == '[')
                    {
                        degree = ReadBracketArgument();
                    }
                    var radicand = ParseRequiredArg("\\sqrt radicand");
                    return new RootNode(radicand, degree);
                }

                case "begin":
                    return ParseEnvironment();

                case "left":
                    return ParseLeftRight();

                case "right":
                    _diagnostics.Add("Unexpected '\\right' without matching '\\left'");
                    ReadDelimiterToken();
                    return SequenceNode.Empty;

                case "end":
                    _diagnostics.Add("Unexpected '\\end' without matching '\\begin'");
                    _ = ReadBraceLiteral();
                    return SequenceNode.Empty;

                case "not":
                {
                    SkipWhitespace();
                    string? next = PeekCommandName();
                    if (next == "in")
                    {
                        ReadCommandName();
                        return new SymbolNode("\u2209", SymbolKind.Relation);
                    }
                    if (next == "eq" || next == "equiv")
                    {
                        ReadCommandName();
                        return new SymbolNode(next == "eq" ? "\u2260" : "\u2262", SymbolKind.Relation);
                    }
                    if (!AtEnd && Current == '=')
                    {
                        _pos++;
                        return new SymbolNode("\u2260", SymbolKind.Relation);
                    }
                    return new SymbolNode("\u00AC", SymbolKind.Operator);
                }
            }

            if (LatexSymbolTable.Accents.Contains(cmd))
            {
                var body = ParseRequiredArg($"\\{cmd}");
                return new AccentNode(cmd, body);
            }

            if (LatexSymbolTable.StyleCommands.Contains(cmd))
                return ParseStyleCommand(cmd);

            if (LatexSymbolTable.BigOperators.TryGetValue(cmd, out var bigOp))
            {
                defaultStackLimits = bigOp.StackLimits;
                return new SymbolNode(bigOp.Glyph, SymbolKind.BigOperator);
            }

            if (LatexSymbolTable.NamedFunctions.Contains(cmd))
                return new SymbolNode(cmd, SymbolKind.FunctionName);

            if (LatexSymbolTable.Symbols.TryGetValue(cmd, out string? sym))
            {
                SymbolKind kind = IsRelationCommand(cmd) ? SymbolKind.Relation : SymbolKind.Operator;
                return new SymbolNode(sym, kind);
            }

            _diagnostics.Add($"Unrecognized command '\\{cmd}'");
            return new SymbolNode("\\" + cmd, SymbolKind.Text);
        }

        private LatexNode ParseStyleCommand(string cmd)
        {
            if (cmd is "text" or "textrm" or "textbf" or "textit" or "mbox")
            {
                string literal = ReadBraceLiteral();
                return new StyledNode(cmd, new SymbolNode(literal, SymbolKind.Text));
            }

            if (cmd == "mathbb")
            {
                SkipWhitespace();
                if (!AtEnd && Current == '{')
                {
                    string literal = ReadBraceLiteral();
                    return new StyledNode(cmd, new SymbolNode(LatexSymbolTable.ToBlackboardBold(literal), SymbolKind.Text));
                }
                if (!AtEnd)
                {
                    char c = _input[_pos++];
                    return new StyledNode(cmd, new SymbolNode(LatexSymbolTable.ToBlackboardBold(c.ToString()), SymbolKind.Text));
                }
                _diagnostics.Add("Incomplete \\mathbb — waiting for character");
                return new SymbolNode("\u25A1", SymbolKind.Text);
            }

            var arg = ParseRequiredArg($"\\{cmd}");
            return new StyledNode(cmd, arg);
        }

        private LatexNode ParseEnvironment()
        {
            string env = ReadBraceLiteral().Trim();
            if (string.IsNullOrEmpty(env))
            {
                _diagnostics.Add("Incomplete \\begin{...} — waiting for environment name");
                return SequenceNode.Empty;
            }

            var rows = new List<IReadOnlyList<LatexNode>>();
            var currentRow = new List<LatexNode>();
            bool closed = false;

            while (!AtEnd)
            {
                SkipWhitespace();
                if (AtEnd)
                    break;

                if (PeekCommandName() == "end")
                {
                    ReadCommandName();
                    string endEnv = ReadBraceLiteral().Trim();
                    if (!string.Equals(env, endEnv, StringComparison.Ordinal))
                        _diagnostics.Add($"Mismatched environment: \\begin{{{env}}} closed by \\end{{{endEnv}}}");
                    closed = true;
                    break;
                }

                var cell = ParseSequenceUntil(stopOnBrace: false, stopOnRight: false, stopOnTableMarkers: true);
                currentRow.Add(cell);

                SkipWhitespace();
                if (AtEnd)
                    break;

                if (Current == '&')
                {
                    _pos++;
                }
                else if (PeekIsRowBreak())
                {
                    _pos += 2;
                    SkipOptionalBracketArg();
                    rows.Add(currentRow);
                    currentRow = new List<LatexNode>();
                }
            }

            if (currentRow.Count > 0 || rows.Count == 0)
                rows.Add(currentRow);

            if (!closed)
                _diagnostics.Add($"Unclosed \\begin{{{env}}} — waiting for \\end{{{env}}}");

            return new MatrixNode(env, rows);
        }

        private LatexNode ParseLeftRight()
        {
            string left = ReadDelimiterToken();
            var body = ParseSequenceUntil(stopOnBrace: false, stopOnRight: true, stopOnTableMarkers: false);

            string right = ".";
            if (!AtEnd && PeekCommandName() == "right")
            {
                ReadCommandName();
                right = ReadDelimiterToken();
            }
            else
            {
                _diagnostics.Add("Unclosed '\\left' delimiter — waiting for '\\right'");
            }

            return new DelimiterNode(left, body, right);
        }

        private bool TryParseColorDirective(out string? colorHex, out LatexNode? explicitTarget)
        {
            colorHex = null;
            explicitTarget = null;
            if (PeekCommandName() != "color")
                return false;

            ReadCommandName();
            colorHex = ReadBraceLiteral().Trim();
            SkipWhitespace();
            if (!AtEnd && Current == '{')
                explicitTarget = ParseSingleArgument(allowRawText: false);
            return true;
        }

        private LatexNode ParseRequiredArg(string label)
        {
            SkipWhitespace();
            if (AtEnd || Current == '}')
            {
                _diagnostics.Add($"Incomplete {label} — waiting for argument");
                return new SymbolNode("\u25A1", SymbolKind.Operator);
            }
            return ParseSingleArgument(allowRawText: false);
        }

        private LatexNode ParseSingleArgument(bool allowRawText)
        {
            SkipWhitespace();
            if (AtEnd)
                return SequenceNode.Empty;

            if (Current == '{')
            {
                if (allowRawText)
                    return new SymbolNode(ReadBraceLiteral(), SymbolKind.Text);

                _pos++;
                var seq = ParseSequenceUntil(stopOnBrace: true, stopOnRight: false, stopOnTableMarkers: false);
                if (!AtEnd && Current == '}')
                    _pos++;
                else
                    _diagnostics.Add("Unclosed '{' — live preview showing partial expression");
                return seq;
            }

            if (Current == '\\')
                return ParseCommand(out _);

            char ch = _input[_pos++];
            if (char.IsDigit(ch))
                return new SymbolNode(ch.ToString(), SymbolKind.Number);
            if (char.IsLetter(ch))
                return new SymbolNode(ch.ToString(), SymbolKind.Identifier);
            return new SymbolNode(ch.ToString(), SymbolKind.Operator);
        }

        private LatexNode ReadBracketArgument()
        {
            _pos++; // consume '['
            var sb = new StringBuilder();
            int depth = 1;
            while (!AtEnd && depth > 0)
            {
                char ch = _input[_pos++];
                if (ch == '[') depth++;
                else if (ch == ']')
                {
                    depth--;
                    if (depth == 0) break;
                }
                sb.Append(ch);
            }
            if (depth > 0)
                _diagnostics.Add("Unclosed '[' optional argument");

            return Parse(sb.ToString(), isDisplayMode: false).Root;
        }

        private void SkipOptionalBracketArg()
        {
            int save = _pos;
            SkipWhitespace();
            if (!AtEnd && Current == '[')
            {
                while (!AtEnd && Current != ']')
                    _pos++;
                if (!AtEnd && Current == ']')
                    _pos++;
            }
            else
            {
                _pos = save;
            }
        }

        private string ReadBraceLiteral()
        {
            SkipWhitespace();
            if (AtEnd)
                return string.Empty;

            if (Current != '{')
                return _input[_pos++].ToString();

            _pos++; // consume '{'
            var sb = new StringBuilder();
            int depth = 1;
            while (!AtEnd && depth > 0)
            {
                char ch = _input[_pos++];
                if (ch == '{')
                {
                    depth++;
                    sb.Append(ch);
                }
                else if (ch == '}')
                {
                    depth--;
                    if (depth > 0)
                        sb.Append(ch);
                }
                else
                {
                    sb.Append(ch);
                }
            }
            if (depth > 0)
                _diagnostics.Add("Unclosed '{' in text argument");
            return sb.ToString();
        }

        private string ReadDelimiterToken()
        {
            SkipWhitespace();
            if (AtEnd)
                return ".";

            if (Current == '\\')
            {
                string cmd = ReadCommandName();
                return cmd switch
                {
                    "{" => "{",
                    "}" => "}",
                    "|" => "\u2016",
                    "lVert" or "rVert" => "\u2016",
                    "lvert" or "rvert" => "|",
                    "langle" => "\u27E8",
                    "rangle" => "\u27E9",
                    "lfloor" => "\u230A",
                    "rfloor" => "\u230B",
                    "lceil" => "\u2308",
                    "rceil" => "\u2309",
                    _ => cmd
                };
            }

            return _input[_pos++].ToString();
        }

        private string ReadCommandName()
        {
            if (AtEnd || Current != '\\')
                return string.Empty;

            _pos++; // consume '\'
            if (AtEnd)
                return string.Empty;

            if (!char.IsLetter(Current))
                return _input[_pos++].ToString();

            int start = _pos;
            while (!AtEnd && char.IsLetter(Current))
                _pos++;
            return _input[start.._pos];
        }

        private string? PeekCommandName()
        {
            int save = _pos;
            SkipWhitespace();
            if (AtEnd || Current != '\\')
            {
                _pos = save;
                return null;
            }
            string cmd = ReadCommandName();
            _pos = save;
            return cmd;
        }

        private bool PeekIsRowBreak()
            => _pos + 1 < _input.Length && _input[_pos] == '\\' && _input[_pos + 1] == '\\';

        private void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(Current))
                _pos++;
        }

        private static LatexNode Collapse(List<LatexNode> items) => items.Count switch
        {
            0 => SequenceNode.Empty,
            1 => items[0],
            _ => new SequenceNode(items)
        };

        private static bool IsRelationCommand(string cmd) => cmd is
            "leq" or "le" or "geq" or "ge" or "neq" or "ne" or "approx" or "equiv" or "sim" or
            "simeq" or "cong" or "propto" or "ll" or "gg" or "subset" or "subseteq" or "supset" or
            "supseteq" or "in" or "notin" or "ni" or "implies" or "iff" or "to" or "rightarrow" or
            "leftarrow" or "Rightarrow" or "Leftarrow" or "leftrightarrow" or "Leftrightarrow" or "mapsto";

        private bool AtEnd => _pos >= _input.Length;
        private char Current => _input[_pos];
    }
}
