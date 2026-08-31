namespace OpenCommander.Text.Syntax;

/// <summary>
/// Maps a file name to the <see cref="SyntaxRules"/> that colour it, by extension. A file whose
/// extension is not listed simply gets no colouring - never an error.
/// </summary>
public static class SyntaxRegistry
{
    /// <summary>The rules for a file, or <see langword="null"/> when its type is not recognised.</summary>
    /// <param name="path">The file path or bare name; case does not matter.</param>
    /// <returns>The rules, or <see langword="null"/>.</returns>
    public static SyntaxRules? ForPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        string extension;
        try
        {
            extension = Path.GetExtension(path);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return Table.TryGetValue(extension, out SyntaxRules? rules) ? rules : null;
    }

    /// <summary>Every language shipped, for the tests that walk them.</summary>
    public static IReadOnlyCollection<SyntaxRules> All =>
        Table.Values.Distinct().ToArray();

    // ------------------------------------------------------------------ the languages

    private static readonly SyntaxRules CSharp = new()
    {
        Name = "C#",
        LineComments = ["//"],
        BlockCommentOpen = "/*",
        BlockCommentClose = "*/",
        VerbatimStrings = true,
        HashPreprocessor = true,
        Keywords = SyntaxRules.Words(caseSensitive: true,
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "record", "ref", "return", "sbyte",
            "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
            "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
            "ushort", "using", "var", "virtual", "void", "volatile", "while",
            "async", "await", "dynamic", "get", "set", "init", "nameof", "partial", "required",
            "when", "where", "with", "yield"),
    };

    private static readonly SyntaxRules JavaScript = new()
    {
        Name = "JavaScript",
        LineComments = ["//"],
        BlockCommentOpen = "/*",
        BlockCommentClose = "*/",
        TemplateLiterals = true,
        Keywords = SyntaxRules.Words(caseSensitive: true,
            "abstract", "any", "as", "async", "await", "boolean", "break", "case", "catch", "class",
            "const", "continue", "debugger", "declare", "default", "delete", "do", "else", "enum",
            "export", "extends", "false", "finally", "for", "from", "function", "get", "if",
            "implements", "import", "in", "instanceof", "interface", "keyof", "let", "namespace",
            "new", "null", "number", "of", "private", "protected", "public", "readonly", "return",
            "set", "static", "string", "super", "switch", "this", "throw", "true", "try", "type",
            "typeof", "undefined", "var", "void", "while", "with", "yield"),
    };

    private static readonly SyntaxRules Json = new()
    {
        Name = "JSON",
        // Strict JSON has no comments, but people open jsonc and json-with-comments constantly,
        // and colouring a comment never hurts a strict file.
        LineComments = ["//"],
        BlockCommentOpen = "/*",
        BlockCommentClose = "*/",
        SingleQuoteStrings = false,
        JsonKeys = true,
        Keywords = SyntaxRules.Words(caseSensitive: true, "true", "false", "null"),
    };

    private static readonly SyntaxRules Sql = new()
    {
        Name = "SQL",
        LineComments = ["--"],
        BlockCommentOpen = "/*",
        BlockCommentClose = "*/",
        CaseSensitive = false,
        BackslashEscapes = false,
        DoubledQuoteEscapes = true,
        Keywords = SyntaxRules.Words(caseSensitive: false,
            "add", "all", "alter", "and", "any", "as", "asc", "backup", "begin", "between", "by",
            "case", "check", "column", "commit", "constraint", "create", "cross", "database",
            "declare", "default", "delete", "desc", "distinct", "drop", "else", "end", "exec",
            "exists", "foreign", "from", "full", "function", "group", "having", "if", "in", "index",
            "inner", "insert", "into", "is", "join", "key", "left", "like", "limit", "merge", "not",
            "null", "offset", "on", "or", "order", "outer", "over", "partition", "primary",
            "procedure", "return", "returns", "right", "rollback", "rownum", "select", "set",
            "table", "then", "top", "transaction", "trigger", "union", "unique", "update", "values",
            "view", "when", "where", "while", "with",
            "bigint", "binary", "bit", "char", "date", "datetime", "datetime2", "decimal", "float",
            "int", "money", "nchar", "ntext", "nvarchar", "numeric", "real", "smallint", "text",
            "time", "tinyint", "uniqueidentifier", "varbinary", "varchar"),
    };

    private static readonly SyntaxRules CFamily = new()
    {
        Name = "C/C++",
        LineComments = ["//"],
        BlockCommentOpen = "/*",
        BlockCommentClose = "*/",
        HashPreprocessor = true,
        Keywords = SyntaxRules.Words(caseSensitive: true,
            "alignas", "alignof", "auto", "bool", "break", "case", "catch", "char", "class",
            "const", "constexpr", "continue", "decltype", "default", "delete", "do", "double",
            "else", "enum", "explicit", "extern", "false", "float", "for", "friend", "goto", "if",
            "inline", "int", "long", "mutable", "namespace", "new", "noexcept", "nullptr",
            "operator", "override", "private", "protected", "public", "register", "return",
            "short", "signed", "sizeof", "static", "struct", "switch", "template", "this",
            "throw", "true", "try", "typedef", "typename", "union", "unsigned", "using",
            "virtual", "void", "volatile", "while",
            "int8_t", "int16_t", "int32_t", "int64_t", "uint8_t", "uint16_t", "uint32_t",
            "uint64_t", "size_t", "wchar_t"),
    };

    private static readonly SyntaxRules Java = new()
    {
        Name = "Java",
        LineComments = ["//"],
        BlockCommentOpen = "/*",
        BlockCommentClose = "*/",
        Keywords = SyntaxRules.Words(caseSensitive: true,
            "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class",
            "const", "continue", "default", "do", "double", "else", "enum", "extends", "false",
            "final", "finally", "float", "for", "goto", "if", "implements", "import", "instanceof",
            "int", "interface", "long", "native", "new", "null", "package", "private", "protected",
            "public", "record", "return", "sealed", "short", "static", "strictfp", "super",
            "switch", "synchronized", "this", "throw", "throws", "transient", "true", "try", "var",
            "void", "volatile", "while", "yield"),
    };

    private static readonly SyntaxRules Python = new()
    {
        Name = "Python",
        LineComments = ["#"],
        TripleQuotedStrings = true,
        Keywords = SyntaxRules.Words(caseSensitive: true,
            "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del",
            "elif", "else", "except", "False", "finally", "for", "from", "global", "if", "import",
            "in", "is", "lambda", "None", "nonlocal", "not", "or", "pass", "raise", "return",
            "self", "True", "try", "while", "with", "yield"),
    };

    private static readonly SyntaxRules PowerShell = new()
    {
        Name = "PowerShell",
        LineComments = ["#"],
        BlockCommentOpen = "<#",
        BlockCommentClose = "#>",
        CaseSensitive = false,

        // PowerShell escapes with a backtick, never a backslash, and doubles quotes instead -
        // otherwise every quoted Windows path ending in '\' swallows the rest of the line.
        BackslashEscapes = false,
        DoubledQuoteEscapes = true,
        Keywords = SyntaxRules.Words(caseSensitive: false,
            "begin", "break", "catch", "class", "continue", "data", "do", "dynamicparam", "else",
            "elseif", "end", "enum", "exit", "filter", "finally", "for", "foreach", "function",
            "hidden", "if", "in", "param", "process", "return", "static", "switch", "throw",
            "trap", "try", "until", "using", "while"),
    };

    /// <summary>
    /// Shell scripts and the hash-commented config family: <c>#</c> comments plus quoted strings
    /// is most of what those files need to become readable.
    /// </summary>
    private static readonly SyntaxRules HashConf = new()
    {
        Name = "Shell/Config",
        LineComments = ["#"],

        // Shell and YAML single quotes have no backslash escapes, so a quoted Windows path with
        // a trailing backslash still closes where it should.
        BackslashEscapes = false,
        Keywords = SyntaxRules.Words(caseSensitive: true,
            "case", "do", "done", "elif", "else", "esac", "exit", "export", "fi", "for",
            "function", "if", "in", "local", "return", "then", "until", "while",
            "true", "false", "null"),
    };

    private static readonly Dictionary<string, SyntaxRules> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = CSharp,
        [".csx"] = CSharp,

        [".js"] = JavaScript,
        [".jsx"] = JavaScript,
        [".mjs"] = JavaScript,
        [".cjs"] = JavaScript,
        [".ts"] = JavaScript,
        [".tsx"] = JavaScript,

        [".json"] = Json,
        [".jsonc"] = Json,

        [".sql"] = Sql,

        [".c"] = CFamily,
        [".h"] = CFamily,
        [".cpp"] = CFamily,
        [".hpp"] = CFamily,
        [".cc"] = CFamily,
        [".cxx"] = CFamily,
        [".hh"] = CFamily,

        [".java"] = Java,

        [".py"] = Python,
        [".pyw"] = Python,

        [".ps1"] = PowerShell,
        [".psm1"] = PowerShell,
        [".psd1"] = PowerShell,

        [".sh"] = HashConf,
        [".bash"] = HashConf,
        [".yaml"] = HashConf,
        [".yml"] = HashConf,
        [".toml"] = HashConf,
        [".conf"] = HashConf,
        [".gitignore"] = HashConf,
        [".editorconfig"] = HashConf,
    };
}
