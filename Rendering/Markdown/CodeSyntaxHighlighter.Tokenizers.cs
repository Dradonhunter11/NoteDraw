using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace NoteDraw.Rendering.Markdown
{
    public static partial class CodeSyntaxHighlighter
    {
        private static bool StartsWithAt(string text, int index, string value)
            => index + value.Length <= text.Length &&
               string.CompareOrdinal(text, index, value, 0, value.Length) == 0;

        private static bool IsLineStart(string text, int index)
        {
            int j = index - 1;
            while (j >= 0 && (text[j] == ' ' || text[j] == '\t'))
                j--;
            return j < 0 || text[j] == '\n';
        }

        private static int ScanLineEnd(string text, int start)
        {
            int i = start;
            while (i < text.Length && text[i] != '\n')
                i++;
            return i;
        }

        private static int ScanQuotedString(string text, int start, char quote, bool allowMultiline)
        {
            int i = start + 1;
            while (i < text.Length)
            {
                char ch = text[i];
                if (ch == '\\' && i + 1 < text.Length)
                {
                    i += 2;
                    continue;
                }
                if (!allowMultiline && (ch == '\n' || ch == '\r'))
                    break;
                if (ch == quote)
                {
                    i++;
                    break;
                }
                i++;
            }
            return i;
        }

        private static int ScanVerbatimString(string text, int start)
        {
            int i = start + 2; // @"
            while (i < text.Length)
            {
                if (text[i] == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        i += 2;
                        continue;
                    }
                    i++;
                    break;
                }
                i++;
            }
            return i;
        }

        private static int ScanTripleQuotedString(string text, int start, string delim)
        {
            int i = start + 3;
            while (i < text.Length)
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    i += 2;
                    continue;
                }
                if (StartsWithAt(text, i, delim))
                    return i + 3;
                i++;
            }
            return text.Length;
        }

        private static int ScanNumber(string text, int start)
        {
            int i = start;
            if (StartsWithAt(text, i, "0x") || StartsWithAt(text, i, "0X") ||
                StartsWithAt(text, i, "0b") || StartsWithAt(text, i, "0B"))
            {
                i += 2;
                while (i < text.Length && (IsHexDigit(text[i]) || text[i] == '_'))
                    i++;
            }
            else
            {
                bool seenDot = false;
                while (i < text.Length)
                {
                    char c = text[i];
                    if (char.IsDigit(c) || c == '_')
                        i++;
                    else if (c == '.' && !seenDot && i + 1 < text.Length && char.IsDigit(text[i + 1]))
                    {
                        seenDot = true;
                        i++;
                    }
                    else
                        break;
                }
            }

            // Optional type/exponent suffix
            if (i < text.Length && text[i] is 'f' or 'F' or 'd' or 'D' or 'm' or 'M' or 'l' or 'L' or 'u' or 'U')
                i++;
            return i;
        }

        private static bool IsHexDigit(char c)
            => char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
        private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';

        private static char NextNonWhitespace(string text, int index)
        {
            int j = index;
            while (j < text.Length && (text[j] == ' ' || text[j] == '\t' || text[j] == '\r' || text[j] == '\n'))
                j++;
            return j < text.Length ? text[j] : '\0';
        }

        private static List<CodeToken> TokenizeImperativeOrShell(string code, LanguageProfile profile)
        {
            var tokens = new List<CodeToken>();
            int i = 0;

            while (i < code.Length)
            {
                char ch = code[i];

                // Whitespace
                if (char.IsWhiteSpace(ch))
                {
                    int start = i;
                    while (i < code.Length && char.IsWhiteSpace(code[i]))
                        i++;
                    tokens.Add(new(code[start..i], CodeTokenKind.Plain));
                    continue;
                }

                // Line comments: // (C-family), -- (SQL), # (Python / Shell)
                if ((profile.Family != SyntaxFamily.Sql && StartsWithAt(code, i, "//")) ||
                    (profile.Family == SyntaxFamily.Sql && StartsWithAt(code, i, "--")) ||
                    (profile.HashAsComment && ch == '#'))
                {
                    int end = ScanLineEnd(code, i);
                    tokens.Add(new(code[i..end], CodeTokenKind.Comment));
                    i = end;
                    continue;
                }

                // Block comments /* ... */ or PowerShell <# ... #>
                if (StartsWithAt(code, i, "/*"))
                {
                    int close = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int end = close >= 0 ? close + 2 : code.Length;
                    tokens.Add(new(code[i..end], CodeTokenKind.Comment));
                    i = end;
                    continue;
                }

                if (profile.CanonicalId == "powershell" && StartsWithAt(code, i, "<#"))
                {
                    int close = code.IndexOf("#>", i + 2, StringComparison.Ordinal);
                    int end = close >= 0 ? close + 2 : code.Length;
                    tokens.Add(new(code[i..end], CodeTokenKind.Comment));
                    i = end;
                    continue;
                }

                // Preprocessor directives (#include, #if, #region, etc.)
                if (profile.HashAsPreprocessor && ch == '#' && IsLineStart(code, i))
                {
                    int end = ScanLineEnd(code, i);
                    tokens.Add(new(code[i..end], CodeTokenKind.Preprocessor));
                    i = end;
                    continue;
                }

                // Python triple quotes or prefixed strings (f"...", r'...', $@"...", $"...")
                if (profile.Family == SyntaxFamily.Python && (StartsWithAt(code, i, "\"\"\"") || StartsWithAt(code, i, "'''")))
                {
                    string delim = code.Substring(i, 3);
                    int end = ScanTripleQuotedString(code, i, delim);
                    tokens.Add(new(code[i..end], CodeTokenKind.String));
                    i = end;
                    continue;
                }

                if (profile.AllowDollarPrefixedStrings && StartsWithAt(code, i, "@\""))
                {
                    int end = ScanVerbatimString(code, i);
                    tokens.Add(new(code[i..end], CodeTokenKind.String));
                    i = end;
                    continue;
                }

                if (profile.AllowDollarPrefixedStrings && (StartsWithAt(code, i, "$@\"") || StartsWithAt(code, i, "@$\"")))
                {
                    int end = ScanVerbatimString(code, i + 1);
                    tokens.Add(new(code[i..end], CodeTokenKind.String));
                    i = end;
                    continue;
                }

                if (profile.AllowDollarPrefixedStrings && StartsWithAt(code, i, "$\""))
                {
                    int end = ScanQuotedString(code, i + 1, '"', allowMultiline: false);
                    tokens.Add(new(code[i..end], CodeTokenKind.String));
                    i = end;
                    continue;
                }

                if (profile.Family == SyntaxFamily.Python &&
                    ch is 'f' or 'F' or 'r' or 'R' or 'b' or 'B' or 'u' or 'U' &&
                    i + 1 < code.Length &&
                    code[i + 1] is '"' or '\'')
                {
                    char quote = code[i + 1];
                    int end = StartsWithAt(code, i + 1, new string(quote, 3))
                        ? ScanTripleQuotedString(code, i + 1, new string(quote, 3))
                        : ScanQuotedString(code, i + 1, quote, allowMultiline: false);
                    tokens.Add(new(code[i..end], CodeTokenKind.String));
                    i = end;
                    continue;
                }

                // Standard quoted / backtick strings
                if (ch is '"' or '\'' || (profile.AllowBacktickStrings && ch == '`'))
                {
                    bool multi = profile.AllowBacktickStrings && ch == '`';
                    int end = ScanQuotedString(code, i, ch, allowMultiline: multi);
                    tokens.Add(new(code[i..end], CodeTokenKind.String));
                    i = end;
                    continue;
                }

                // Decorators / Attributes (@decorator or PowerShell / Bash $variable)
                if (ch == '@' && i + 1 < code.Length && IsIdentStart(code[i + 1]))
                {
                    int start = i++;
                    while (i < code.Length && (IsIdentPart(code[i]) || code[i] == '.'))
                        i++;
                    tokens.Add(new(code[start..i], CodeTokenKind.Attribute));
                    continue;
                }

                if (profile.Family == SyntaxFamily.Shell && ch == '$')
                {
                    int start = i++;
                    if (i < code.Length && code[i] == '{')
                    {
                        int close = code.IndexOf('}', i + 1);
                        i = close >= 0 ? close + 1 : code.Length;
                    }
                    else
                    {
                        while (i < code.Length && IsIdentPart(code[i]))
                            i++;
                    }
                    string word = code[start..i];
                    var kind = profile.Constants.Contains(word) ? CodeTokenKind.Constant : CodeTokenKind.Property;
                    tokens.Add(new(word, kind));
                    continue;
                }

                // CLI long/short flags in shell scripts (--flag)
                if (profile.Family == SyntaxFamily.Shell &&
                    ch == '-' &&
                    (i == 0 || char.IsWhiteSpace(code[i - 1])) &&
                    i + 1 < code.Length &&
                    (char.IsLetter(code[i + 1]) || code[i + 1] == '-'))
                {
                    int start = i++;
                    while (i < code.Length && (IsIdentPart(code[i]) || code[i] == '-'))
                        i++;
                    tokens.Add(new(code[start..i], CodeTokenKind.Property));
                    continue;
                }

                // Numbers
                if (char.IsDigit(ch))
                {
                    int end = ScanNumber(code, i);
                    tokens.Add(new(code[i..end], CodeTokenKind.Number));
                    i = end;
                    continue;
                }

                // Identifiers / Keywords / Types / Functions / Constants
                if (IsIdentStart(ch))
                {
                    int start = i++;
                    while (i < code.Length && IsIdentPart(code[i]))
                        i++;
                    string ident = code[start..i];

                    CodeTokenKind kind;
                    if (profile.Keywords.Contains(ident))
                        kind = CodeTokenKind.Keyword;
                    else if (profile.Constants.Contains(ident))
                        kind = CodeTokenKind.Constant;
                    else if (profile.TypeNames.Contains(ident))
                        kind = CodeTokenKind.TypeName;
                    else if (NextNonWhitespace(code, i) == '(')
                        kind = CodeTokenKind.Function;
                    else if (profile.Family == SyntaxFamily.CFamily && char.IsUpper(ident[0]) && ident.Any(char.IsLower))
                        kind = CodeTokenKind.TypeName;
                    else
                        kind = CodeTokenKind.Plain;

                    tokens.Add(new(ident, kind));
                    continue;
                }

                // Punctuation & Operators
                if (ch is '{' or '}' or '(' or ')' or '[' or ']' or ';' or ',' or ':')
                {
                    tokens.Add(new(code.Substring(i, 1), CodeTokenKind.Punctuation));
                    i++;
                    continue;
                }

                tokens.Add(new(code.Substring(i, 1), CodeTokenKind.Operator));
                i++;
            }

            return tokens;
        }

        private static List<CodeToken> TokenizeJson(string code, LanguageProfile profile)
        {
            var tokens = new List<CodeToken>();
            int i = 0;

            while (i < code.Length)
            {
                char ch = code[i];
                if (char.IsWhiteSpace(ch))
                {
                    int start = i;
                    while (i < code.Length && char.IsWhiteSpace(code[i])) i++;
                    tokens.Add(new(code[start..i], CodeTokenKind.Plain));
                    continue;
                }

                if (StartsWithAt(code, i, "//"))
                {
                    int end = ScanLineEnd(code, i);
                    tokens.Add(new(code[i..end], CodeTokenKind.Comment));
                    i = end;
                    continue;
                }

                if (StartsWithAt(code, i, "/*"))
                {
                    int close = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int end = close >= 0 ? close + 2 : code.Length;
                    tokens.Add(new(code[i..end], CodeTokenKind.Comment));
                    i = end;
                    continue;
                }

                if (ch == '"')
                {
                    int end = ScanQuotedString(code, i, '"', allowMultiline: false);
                    var kind = NextNonWhitespace(code, end) == ':' ? CodeTokenKind.Property : CodeTokenKind.String;
                    tokens.Add(new(code[i..end], kind));
                    i = end;
                    continue;
                }

                if (char.IsDigit(ch) || (ch == '-' && i + 1 < code.Length && char.IsDigit(code[i + 1])))
                {
                    int start = i;
                    if (ch == '-') i++;
                    int end = ScanNumber(code, i);
                    tokens.Add(new(code[start..end], CodeTokenKind.Number));
                    i = end;
                    continue;
                }

                if (IsIdentStart(ch))
                {
                    int start = i++;
                    while (i < code.Length && IsIdentPart(code[i])) i++;
                    string ident = code[start..i];
                    tokens.Add(new(ident, profile.Constants.Contains(ident) ? CodeTokenKind.Constant : CodeTokenKind.Plain));
                    continue;
                }

                tokens.Add(new(code.Substring(i, 1), CodeTokenKind.Punctuation));
                i++;
            }

            return tokens;
        }

        private static List<CodeToken> TokenizeMarkup(string code)
        {
            var tokens = new List<CodeToken>();
            int i = 0;

            while (i < code.Length)
            {
                if (StartsWithAt(code, i, "<!--"))
                {
                    int close = code.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    int end = close >= 0 ? close + 3 : code.Length;
                    tokens.Add(new(code[i..end], CodeTokenKind.Comment));
                    i = end;
                    continue;
                }

                if (code[i] == '<')
                {
                    int close = code.IndexOf('>', i + 1);
                    if (close >= 0)
                    {
                        TokenizeMarkupTag(code, i, close + 1, tokens);
                        i = close + 1;
                        continue;
                    }
                }

                int nextTag = code.IndexOf('<', i);
                int textEnd = nextTag >= 0 ? nextTag : code.Length;
                if (textEnd == i)
                    textEnd++;
                tokens.Add(new(code[i..textEnd], CodeTokenKind.Plain));
                i = textEnd;
            }

            return tokens;
        }

        private static void TokenizeMarkupTag(string code, int start, int end, List<CodeToken> tokens)
        {
            int i = start;
            int prefixEnd = i + 1;
            if (prefixEnd < end && code[prefixEnd] is '/' or '!' or '?')
                prefixEnd++;
            tokens.Add(new(code[i..prefixEnd], CodeTokenKind.Punctuation));
            i = prefixEnd;

            int tagStart = i;
            while (i < end && !char.IsWhiteSpace(code[i]) && code[i] is not ('>' or '/' or '?'))
                i++;
            if (i > tagStart)
                tokens.Add(new(code[tagStart..i], CodeTokenKind.Tag));

            while (i < end)
            {
                char c = code[i];
                if (char.IsWhiteSpace(c))
                {
                    int ws = i;
                    while (i < end && char.IsWhiteSpace(code[i])) i++;
                    tokens.Add(new(code[ws..i], CodeTokenKind.Plain));
                }
                else if (c is '>' or '/' or '=' or '?')
                {
                    tokens.Add(new(code.Substring(i, 1), CodeTokenKind.Punctuation));
                    i++;
                }
                else if (c is '"' or '\'')
                {
                    int qEnd = ScanQuotedString(code, i, c, allowMultiline: true);
                    qEnd = Math.Min(qEnd, end);
                    tokens.Add(new(code[i..qEnd], CodeTokenKind.String));
                    i = qEnd;
                }
                else
                {
                    int attrStart = i;
                    while (i < end && !char.IsWhiteSpace(code[i]) && code[i] is not ('=' or '>' or '/' or '"' or '\''))
                        i++;
                    tokens.Add(new(code[attrStart..i], CodeTokenKind.Property));
                }
            }
        }

        private static List<CodeToken> TokenizeCss(string code, LanguageProfile profile)
        {
            var tokens = new List<CodeToken>();
            int i = 0;

            while (i < code.Length)
            {
                char ch = code[i];
                if (char.IsWhiteSpace(ch))
                {
                    int start = i;
                    while (i < code.Length && char.IsWhiteSpace(code[i])) i++;
                    tokens.Add(new(code[start..i], CodeTokenKind.Plain));
                    continue;
                }

                if (StartsWithAt(code, i, "/*"))
                {
                    int close = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int end = close >= 0 ? close + 2 : code.Length;
                    tokens.Add(new(code[i..end], CodeTokenKind.Comment));
                    i = end;
                    continue;
                }

                if (StartsWithAt(code, i, "//"))
                {
                    int end = ScanLineEnd(code, i);
                    tokens.Add(new(code[i..end], CodeTokenKind.Comment));
                    i = end;
                    continue;
                }

                if (ch is '"' or '\'')
                {
                    int end = ScanQuotedString(code, i, ch, allowMultiline: false);
                    tokens.Add(new(code[i..end], CodeTokenKind.String));
                    i = end;
                    continue;
                }

                if (ch == '@' && i + 1 < code.Length && char.IsLetter(code[i + 1]))
                {
                    int start = i++;
                    while (i < code.Length && (IsIdentPart(code[i]) || code[i] == '-')) i++;
                    tokens.Add(new(code[start..i], CodeTokenKind.Keyword));
                    continue;
                }

                if (ch == '#' && i + 1 < code.Length && IsHexDigit(code[i + 1]))
                {
                    int start = i++;
                    while (i < code.Length && IsHexDigit(code[i])) i++;
                    tokens.Add(new(code[start..i], CodeTokenKind.Number));
                    continue;
                }

                if (char.IsDigit(ch))
                {
                    int start = i;
                    i = ScanNumber(code, i);
                    while (i < code.Length && (char.IsLetter(code[i]) || code[i] == '%')) i++;
                    tokens.Add(new(code[start..i], CodeTokenKind.Number));
                    continue;
                }

                if (IsIdentStart(ch) || ch == '.')
                {
                    int start = i++;
                    while (i < code.Length && (IsIdentPart(code[i]) || code[i] == '-')) i++;
                    string ident = code[start..i];

                    CodeTokenKind kind;
                    if (ident.StartsWith('.'))
                        kind = CodeTokenKind.TypeName;
                    else if (NextNonWhitespace(code, i) == ':')
                        kind = CodeTokenKind.Property;
                    else if (NextNonWhitespace(code, i) == '(')
                        kind = CodeTokenKind.Function;
                    else if (profile.Keywords.Contains(ident))
                        kind = CodeTokenKind.Keyword;
                    else
                        kind = CodeTokenKind.Tag;

                    tokens.Add(new(ident, kind));
                    continue;
                }

                tokens.Add(new(code.Substring(i, 1), CodeTokenKind.Punctuation));
                i++;
            }

            return tokens;
        }

        private static List<CodeToken> TokenizeYamlToml(string code, LanguageProfile profile)
        {
            var tokens = new List<CodeToken>();
            int i = 0;

            while (i < code.Length)
            {
                if (code[i] == '#')
                {
                    int end = ScanLineEnd(code, i);
                    tokens.Add(new(code[i..end], CodeTokenKind.Comment));
                    i = end;
                    continue;
                }

                if (char.IsWhiteSpace(code[i]))
                {
                    int start = i;
                    while (i < code.Length && char.IsWhiteSpace(code[i])) i++;
                    tokens.Add(new(code[start..i], CodeTokenKind.Plain));
                    continue;
                }

                if (code[i] is '"' or '\'')
                {
                    int end = ScanQuotedString(code, i, code[i], allowMultiline: false);
                    tokens.Add(new(code[i..end], CodeTokenKind.String));
                    i = end;
                    continue;
                }

                if (code[i] == '[' && IsLineStart(code, i))
                {
                    int close = code.IndexOf(']', i + 1);
                    if (close > i)
                    {
                        tokens.Add(new(code[i..(close + 1)], CodeTokenKind.Tag));
                        i = close + 1;
                        continue;
                    }
                }

                if (char.IsDigit(code[i]))
                {
                    int end = ScanNumber(code, i);
                    tokens.Add(new(code[i..end], CodeTokenKind.Number));
                    i = end;
                    continue;
                }

                if (IsIdentStart(code[i]))
                {
                    int start = i++;
                    while (i < code.Length && (IsIdentPart(code[i]) || code[i] is '-' or '.')) i++;
                    string ident = code[start..i];
                    char next = NextNonWhitespace(code, i);
                    var kind = next is ':' or '='
                        ? CodeTokenKind.Property
                        : profile.Constants.Contains(ident) ? CodeTokenKind.Constant : CodeTokenKind.Plain;
                    tokens.Add(new(ident, kind));
                    continue;
                }

                tokens.Add(new(code.Substring(i, 1), CodeTokenKind.Punctuation));
                i++;
            }

            return tokens;
        }

        private static List<CodeToken> TokenizeMarkdownSnippet(string code)
        {
            var tokens = new List<CodeToken>();
            int i = 0;
            while (i < code.Length)
            {
                if (code[i] == '#' && IsLineStart(code, i))
                {
                    int end = ScanLineEnd(code, i);
                    tokens.Add(new(code[i..end], CodeTokenKind.Tag));
                    i = end;
                    continue;
                }
                if (code[i] == '`')
                {
                    int end = ScanQuotedString(code, i, '`', allowMultiline: false);
                    tokens.Add(new(code[i..end], CodeTokenKind.String));
                    i = end;
                    continue;
                }
                int start = i++;
                while (i < code.Length && code[i] != '#' && code[i] != '`') i++;
                tokens.Add(new(code[start..i], CodeTokenKind.Plain));
            }
            return tokens;
        }
    }
}
