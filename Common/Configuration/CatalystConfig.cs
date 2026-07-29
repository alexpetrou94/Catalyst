namespace Catalyst.Common.Configuration;

internal sealed class CatalystConfig
{
    public GenConfig Gen { get; init; } = new();
}

internal sealed class GenConfig
{
    public OpenApiGenConfig OpenApi { get; init; } = new();
}

internal sealed class OpenApiGenConfig
{
    public List<string> SkipPaths { get; init; } = [];
}
