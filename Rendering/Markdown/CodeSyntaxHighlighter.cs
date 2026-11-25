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
    public static class CodeSyntaxHighlighter
    {
        private enum SyntaxFamily
        {
            PlainText,
            CFamily,
            Python,
            Json,
            Markup,
            Css,
            Sql,
            Shell,
            YamlToml,
            Markdown
        }

        private sealed class LanguageProfile
        {
            public required string CanonicalId { get; init; }
            public required string DisplayName { get; init; }
            public required SyntaxFamily Family { get; init; }
            public bool CaseInsensitive { get; init; }
            public bool HashAsComment { get; init; }
            public bool HashAsPreprocessor { get; init; }
            public bool AllowBacktickStrings { get; init; }
            public bool AllowDollarPrefixedStrings { get; init; }
            public HashSet<string> Keywords { get; init; } = new(StringComparer.Ordinal);
            public HashSet<string> TypeNames { get; init; } = new(StringComparer.Ordinal);
            public HashSet<string> Constants { get; init; } = new(StringComparer.Ordinal);
        }

        private static readonly Dictionary<string, LanguageProfile> Profiles = BuildProfiles();
        private static readonly LanguageProfile PlainProfile = Profiles["text"];

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

        private static HashSet<string> Set(bool ignoreCase, params string[] items)
            => new(items, ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        private static Dictionary<string, LanguageProfile> BuildProfiles()
        {
            var map = new Dictionary<string, LanguageProfile>(StringComparer.OrdinalIgnoreCase);

            void Register(LanguageProfile profile, params string[] aliases)
            {
                map[profile.CanonicalId] = profile;
                foreach (string alias in aliases)
                    map[alias] = profile;
            }

            Register(new LanguageProfile
            {
                CanonicalId = "text",
                DisplayName = "Plain Text",
                Family = SyntaxFamily.PlainText
            }, "plaintext", "txt", "code", "");

            Register(new LanguageProfile
            {
                CanonicalId = "csharp",
                DisplayName = "C#",
                Family = SyntaxFamily.CFamily,
                HashAsPreprocessor = true,
                AllowDollarPrefixedStrings = true,
                Keywords = Set(false,
                    "abstract", "as", "async", "await", "base", "break", "case", "catch", "checked",
                    "class", "const", "continue", "default", "delegate", "do", "else", "enum", "event",
                    "explicit", "extern", "finally", "fixed", "for", "foreach", "get", "global", "goto",
                    "if", "implicit", "in", "init", "interface", "internal", "is", "lock", "namespace",
                    "new", "operator", "out", "override", "params", "partial", "private", "protected",
                    "public", "readonly", "record", "ref", "required", "return", "sealed", "set",
                    "sizeof", "stackalloc", "static", "struct", "switch", "this", "throw", "try",
                    "typeof", "unchecked", "unsafe", "using", "var", "virtual", "void", "volatile",
                    "when", "where", "while", "with", "yield", "and", "or", "not"),
                TypeNames = Set(false,
                    "bool", "byte", "char", "decimal", "double", "dynamic", "float", "int", "long",
                    "nint", "nuint", "object", "sbyte", "short", "string", "uint", "ulong", "ushort",
                    "String", "Task", "List", "Dictionary", "IEnumerable", "IReadOnlyList", "Action",
                    "Func", "Exception", "Console", "Math", "Path", "File", "Directory"),
                Constants = Set(false, "true", "false", "null", "default")
            }, "cs", "c#", "dotnet");

            Register(new LanguageProfile
            {
                CanonicalId = "python",
                DisplayName = "Python",
                Family = SyntaxFamily.Python,
                HashAsComment = true,
                Keywords = Set(false,
                    "and", "as", "assert", "async", "await", "break", "case", "class", "continue",
                    "def", "del", "elif", "else", "except", "finally", "for", "from", "global", "if",
                    "import", "in", "is", "lambda", "match", "nonlocal", "not", "or", "pass", "raise",
                    "return", "try", "while", "with", "yield"),
                TypeNames = Set(false,
                    "int", "float", "str", "bool", "list", "dict", "tuple", "set", "bytes", "object",
                    "range", "len", "print", "enumerate", "zip", "map", "filter", "isinstance", "self", "cls"),
                Constants = Set(false, "True", "False", "None", "NotImplemented", "Ellipsis")
            }, "py", "py3");

            Register(new LanguageProfile
            {
                CanonicalId = "javascript",
                DisplayName = "JavaScript",
                Family = SyntaxFamily.CFamily,
                AllowBacktickStrings = true,
                Keywords = Set(false,
                    "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger",
                    "default", "delete", "do", "else", "export", "extends", "finally", "for", "from",
                    "function", "if", "import", "in", "instanceof", "let", "new", "of", "return",
                    "static", "super", "switch", "this", "throw", "try", "typeof", "var", "void",
                    "while", "with", "yield"),
                TypeNames = Set(false,
                    "Array", "Boolean", "Date", "Error", "Function", "JSON", "Map", "Math", "Number",
                    "Object", "Promise", "RegExp", "Set", "String", "Symbol", "console", "window", "document"),
                Constants = Set(false, "true", "false", "null", "undefined", "NaN", "Infinity")
            }, "js", "jsx", "mjs", "cjs");

            Register(new LanguageProfile
            {
                CanonicalId = "typescript",
                DisplayName = "TypeScript",
                Family = SyntaxFamily.CFamily,
                AllowBacktickStrings = true,
                Keywords = Set(false,
                    "abstract", "as", "async", "await", "break", "case", "catch", "class", "const",
                    "continue", "declare", "default", "delete", "do", "else", "enum", "export", "extends",
                    "finally", "for", "from", "function", "implements", "import", "in", "infer", "if",
                    "instanceof", "interface", "is", "keyof", "let", "namespace", "new", "of", "private",
                    "protected", "public", "readonly", "return", "satisfies", "static", "super", "switch",
                    "this", "throw", "try", "type", "typeof", "var", "void", "while", "with", "yield"),
                TypeNames = Set(false,
                    "any", "bigint", "boolean", "never", "number", "object", "string", "symbol",
                    "unknown", "void", "Array", "Promise", "Record", "Partial", "Pick", "Omit", "Map", "Set"),
                Constants = Set(false, "true", "false", "null", "undefined", "NaN", "Infinity")
            }, "ts", "tsx");

            Register(new LanguageProfile
            {
                CanonicalId = "c",
                DisplayName = "C",
                Family = SyntaxFamily.CFamily,
                HashAsPreprocessor = true,
                Keywords = Set(false,
                    "auto", "break", "case", "const", "continue", "default", "do", "else", "enum",
                    "extern", "for", "goto", "if", "inline", "register", "restrict", "return", "sizeof",
                    "static", "struct", "switch", "typedef", "union", "volatile", "while"),
                TypeNames = Set(false,
                    "char", "double", "float", "int", "long", "short", "signed", "unsigned", "void",
                    "size_t", "uint8_t", "uint16_t", "uint32_t", "uint64_t", "int32_t", "int64_t", "bool", "FILE"),
                Constants = Set(false, "NULL", "true", "false", "EOF")
            }, "h");

            Register(new LanguageProfile
            {
                CanonicalId = "cpp",
                DisplayName = "C++",
                Family = SyntaxFamily.CFamily,
                HashAsPreprocessor = true,
                Keywords = Set(false,
                    "alignas", "alignof", "and", "auto", "break", "case", "catch", "class", "co_await",
                    "co_return", "co_yield", "concept", "const", "consteval", "constexpr", "constinit",
                    "continue", "decltype", "default", "delete", "do", "else", "enum", "explicit",
                    "export", "extern", "for", "friend", "goto", "if", "inline", "mutable", "namespace",
                    "new", "noexcept", "not", "operator", "or", "override", "private", "protected",
                    "public", "requires", "return", "sizeof", "static", "static_cast", "dynamic_cast",
                    "reinterpret_cast", "const_cast", "struct", "switch", "template", "this", "throw",
                    "try", "typedef", "typename", "union", "using", "virtual", "volatile", "while"),
                TypeNames = Set(false,
                    "bool", "char", "double", "float", "int", "long", "short", "signed", "unsigned",
                    "void", "wchar_t", "size_t", "std", "string", "vector", "map", "unordered_map",
                    "unique_ptr", "shared_ptr", "optional", "span"),
                Constants = Set(false, "true", "false", "nullptr", "NULL")
            }, "c++", "cc", "cxx", "hpp", "hh");

            Register(new LanguageProfile
            {
                CanonicalId = "java",
                DisplayName = "Java",
                Family = SyntaxFamily.CFamily,
                Keywords = Set(false,
                    "abstract", "assert", "break", "case", "catch", "class", "continue", "default",
                    "do", "else", "enum", "extends", "final", "finally", "for", "if", "implements",
                    "import", "instanceof", "interface", "native", "new", "package", "private",
                    "protected", "public", "record", "return", "sealed", "static", "strictfp", "super",
                    "switch", "synchronized", "this", "throw", "throws", "transient", "try", "var",
                    "void", "volatile", "while", "yield"),
                TypeNames = Set(false,
                    "boolean", "byte", "char", "double", "float", "int", "long", "short", "String",
                    "Object", "List", "ArrayList", "Map", "HashMap", "Set", "Optional", "System"),
                Constants = Set(false, "true", "false", "null")
            });

            Register(new LanguageProfile
            {
                CanonicalId = "kotlin",
                DisplayName = "Kotlin",
                Family = SyntaxFamily.CFamily,
                Keywords = Set(false,
                    "abstract", "as", "break", "by", "catch", "class", "companion", "const", "constructor",
                    "continue", "data", "do", "else", "enum", "finally", "for", "fun", "if", "import",
                    "in", "init", "interface", "internal", "is", "lateinit", "object", "open", "operator",
                    "out", "override", "package", "private", "protected", "public", "return", "sealed",
                    "super", "suspend", "this", "throw", "try", "typealias", "val", "var", "when", "while"),
                TypeNames = Set(false,
                    "Any", "Boolean", "Byte", "Char", "Double", "Float", "Int", "List", "Long", "Map",
                    "Nothing", "Set", "Short", "String", "Unit"),
                Constants = Set(false, "true", "false", "null")
            }, "kt", "kts");

            Register(new LanguageProfile
            {
                CanonicalId = "rust",
                DisplayName = "Rust",
                Family = SyntaxFamily.CFamily,
                Keywords = Set(false,
                    "as", "async", "await", "break", "const", "continue", "crate", "dyn", "else", "enum",
                    "extern", "fn", "for", "if", "impl", "in", "let", "loop", "match", "mod", "move",
                    "mut", "pub", "ref", "return", "Self", "static", "struct", "super", "trait", "type",
                    "unsafe", "use", "where", "while"),
                TypeNames = Set(false,
                    "bool", "char", "f32", "f64", "i8", "i16", "i32", "i64", "i128", "isize", "str",
                    "u8", "u16", "u32", "u64", "u128", "usize", "Option", "Result", "String", "Vec", "Box", "self"),
                Constants = Set(false, "true", "false", "Some", "None", "Ok", "Err")
            }, "rs");

            Register(new LanguageProfile
            {
                CanonicalId = "go",
                DisplayName = "Go",
                Family = SyntaxFamily.CFamily,
                AllowBacktickStrings = true,
                Keywords = Set(false,
                    "break", "case", "chan", "const", "continue", "default", "defer", "else",
                    "fallthrough", "for", "func", "go", "goto", "if", "import", "interface", "map",
                    "package", "range", "return", "select", "struct", "switch", "type", "var"),
                TypeNames = Set(false,
                    "any", "bool", "byte", "complex64", "complex128", "error", "float32", "float64",
                    "int", "int8", "int16", "int32", "int64", "rune", "string", "uint", "uint8",
                    "uint16", "uint32", "uint64", "uintptr"),
                Constants = Set(false, "true", "false", "nil", "iota")
            }, "golang");

            Register(new LanguageProfile
            {
                CanonicalId = "json",
                DisplayName = "JSON",
                Family = SyntaxFamily.Json,
                Constants = Set(false, "true", "false", "null")
            }, "jsonc");

            Register(new LanguageProfile
            {
                CanonicalId = "html",
                DisplayName = "HTML",
                Family = SyntaxFamily.Markup
            }, "htm");

            Register(new LanguageProfile
            {
                CanonicalId = "xml",
                DisplayName = "XML",
                Family = SyntaxFamily.Markup
            }, "xaml", "axaml", "svg");

            Register(new LanguageProfile
            {
                CanonicalId = "css",
                DisplayName = "CSS",
                Family = SyntaxFamily.Css,
                Keywords = Set(true,
                    "!important", "auto", "block", "flex", "grid", "inline", "inline-block", "none",
                    "absolute", "relative", "fixed", "sticky", "solid", "dashed", "inherit", "initial",
                    "center", "left", "right", "transparent")
            }, "scss", "less");

            Register(new LanguageProfile
            {
                CanonicalId = "sql",
                DisplayName = "SQL",
                Family = SyntaxFamily.Sql,
                CaseInsensitive = true,
                Keywords = Set(true,
                    "ADD", "ALL", "ALTER", "AND", "AS", "ASC", "BETWEEN", "BY", "CASE", "CREATE",
                    "DELETE", "DESC", "DISTINCT", "DROP", "ELSE", "END", "EXISTS", "FROM", "FULL",
                    "GROUP", "HAVING", "IN", "INDEX", "INNER", "INSERT", "INTO", "IS", "JOIN", "KEY",
                    "LEFT", "LIKE", "LIMIT", "NOT", "OFFSET", "ON", "OR", "ORDER", "OUTER", "PRIMARY",
                    "REFERENCES", "RIGHT", "SELECT", "SET", "TABLE", "THEN", "UNION", "UNIQUE",
                    "UPDATE", "VALUES", "VIEW", "WHEN", "WHERE", "WITH"),
                TypeNames = Set(true,
                    "BIGINT", "BIT", "BLOB", "BOOLEAN", "CHAR", "DATE", "DATETIME", "DECIMAL",
                    "DOUBLE", "FLOAT", "INT", "INTEGER", "JSON", "NUMERIC", "REAL", "SMALLINT",
                    "TEXT", "TIMESTAMP", "UUID", "VARCHAR"),
                Constants = Set(true, "TRUE", "FALSE", "NULL")
            }, "sqlite", "postgres", "postgresql", "mysql", "tsql");

            Register(new LanguageProfile
            {
                CanonicalId = "bash",
                DisplayName = "Bash",
                Family = SyntaxFamily.Shell,
                HashAsComment = true,
                Keywords = Set(false,
                    "case", "do", "done", "elif", "else", "esac", "export", "fi", "for", "function",
                    "if", "in", "local", "readonly", "return", "select", "then", "until", "while",
                    "cd", "echo", "exit", "grep", "ls", "mkdir", "rm", "cp", "mv", "cat", "git", "dotnet"),
                Constants = Set(false, "true", "false")
            }, "sh", "shell", "zsh");

            Register(new LanguageProfile
            {
                CanonicalId = "powershell",
                DisplayName = "PowerShell",
                Family = SyntaxFamily.Shell,
                CaseInsensitive = true,
                HashAsComment = true,
                Keywords = Set(true,
                    "begin", "break", "catch", "class", "continue", "data", "do", "else", "elseif",
                    "end", "exit", "filter", "finally", "for", "foreach", "from", "function", "if",
                    "in", "param", "process", "return", "switch", "throw", "trap", "try", "until", "while"),
                TypeNames = Set(true, "string", "int", "bool", "double", "array", "hashtable", "void"),
                Constants = Set(true, "$true", "$false", "$null")
            }, "pwsh", "ps1");

            Register(new LanguageProfile
            {
                CanonicalId = "yaml",
                DisplayName = "YAML",
                Family = SyntaxFamily.YamlToml,
                CaseInsensitive = true,
                HashAsComment = true,
                Constants = Set(true, "true", "false", "null", "yes", "no")
            }, "yml", "toml", "ini");

            Register(new LanguageProfile
            {
                CanonicalId = "markdown",
                DisplayName = "Markdown",
                Family = SyntaxFamily.Markdown
            }, "md");

            return map;
        }

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
