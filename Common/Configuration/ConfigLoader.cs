using System.Text.Json;

namespace Catalyst.Common.Configuration;

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

                    return config;
                }
                
                catch
                {
                    return null;
                }
            }

            dir = dir.Parent;
        }

        return null;
    }
}
