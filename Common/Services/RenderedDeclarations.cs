using System.Text.RegularExpressions;

namespace Catalyst.Common.Services;

/// <summary>
/// Cuts the top-level declarations (doc comment included) out of a generated file, keyed by type name. The
/// generator's own output has a fixed layout: every declaration starts at column 0 with "export" or a doc comment,
/// and everything indented belongs to the declaration above it.
/// </summary>
internal static partial class RenderedDeclarations
{
    [GeneratedRegex(@"^export (?:interface|type|enum) ([A-Za-z0-9_$]+)")]
    private static partial Regex DeclarationName();

    public static Dictionary<string, string> Split(string code)
    {
        string[] lines = code.Replace("\r\n", "\n").Split('\n');
        List<(int Start, string? Name)> blocks = [];
        bool inDocComment = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (line.StartsWith("/**", StringComparison.Ordinal))
            {
                blocks.Add((i, null));
                inDocComment = true;
            }
            else if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                Match match = DeclarationName().Match(line);
                string? name = match.Success ? match.Groups[1].Value : null;

                if (inDocComment)
                {
                    blocks[^1] = (blocks[^1].Start, name);
                }
                else
                {
                    blocks.Add((i, name));
                }

                inDocComment = false;
            }
        }

        Dictionary<string, string> result = [];

        for (int b = 0; b < blocks.Count; b++)
        {
            if (blocks[b].Name == null)
            {
                continue;
            }

            int end = b + 1 < blocks.Count ? blocks[b + 1].Start : lines.Length;
            result[blocks[b].Name!] = string.Join('\n', lines[blocks[b].Start..end]).TrimEnd();
        }

        return result;
    }
}
