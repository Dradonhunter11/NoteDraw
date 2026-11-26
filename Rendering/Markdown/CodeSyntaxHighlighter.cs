using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace NoteDraw.Rendering.Markdown
{
    public enum CodeTokenKind
    {
        Plain,
        Keyword,
        TypeName,
        String,
        Comment,
        Number,
        Function,
        Operator,
        Punctuation,
        Preprocessor,
        Attribute,
        Tag,
        Property,
        Constant
    }

    public readonly record struct CodeToken(string Text, CodeTokenKind Kind);

    /// <summary>
    /// Fast zero-dependency lexer and VS Code-inspired Dark+/Light+ theme palette
    /// for fenced Markdown code blocks across common programming, markup, data, and shell languages.
    /// </summary>
    public static partial class CodeSyntaxHighlighter
    {
        private static readonly Dictionary<CodeTokenKind, IBrush> DarkBrushes = new()
        {
            [CodeTokenKind.Plain] = Brush("#D4D4D4"),
            [CodeTokenKind.Keyword] = Brush("#C586C0"),
            [CodeTokenKind.TypeName] = Brush("#4EC9B0"),
            [CodeTokenKind.String] = Brush("#CE9178"),
            [CodeTokenKind.Comment] = Brush("#6A9955"),
            [CodeTokenKind.Number] = Brush("#B5CEA8"),
            [CodeTokenKind.Function] = Brush("#DCDCAA"),
            [CodeTokenKind.Operator] = Brush("#D4D4D4"),
            [CodeTokenKind.Punctuation] = Brush("#9CDCFE"),
            [CodeTokenKind.Preprocessor] = Brush("#9B9B9B"),
            [CodeTokenKind.Attribute] = Brush("#DCDCAA"),
            [CodeTokenKind.Tag] = Brush("#569CD6"),
            [CodeTokenKind.Property] = Brush("#9CDCFE"),
            [CodeTokenKind.Constant] = Brush("#569CD6")
        };

        private static readonly Dictionary<CodeTokenKind, IBrush> LightBrushes = new()
        {
            [CodeTokenKind.Plain] = Brush("#1E1E1E"),
            [CodeTokenKind.Keyword] = Brush("#AF00DB"),
            [CodeTokenKind.TypeName] = Brush("#267F99"),
            [CodeTokenKind.String] = Brush("#A31515"),
            [CodeTokenKind.Comment] = Brush("#008000"),
            [CodeTokenKind.Number] = Brush("#098658"),
            [CodeTokenKind.Function] = Brush("#795E26"),
            [CodeTokenKind.Operator] = Brush("#383A42"),
            [CodeTokenKind.Punctuation] = Brush("#0451A5"),
            [CodeTokenKind.Preprocessor] = Brush("#6B7280"),
            [CodeTokenKind.Attribute] = Brush("#795E26"),
            [CodeTokenKind.Tag] = Brush("#800000"),
            [CodeTokenKind.Property] = Brush("#0451A5"),
            [CodeTokenKind.Constant] = Brush("#0000FF")
        };

        private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

        public static string NormalizeLanguage(string? rawLanguage)
        {
            if (string.IsNullOrWhiteSpace(rawLanguage))
                return "text";

            string firstToken = rawLanguage.Trim()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;

            if (Profiles.TryGetValue(firstToken, out var profile))
                return profile.CanonicalId;

            return firstToken.ToLowerInvariant();
        }

        public static string GetDisplayName(string? rawLanguage)
        {
            if (string.IsNullOrWhiteSpace(rawLanguage))
                return "Plain Text";

            string firstToken = rawLanguage.Trim()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;

            if (Profiles.TryGetValue(firstToken, out var profile))
                return profile.DisplayName;

            return firstToken.Length <= 4 ? firstToken.ToUpperInvariant() : char.ToUpperInvariant(firstToken[0]) + firstToken[1..];
        }

        public static IBrush GetTokenBrush(CodeTokenKind kind, bool isDarkTheme)
        {
            var palette = isDarkTheme ? DarkBrushes : LightBrushes;
            return palette.TryGetValue(kind, out var brush) ? brush : palette[CodeTokenKind.Plain];
        }

        public static FontStyle GetTokenFontStyle(CodeTokenKind kind)
            => kind == CodeTokenKind.Comment ? FontStyle.Italic : FontStyle.Normal;

        public static FontWeight GetTokenFontWeight(CodeTokenKind kind)
            => kind is CodeTokenKind.Keyword or CodeTokenKind.Tag ? FontWeight.SemiBold : FontWeight.Normal;

        public static IReadOnlyList<CodeToken> Tokenize(string code, string? language)
        {
            if (string.IsNullOrEmpty(code))
                return Array.Empty<CodeToken>();

            string firstToken = string.IsNullOrWhiteSpace(language)
                ? string.Empty
                : language.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

            var profile = Profiles.TryGetValue(firstToken, out var found) ? found : PlainProfile;
            if (profile.Family == SyntaxFamily.PlainText && !string.IsNullOrWhiteSpace(firstToken))
            {
                // Unknown named language: apply generic C-like/hash identifier + string + number highlighting
                profile = Profiles["csharp"];
            }

            if (profile.Family == SyntaxFamily.PlainText)
                return new[] { new CodeToken(code, CodeTokenKind.Plain) };

            var raw = profile.Family switch
            {
                SyntaxFamily.Json => TokenizeJson(code, profile),
                SyntaxFamily.Markup => TokenizeMarkup(code),
                SyntaxFamily.Css => TokenizeCss(code, profile),
                SyntaxFamily.YamlToml => TokenizeYamlToml(code, profile),
                SyntaxFamily.Markdown => TokenizeMarkdownSnippet(code),
                _ => TokenizeImperativeOrShell(code, profile)
            };

            return CoalesceTokens(raw);
        }

        private static IReadOnlyList<CodeToken> CoalesceTokens(List<CodeToken> input)
        {
            if (input.Count <= 1)
                return input;

            var merged = new List<CodeToken>(input.Count);
            CodeToken current = input[0];
            for (int i = 1; i < input.Count; i++)
            {
                var next = input[i];
                if (next.Kind == current.Kind)
                    current = current with { Text = current.Text + next.Text };
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }
            merged.Add(current);
            return merged;
        }
    }
}
