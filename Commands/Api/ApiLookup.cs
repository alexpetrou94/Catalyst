using System.Text.RegularExpressions;
using Catalyst.Common.Models;

namespace Catalyst.Commands.Api;

/// <summary>The outcome of resolving an operation selector: one operation, or an error code with suggestions.</summary>
internal sealed record OperationMatch(ApiOperation? Operation, string? ErrorCode, string? Message, List<string> Suggestions);

/// <summary>Finding operations, types and close matches in a <see cref="ClientModel"/>.</summary>
internal static partial class ApiLookup
{
    private const int MaxSuggestions = 5;

    [GeneratedRegex(@"^(?<method>[A-Za-z]+)\s+(?<path>/\S*)$")]
    private static partial Regex MethodAndPath();

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex PathParameter();

    /// <summary>
    /// Resolves "GET /items/{id}" (the path may use other parameter names), an operationId, or a generated call
    /// such as "get.itemsId" or "client.get.itemsId".
    /// </summary>
    public static OperationMatch FindOperation(ClientModel model, string selector)
    {
        string text = selector.Trim();
        List<ApiOperation> matches = [];

        Match methodAndPath = MethodAndPath().Match(text);
        if (methodAndPath.Success)
        {
            string method = methodAndPath.Groups["method"].Value;
            string path = methodAndPath.Groups["path"].Value;

            matches = model.Operations.Where(o => o.Method.Equals(method, StringComparison.OrdinalIgnoreCase) && o.Path == path).ToList();
            if (matches.Count == 0)
            {
                string shape = PathParameter().Replace(path, "{}");
                matches = model.Operations
                    .Where(o => o.Method.Equals(method, StringComparison.OrdinalIgnoreCase) && PathParameter().Replace(o.Path, "{}") == shape)
                    .ToList();
            }
        }
        else
        {
            string call = text.StartsWith("client.", StringComparison.OrdinalIgnoreCase) ? text["client.".Length..] : text;
            string[] parts = call.Split('.');

            if (parts.Length == 2)
            {
                matches = model.Operations.Where(o => o.Verb.Equals(parts[0], StringComparison.OrdinalIgnoreCase) && o.MethodName == parts[1]).ToList();
                if (matches.Count == 0)
                {
                    matches = model.Operations.Where(o => o.Verb.Equals(parts[0], StringComparison.OrdinalIgnoreCase) && o.MethodName.Equals(parts[1], StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }

            if (matches.Count == 0)
            {
                matches = model.Operations.Where(o => o.OperationId == text).ToList();
            }

            if (matches.Count == 0)
            {
                matches = model.Operations.Where(o => string.Equals(o.OperationId, text, StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }

        if (matches.Count == 1)
        {
            return new OperationMatch(matches[0], null, null, []);
        }

        if (matches.Count > 1)
        {
            return new OperationMatch(null, IssueCodes.OperationAmbiguous, $"\"{selector}\" matches {matches.Count} operations. Use \"METHOD /path\" or the generated call.", matches.Take(MaxSuggestions).Select(Describe).ToList());
        }

        return new OperationMatch(null, IssueCodes.OperationNotFound, $"No operation matches \"{selector}\".", SuggestOperations(model, text));
    }

    /// <summary>The operations whose method, path, operationId or generated call is closest to the text.</summary>
    public static List<string> SuggestOperations(ClientModel model, string text)
    {
        string query = text.ToLowerInvariant();

        return model.Operations
            .Select(o => (Operation: o, Score: Distance(query, [$"{o.Method} {o.Path}", o.OperationId ?? string.Empty, o.Call, $"{o.Verb}.{o.MethodName}"])))
            .OrderBy(pair => pair.Score)
            .ThenBy(pair => pair.Operation.Path, StringComparer.Ordinal)
            .Take(MaxSuggestions)
            .Select(pair => Describe(pair.Operation))
            .ToList();
    }

    public static List<string> SuggestTypes(ClientModel model, string text)
    {
        string query = text.ToLowerInvariant();

        return model.Types
            .Select(t => (Type: t, Score: Distance(query, [t.Name, t.ComponentName ?? string.Empty])))
            .OrderBy(pair => pair.Score)
            .ThenBy(pair => pair.Type.Name, StringComparer.Ordinal)
            .Take(MaxSuggestions)
            .Select(pair => pair.Type.Name)
            .ToList();
    }

    public static string Describe(ApiOperation operation)
    {
        return $"{operation.Method} {operation.Path}  ({operation.Call})";
    }

    /// <summary>Finds a type by its TypeScript name or its original component name, exact first, then ignoring case.</summary>
    public static TypeDeclaration? FindType(ClientModel model, string name)
    {
        return model.Types.FirstOrDefault(t => t.Name == name)
            ?? model.Types.FirstOrDefault(t => t.ComponentName == name)
            ?? model.Types.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? model.Types.FirstOrDefault(t => string.Equals(t.ComponentName, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The named types reachable from the roots, dependencies first, each once. A depth limits how far to follow
    /// dependencies (0 is the roots only); null follows all of them.
    /// </summary>
    public static List<TypeDeclaration> TypeClosure(ClientModel model, IEnumerable<string> roots, int? depth = null)
    {
        Dictionary<string, TypeDeclaration> byName = model.Types.ToDictionary(t => t.Name);
        List<string> rootList = roots.Where(byName.ContainsKey).Distinct().ToList();

        Dictionary<string, int> level = [];
        Queue<string> queue = new();
        foreach (string root in rootList)
        {
            level[root] = 0;
            queue.Enqueue(root);
        }

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            if (depth.HasValue && level[current] >= depth.Value)
            {
                continue;
            }

            foreach (string dependency in byName[current].DependsOn.Where(byName.ContainsKey))
            {
                if (level.TryAdd(dependency, level[current] + 1))
                {
                    queue.Enqueue(dependency);
                }
            }
        }

        List<TypeDeclaration> ordered = [];
        HashSet<string> visited = [];

        void Visit(string name)
        {
            if (!level.ContainsKey(name) || !visited.Add(name))
            {
                return;
            }

            foreach (string dependency in byName[name].DependsOn)
            {
                Visit(dependency);
            }

            ordered.Add(byName[name]);
        }

        foreach (string root in rootList)
        {
            Visit(root);
        }

        return ordered;
    }

    /// <summary>0 when the query is inside a candidate, otherwise the smallest edit distance to a candidate.</summary>
    private static int Distance(string query, IEnumerable<string> candidates)
    {
        int best = int.MaxValue;

        foreach (string candidate in candidates.Where(c => c.Length > 0).Select(c => c.ToLowerInvariant()))
        {
            best = Math.Min(best, candidate.Contains(query, StringComparison.Ordinal) ? 0 : EditDistance(query, candidate));
        }

        return best;
    }

    private static int EditDistance(string a, string b)
    {
        int[] previous = Enumerable.Range(0, b.Length + 1).ToArray();

        for (int i = 1; i <= a.Length; i++)
        {
            int[] current = new int[b.Length + 1];
            current[0] = i;

            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            previous = current;
        }

        return previous[b.Length];
    }

    /// <summary>The operations matching the list filters: every filter term, the tag, and the HTTP method.</summary>
    public static List<ApiOperation> FilterOperations(ClientModel model, string? filter, string? tag, string? method)
    {
        string[] terms = (filter ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return model.Operations
            .Where(o => string.IsNullOrWhiteSpace(method) || o.Method.Equals(method.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(o => string.IsNullOrWhiteSpace(tag) || o.Tags.Any(t => t.Equals(tag.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Where(o =>
            {
                string haystack = string.Join(' ', [o.Method, o.Path, o.OperationId, o.MethodName, o.Call, o.Summary, .. o.Tags]);
                return terms.All(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(o => o.Path, StringComparer.Ordinal)
            .ThenBy(o => o.Method, StringComparer.Ordinal)
            .ToList();
    }
}
