using System.Text.Json;

namespace Catalyst.Common.Configuration;

/// <summary>Outcome of locating and reading a catalyst config file.</summary>
/// <param name="Config">The parsed config, or null when none was found or the file is unusable.</param>
/// <param name="Path">Absolute path of the config file, when one was found.</param>
/// <param name="Error">A human readable reason when a file was found but could not be used.</param>
internal sealed record ConfigLoadResult(CatalystConfig? Config, string? Path, string? Error);

internal static class ConfigLoader
{
    private static readonly string[] ConfigFileNames = ["catalyst.config.json", ".catalyst.config.json"];
    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public static CatalystConfig? Load()
    {
        return Load(Directory.GetCurrentDirectory());
    }

    public static CatalystConfig? Load(string startDirectory)
    {
        return LoadDetailed(startDirectory).Config;
    }

    /// <summary>
    /// Like <see cref="Load(string)"/> but reports where the file was found and why an existing
    /// file could not be used, so callers can fail with a useful message.
    /// </summary>
    public static ConfigLoadResult LoadDetailed(string startDirectory)
    {
        DirectoryInfo? dir = new(startDirectory);

        while (dir != null)
        {
            foreach (string name in ConfigFileNames)
            {
                string path = Path.Combine(dir.FullName, name);

                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    string content = File.ReadAllText(path);
                    CatalystConfig? config = JsonSerializer.Deserialize<CatalystConfig>(content, JsonSerializerOptions);

                    return config is null
                        ? new ConfigLoadResult(null, path, $"The config is empty or not a JSON object: {path}")
                        : new ConfigLoadResult(config, path, null);
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    return new ConfigLoadResult(null, path, $"Failed to read {path}: {ex.Message}");
                }
            }

            dir = dir.Parent;
        }

        return new ConfigLoadResult(null, null, null);
    }
}
