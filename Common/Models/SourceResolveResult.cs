using Catalyst.Common.Enums;

namespace Catalyst.Common.Models;

internal sealed class SourceResolveResult
{
    public required string Content { get; init; }
    public required string DisplayPath { get; init; }
    public required SourceType Type { get; init; }
}
