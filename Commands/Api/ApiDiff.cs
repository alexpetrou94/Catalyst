using System.Text.RegularExpressions;
using Catalyst.Common.Models;

namespace Catalyst.Commands.Api;

internal static class ChangeSeverity
{
    public const string Breaking = "breaking";
    public const string NonBreaking = "non-breaking";
}

/// <summary>The stable codes of <c>api diff</c>, one per kind of change.</summary>
internal static class ChangeKind
{
    public const string OperationRemoved = "OPERATION_REMOVED";
    public const string OperationAdded = "OPERATION_ADDED";
    public const string OperationMoved = "OPERATION_MOVED";
    public const string OperationCallChanged = "OPERATION_CALL_CHANGED";
    public const string ParameterAddedRequired = "PARAMETER_ADDED_REQUIRED";
    public const string ParameterAddedOptional = "PARAMETER_ADDED_OPTIONAL";
    public const string ParameterRemoved = "PARAMETER_REMOVED";
    public const string ParameterBecameRequired = "PARAMETER_BECAME_REQUIRED";
    public const string ParameterBecameOptional = "PARAMETER_BECAME_OPTIONAL";
    public const string ParameterTypeChanged = "PARAMETER_TYPE_CHANGED";
    public const string ParameterMoved = "PARAMETER_MOVED";
    public const string BodyAdded = "BODY_ADDED";
    public const string BodyRemoved = "BODY_REMOVED";
    public const string BodyTypeChanged = "BODY_TYPE_CHANGED";
    public const string BodyMediaTypeChanged = "BODY_MEDIA_TYPE_CHANGED";
    public const string ResponseTypeChanged = "RESPONSE_TYPE_CHANGED";
    public const string TypeRemoved = "TYPE_REMOVED";
    public const string TypeAdded = "TYPE_ADDED";
    public const string TypeKindChanged = "TYPE_KIND_CHANGED";
    public const string TypeAliasChanged = "TYPE_ALIAS_CHANGED";
    public const string TypePropertyRemoved = "TYPE_PROPERTY_REMOVED";
    public const string TypePropertyAddedRequired = "TYPE_PROPERTY_ADDED_REQUIRED";
    public const string TypePropertyAddedOptional = "TYPE_PROPERTY_ADDED_OPTIONAL";
    public const string TypePropertyTypeChanged = "TYPE_PROPERTY_TYPE_CHANGED";
    public const string TypePropertyBecameRequired = "TYPE_PROPERTY_BECAME_REQUIRED";
    public const string TypePropertyBecameOptional = "TYPE_PROPERTY_BECAME_OPTIONAL";
    public const string TypeMemberRemoved = "TYPE_MEMBER_REMOVED";
    public const string TypeMemberAdded = "TYPE_MEMBER_ADDED";
}

internal sealed record ApiOperationRef(string Method, string Path, string Call);

/// <summary>One difference between two versions of the generated client.</summary>
internal sealed record ApiChange(
    string Severity,
    string Kind,
    ApiOperationRef? Operation,
    string? Type,
    string? Member,
    string? Before,
    string? After,
    string Message);

/// <summary>
/// Compares two <see cref="ClientModel"/>s and reports what changes for code written against the generated client:
/// call names, parameter lists and TypeScript types, not raw spec differences. Type strings are compared as text after
/// collapsing whitespace, so a renamed type shows as a changed type plus a removed and an added type.
/// </summary>
internal static partial class ApiDiff
{
    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex PathParameter();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static List<ApiChange> Compare(ClientModel before, ClientModel after)
    {
        List<ApiChange> changes = [];
        CompareOperations(before, after, changes);
        CompareTypes(before, after, changes);
        return changes;
    }

    // ---- operations ----

    private static string Key(ApiOperation operation) => operation.Method + " " + PathParameter().Replace(operation.Path, "{}");

    private static void CompareOperations(ClientModel before, ClientModel after, List<ApiChange> changes)
    {
        Dictionary<string, ApiOperation> afterByKey = after.Operations.GroupBy(Key).ToDictionary(g => g.Key, g => g.First());
        List<ApiOperation> unmatchedAfter = after.Operations.Where(o => !before.Operations.Any(b => Key(b) == Key(o))).ToList();

        foreach (ApiOperation old in before.Operations)
        {
            ApiOperation? current = afterByKey.GetValueOrDefault(Key(old));

            // A changed path with the same operationId is the same operation that moved.
            if (current == null && old.OperationId != null)
            {
                current = unmatchedAfter.FirstOrDefault(o => o.OperationId == old.OperationId);
                if (current != null)
                {
                    unmatchedAfter.Remove(current);
                    string severity = old.Call == current.Call && old.Signature == current.Signature ? ChangeSeverity.NonBreaking : ChangeSeverity.Breaking;
                    changes.Add(OperationChange(severity, ChangeKind.OperationMoved, current, old.Method + " " + old.Path, current.Method + " " + current.Path, $"moved from {old.Method} {old.Path}"));
                }
            }

            if (current == null)
            {
                changes.Add(OperationChange(ChangeSeverity.Breaking, ChangeKind.OperationRemoved, old, old.Call, null, "removed"));
                continue;
            }

            CompareOperation(old, current, changes);
        }

        foreach (ApiOperation added in unmatchedAfter)
        {
            changes.Add(OperationChange(ChangeSeverity.NonBreaking, ChangeKind.OperationAdded, added, null, added.Call, "added"));
        }
    }

    private static ApiChange OperationChange(string severity, string kind, ApiOperation operation, string? beforeValue, string? afterValue, string message, string? member = null)
    {
        return new ApiChange(severity, kind, new ApiOperationRef(operation.Method, operation.Path, operation.Call), null, member, beforeValue, afterValue, message);
    }

    private static void CompareOperation(ApiOperation old, ApiOperation current, List<ApiChange> changes)
    {
        if (old.Call != current.Call)
        {
            changes.Add(OperationChange(ChangeSeverity.Breaking, ChangeKind.OperationCallChanged, current, old.Call, current.Call, $"generated call changed from {old.Call} to {current.Call}"));
        }

        CompareParameters(old, current, changes);
        CompareBody(old, current, changes);

        if (Normalize(old.Response.Type) != Normalize(current.Response.Type))
        {
            changes.Add(OperationChange(ChangeSeverity.Breaking, ChangeKind.ResponseTypeChanged, current, old.Response.Type, current.Response.Type, $"response type changed from {old.Response.Type} to {current.Response.Type}"));
        }
    }

    private static string ParameterKey(ApiParameter parameter) => parameter.In + ":" + parameter.WireName;

    private static void CompareParameters(ApiOperation old, ApiOperation current, List<ApiChange> changes)
    {
        Dictionary<string, ApiParameter> oldByKey = old.Parameters.ToDictionary(ParameterKey);
        Dictionary<string, ApiParameter> currentByKey = current.Parameters.ToDictionary(ParameterKey);

        foreach (ApiParameter removed in old.Parameters.Where(p => !currentByKey.ContainsKey(ParameterKey(p))))
        {
            changes.Add(OperationChange(ChangeSeverity.Breaking, ChangeKind.ParameterRemoved, current, removed.Name, null, $"parameter \"{removed.WireName}\" removed", removed.WireName));
        }

        foreach (ApiParameter added in current.Parameters.Where(p => !oldByKey.ContainsKey(ParameterKey(p))))
        {
            changes.Add(added.Required
                ? OperationChange(ChangeSeverity.Breaking, ChangeKind.ParameterAddedRequired, current, null, added.Name, $"parameter \"{added.WireName}\" added as required", added.WireName)
                : OperationChange(ChangeSeverity.NonBreaking, ChangeKind.ParameterAddedOptional, current, null, added.Name, $"parameter \"{added.WireName}\" added as optional", added.WireName));
        }

        List<string> commonOld = SignatureOrder(old).Where(currentByKey.ContainsKey).ToList();
        List<string> commonCurrent = SignatureOrder(current).Where(oldByKey.ContainsKey).ToList();

        foreach (string key in commonOld)
        {
            ApiParameter before = oldByKey[key];
            ApiParameter after = currentByKey[key];
            bool requirednessChanged = before.Required != after.Required;

            if (requirednessChanged)
            {
                changes.Add(after.Required
                    ? OperationChange(ChangeSeverity.Breaking, ChangeKind.ParameterBecameRequired, current, "optional", "required", $"parameter \"{after.WireName}\" became required", after.WireName)
                    : OperationChange(ChangeSeverity.NonBreaking, ChangeKind.ParameterBecameOptional, current, "required", "optional", $"parameter \"{after.WireName}\" became optional", after.WireName));
            }

            if (Normalize(before.Type) != Normalize(after.Type))
            {
                changes.Add(OperationChange(ChangeSeverity.Breaking, ChangeKind.ParameterTypeChanged, current, before.Type, after.Type, $"parameter \"{after.WireName}\" type changed from {before.Type} to {after.Type}", after.WireName));
            }

            // Arguments are positional: a parameter that is no longer in the same place among the others breaks callers.
            // A parameter whose requiredness changed moves by design and is already reported above.
            if (!requirednessChanged && commonOld.IndexOf(key) != commonCurrent.IndexOf(key))
            {
                changes.Add(OperationChange(ChangeSeverity.Breaking, ChangeKind.ParameterMoved, current, (commonOld.IndexOf(key) + 1).ToString(), (commonCurrent.IndexOf(key) + 1).ToString(), $"parameter \"{after.WireName}\" moved from position {commonOld.IndexOf(key) + 1} to {commonCurrent.IndexOf(key) + 1}", after.WireName));
            }
        }
    }

    /// <summary>Parameter keys in call order: path and required parameters first, then the optional ones, as in the generated signature.</summary>
    private static List<string> SignatureOrder(ApiOperation operation)
    {
        List<string> order = operation.Parameters.Where(p => p.Required || p.In == "path").Select(ParameterKey).ToList();
        order.AddRange(operation.Parameters.Where(p => !p.Required && p.In != "path").Select(ParameterKey));
        return order;
    }

    private static void CompareBody(ApiOperation old, ApiOperation current, List<ApiChange> changes)
    {
        if (old.Body == null && current.Body != null)
        {
            // The generated body parameter is always mandatory, so any new body breaks existing calls.
            changes.Add(OperationChange(ChangeSeverity.Breaking, ChangeKind.BodyAdded, current, null, current.Body.Type, $"body {current.Body.Type} added", "body"));
        }
        else if (old.Body != null && current.Body == null)
        {
            changes.Add(OperationChange(ChangeSeverity.Breaking, ChangeKind.BodyRemoved, current, old.Body.Type, null, "body removed", "body"));
        }
        else if (old.Body != null && current.Body != null)
        {
            if (Normalize(old.Body.Type) != Normalize(current.Body.Type))
            {
                changes.Add(OperationChange(ChangeSeverity.Breaking, ChangeKind.BodyTypeChanged, current, old.Body.Type, current.Body.Type, $"body type changed from {old.Body.Type} to {current.Body.Type}", "body"));
            }
            else if (old.Body.MediaType != current.Body.MediaType)
            {
                changes.Add(OperationChange(ChangeSeverity.NonBreaking, ChangeKind.BodyMediaTypeChanged, current, old.Body.MediaType, current.Body.MediaType, $"body media type changed from {old.Body.MediaType} to {current.Body.MediaType}", "body"));
            }
        }
    }

    // ---- types ----

    private static void CompareTypes(ClientModel before, ClientModel after, List<ApiChange> changes)
    {
        Dictionary<string, TypeDeclaration> afterByName = after.Types.ToDictionary(t => t.Name);
        Dictionary<string, TypeDeclaration> beforeByName = before.Types.ToDictionary(t => t.Name);

        foreach (TypeDeclaration old in before.Types)
        {
            if (!afterByName.TryGetValue(old.Name, out TypeDeclaration? current))
            {
                changes.Add(TypeChange(ChangeSeverity.Breaking, ChangeKind.TypeRemoved, old.Name, null, null, null, "removed"));
                continue;
            }

            CompareType(old, current, changes);
        }

        foreach (TypeDeclaration added in after.Types.Where(t => !beforeByName.ContainsKey(t.Name)))
        {
            changes.Add(TypeChange(ChangeSeverity.NonBreaking, ChangeKind.TypeAdded, added.Name, null, null, null, "added"));
        }
    }

    private static ApiChange TypeChange(string severity, string kind, string type, string? member, string? beforeValue, string? afterValue, string message)
    {
        return new ApiChange(severity, kind, null, type, member, beforeValue, afterValue, message);
    }

    private static void CompareType(TypeDeclaration old, TypeDeclaration current, List<ApiChange> changes)
    {
        // An enum and a literal union are different kinds, as are an interface and an alias.
        string oldKind = old.Kind;
        string currentKind = current.Kind;
        if (oldKind != currentKind)
        {
            changes.Add(TypeChange(ChangeSeverity.Breaking, ChangeKind.TypeKindChanged, current.Name, null, oldKind, currentKind, $"changed from {oldKind} to {currentKind}"));
            return;
        }

        switch (current.Kind)
        {
            case "interface":
                CompareInterface(old, current, changes);
                break;
            case "enum":
                CompareMembers(current.Name, old.Values, current.Values, changes);
                break;
            default:
                CompareAlias(old, current, changes);
                break;
        }
    }

    private static void CompareInterface(TypeDeclaration old, TypeDeclaration current, List<ApiChange> changes)
    {
        Dictionary<string, TypeMember> oldByName = old.Members.ToDictionary(m => m.Name);
        Dictionary<string, TypeMember> currentByName = current.Members.ToDictionary(m => m.Name);

        foreach (TypeMember removed in old.Members.Where(m => !currentByName.ContainsKey(m.Name)))
        {
            changes.Add(TypeChange(ChangeSeverity.Breaking, ChangeKind.TypePropertyRemoved, current.Name, removed.Name, removed.Type, null, $"property \"{removed.Name}\" removed"));
        }

        foreach (TypeMember added in current.Members.Where(m => !oldByName.ContainsKey(m.Name)))
        {
            changes.Add(added.Required
                ? TypeChange(ChangeSeverity.Breaking, ChangeKind.TypePropertyAddedRequired, current.Name, added.Name, null, added.Type, $"property \"{added.Name}\" added as required")
                : TypeChange(ChangeSeverity.NonBreaking, ChangeKind.TypePropertyAddedOptional, current.Name, added.Name, null, added.Type, $"property \"{added.Name}\" added as optional"));
        }

        foreach (TypeMember before in old.Members.Where(m => currentByName.ContainsKey(m.Name)))
        {
            TypeMember after = currentByName[before.Name];

            if (Normalize(before.Type) != Normalize(after.Type) && !CompareLiteralUnion(current.Name, before, after, changes))
            {
                changes.Add(TypeChange(ChangeSeverity.Breaking, ChangeKind.TypePropertyTypeChanged, current.Name, before.Name, before.Type, after.Type, $"property \"{before.Name}\" type changed from {before.Type} to {after.Type}"));
            }

            // Either direction can break strict TypeScript: reading an optional value, or constructing without a required one.
            if (before.Required != after.Required)
            {
                changes.Add(after.Required
                    ? TypeChange(ChangeSeverity.Breaking, ChangeKind.TypePropertyBecameRequired, current.Name, before.Name, "optional", "required", $"property \"{before.Name}\" became required")
                    : TypeChange(ChangeSeverity.Breaking, ChangeKind.TypePropertyBecameOptional, current.Name, before.Name, "required", "optional", $"property \"{before.Name}\" became optional"));
            }
        }
    }

    /// <summary>
    /// A property whose type only gained or lost literal members of a union ("a" | "b") is a member change, not a
    /// type change. Returns false when the difference is anything else.
    /// </summary>
    private static bool CompareLiteralUnion(string type, TypeMember before, TypeMember after, List<ApiChange> changes)
    {
        List<string> oldMembers = SplitUnion(Normalize(before.Type));
        List<string> currentMembers = SplitUnion(Normalize(after.Type));
        List<string> removed = oldMembers.Except(currentMembers).ToList();
        List<string> added = currentMembers.Except(oldMembers).ToList();

        bool onlyLiterals = removed.Concat(added).All(IsLiteral);
        bool oneDirection = (removed.Count == 0) != (added.Count == 0);
        if (!onlyLiterals || !oneDirection)
        {
            return false;
        }

        foreach (string member in removed)
        {
            changes.Add(TypeChange(ChangeSeverity.Breaking, ChangeKind.TypeMemberRemoved, type, before.Name, member, null, $"property \"{before.Name}\": member {member} removed"));
        }

        foreach (string member in added)
        {
            changes.Add(TypeChange(ChangeSeverity.NonBreaking, ChangeKind.TypeMemberAdded, type, before.Name, null, member, $"property \"{before.Name}\": member {member} added"));
        }

        return true;
    }

    private static bool IsLiteral(string member) => member.StartsWith('"') || member is "true" or "false" || (member.Length > 0 && (char.IsDigit(member[0]) || member[0] == '-'));

    private static void CompareAlias(TypeDeclaration old, TypeDeclaration current, List<ApiChange> changes)
    {
        string oldType = Normalize(old.AliasType ?? string.Empty);
        string currentType = Normalize(current.AliasType ?? string.Empty);
        if (oldType == currentType)
        {
            return;
        }

        List<string> oldMembers = SplitUnion(oldType);
        List<string> currentMembers = SplitUnion(currentType);

        // Unions (including literal unions) compare member by member; anything else is one changed type.
        if (oldMembers.Count > 1 || currentMembers.Count > 1)
        {
            CompareMembers(current.Name, oldMembers, currentMembers, changes);
            return;
        }

        changes.Add(TypeChange(ChangeSeverity.Breaking, ChangeKind.TypeAliasChanged, current.Name, null, old.AliasType, current.AliasType, $"changed from {old.AliasType} to {current.AliasType}"));
    }

    private static void CompareMembers(string type, List<string> oldMembers, List<string> currentMembers, List<ApiChange> changes)
    {
        foreach (string removed in oldMembers.Except(currentMembers))
        {
            changes.Add(TypeChange(ChangeSeverity.Breaking, ChangeKind.TypeMemberRemoved, type, removed, removed, null, $"member {removed} removed"));
        }

        foreach (string added in currentMembers.Except(oldMembers))
        {
            changes.Add(TypeChange(ChangeSeverity.NonBreaking, ChangeKind.TypeMemberAdded, type, added, null, added, $"member {added} added"));
        }
    }

    // ---- helpers ----

    private static string Normalize(string type) => Whitespace().Replace(type.Trim(), " ");

    /// <summary>Splits a type at its top-level "|" (not inside brackets, generics or string literals).</summary>
    internal static List<string> SplitUnion(string type)
    {
        List<string> parts = [];
        int depth = 0;
        char quote = '\0';
        int start = 0;

        for (int i = 0; i < type.Length; i++)
        {
            char c = type[i];

            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (c)
            {
                case '"':
                case '\'':
                    quote = c;
                    break;
                case '{' or '(' or '<' or '[':
                    depth++;
                    break;
                case '}' or ')' or '>' or ']':
                    depth--;
                    break;
                case '|' when depth == 0:
                    parts.Add(type[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        parts.Add(type[start..].Trim());
        return parts.Where(p => p.Length > 0).ToList();
    }
}
