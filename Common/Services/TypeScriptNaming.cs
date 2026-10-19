using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Auxil.Extensions;

namespace Catalyst.Common.Services;

/// <summary>
/// Naming and quoting rules shared by the TypeScript generator: identifier sanitising, type names
/// for component schemas, and literal/key quoting.
/// </summary>
internal static class TypeScriptNaming
{
    private static readonly JsonSerializerOptions StringLiteralOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly HashSet<string> ReservedWords =
    [
        "break", "case", "catch", "class", "const", "continue", "debugger", "default",
        "delete", "do", "else", "export", "extends", "finally", "for", "function",
        "if", "import", "in", "instanceof", "new", "return", "super", "switch",
        "this", "throw", "try", "typeof", "var", "void", "while", "with", "yield",
        "let", "static", "enum", "await", "implements", "interface", "package",
        "private", "protected", "public", "abstract", "boolean", "byte", "char",
        "double", "final", "float", "goto", "int", "long", "native", "short",
        "synchronized", "throws", "transient", "volatile", "null", "true", "false"
    ];

    /// <summary>
    /// Names a generated type must not take: the fixed declarations of the generated file, the
    /// global types it relies on, and the built-in type keywords.
    /// </summary>
    private static readonly HashSet<string> ReservedTypeNames =
    [
        "ProblemDetail", "ApiResult", "ClientOptions", "RequestOptions", "createClient",
        "Get", "Put", "Post", "Delete", "Options", "Head", "Patch", "Trace",
        "Record", "Promise", "Array", "Object", "String", "Number", "Boolean", "Error",
        "AbortSignal", "RequestCredentials", "BodyInit", "FormData", "Blob", "ArrayBuffer",
        "URLSearchParams", "Response",
        "string", "number", "boolean", "any", "unknown", "never", "object", "symbol",
        "bigint", "undefined",
    ];

    public static bool IsReservedTypeName(string name)
    {
        return ReservedTypeNames.Contains(name) || ReservedWords.Contains(name);
    }

    public static bool IsReservedWord(string word)
    {
        return ReservedWords.Contains(word);
    }

    public static string SanitizeIdentifier(string value)
    {
        return SanitizeIdentifier(value, asParameter: false);
    }

    public static string SanitizeIdentifier(string value, bool asParameter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "_";
        }

        string sanitized = Regex.Replace(value, "[^a-zA-Z0-9_$]", "_");

        if (char.IsDigit(sanitized[0]))
        {
            sanitized = "_" + sanitized;
        }

        // Reserved words can't be used as bare identifiers in TypeScript.
        //  - As property/method/type names they are valid when quoted ("class").
        //  - As parameter bindings they must stay valid identifiers, so prefix "_".
        if (IsReservedWord(sanitized))
        {
            return asParameter ? "_" + sanitized : "\"" + sanitized + "\"";
        }

        if (sanitized == "_")
        {
            sanitized = "param";
        }

        return sanitized;
    }

    public static string ToPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        // Insert separators at camelCase boundaries so the library's ToPascalCase
        // treats each word independently (it otherwise lowercases the trailing text).
        string separated = Regex.Replace(value, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return separated.ToPascalCase();
    }

    /// <summary>True when the text can be used unquoted as a property name or binding.</summary>
    public static bool IsValidIdentifier(string value)
    {
        return Regex.IsMatch(value, @"^[A-Za-z_$][A-Za-z0-9_$]*$");
    }

    /// <summary>Property or object key: bare when it is a valid identifier, otherwise a double-quoted literal.</summary>
    public static string QuoteKey(string key)
    {
        return IsValidIdentifier(key) ? key : StringLiteral(key);
    }

    /// <summary>Double-quoted literal with quotes, backslashes and control characters escaped.</summary>
    public static string StringLiteral(string value)
    {
        return JsonSerializer.Serialize(value, StringLiteralOptions);
    }

    /// <summary>Single-quoted literal, the style used for the URLs in the generated factory.</summary>
    public static string SingleQuotedLiteral(string value)
    {
        return "'" + EscapeLiteral(value, "'") + "'";
    }

    /// <summary>Escapes text for the literal parts of a template string.</summary>
    public static string TemplateLiteralText(string value)
    {
        return EscapeLiteral(value, "`").Replace("${", "\\${");
    }

    private static string EscapeLiteral(string value, string quote)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace(quote, "\\" + quote)
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    /// <summary>
    /// Turns a component key into a valid TypeScript type identifier. Valid identifiers are kept;
    /// otherwise runs of invalid characters become "_", a trailing "_" is trimmed and a leading
    /// digit gets a "_" prefix. Reserved words are prefixed with "_".
    /// </summary>
    public static string ToTypeIdentifier(string key)
    {
        string identifier = Regex.Replace(key, "[^A-Za-z0-9_$]+", "_");

        if (!IsValidIdentifier(key))
        {
            identifier = identifier.TrimEnd('_');
        }

        if (identifier.Length == 0)
        {
            return "Schema";
        }

        if (char.IsDigit(identifier[0]))
        {
            identifier = "_" + identifier;
        }

        if (IsReservedWord(identifier))
        {
            identifier = "_" + identifier;
        }

        return identifier;
    }

    /// <summary>
    /// Maps every component key to a unique TypeScript identifier. Keys that are already valid keep
    /// their name (unless it is reserved); the others are sanitised afterwards so they never take a
    /// valid key's name. <paramref name="reserved"/> lists names the generated file declares itself.
    /// </summary>
    public static Dictionary<string, string> BuildComponentNames(IEnumerable<string> keys, IEnumerable<string>? reserved = null)
    {
        List<string> keyList = keys.ToList();
        HashSet<string> taken = new(ReservedTypeNames);
        foreach (string name in reserved ?? [])
        {
            taken.Add(name);
        }

        Dictionary<string, string> names = [];

        foreach (string key in keyList)
        {
            if (IsValidIdentifier(key) && !IsReservedWord(key) && taken.Add(key))
            {
                names[key] = key;
            }
        }

        foreach (string key in keyList)
        {
            if (names.ContainsKey(key))
            {
                continue;
            }

            string candidate = ToTypeIdentifier(key);
            string unique = candidate;
            int suffix = 2;
            while (!taken.Add(unique))
            {
                unique = $"{candidate}_{suffix++}";
            }

            names[key] = unique;
        }

        return names;
    }

    /// <summary>Lower-cases the first character: the casing of every generated method name.</summary>
    public static string LowerFirst(string name)
    {
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    /// <summary>
    /// The method name of an operation, always camelCase. The HTTP verb is dropped ("GetUser" under get becomes
    /// "user"), but only when the next character starts a new word, so "PostcodeLookup" stays intact (as
    /// "postcodeLookup"); names derived from the path always start a new word after the verb. A digit never
    /// starts a method name.
    /// </summary>
    public static string StripHttpVerbPrefix(string name, string httpMethod, bool pathDerived = false)
    {
        if (name.Length > httpMethod.Length && name.StartsWith(httpMethod, StringComparison.OrdinalIgnoreCase))
        {
            string stripped = name.Substring(httpMethod.Length);
            if (!char.IsDigit(stripped[0]) && (pathDerived || !char.IsLower(stripped[0])))
            {
                return LowerFirst(stripped);
            }
        }

        return LowerFirst(name);
    }
}
