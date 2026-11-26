using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace NoteDraw.Rendering.Markdown
{
    public static partial class CodeSyntaxHighlighter
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
    }
}
