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

    /// <summary>
    /// Named clients this repository consumes. The Syncfully Workbench lists these projects and
    /// prefills its generator form from them; `catalyst gen openapi` without --source generates
    /// every configured client in one run.
    /// </summary>
    public List<OpenApiClientConfig> Clients { get; init; } = [];
}

internal sealed class OpenApiClientConfig
{
    /// <summary>Generated client name; becomes the --class-name and the default file name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>OpenAPI source: a local JSON file path or an http(s) URL.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Output folder or file, relative to the config file's folder.</summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>Skip TLS certificate validation for a local HTTPS server with a self-signed certificate.</summary>
    public bool Insecure { get; init; }

    /// <summary>Per-client skip paths; falls back to the project wide gen.openapi.skipPaths.</summary>
    public List<string> SkipPaths { get; init; } = [];
}
