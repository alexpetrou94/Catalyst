namespace Catalyst.Common.Services;

/// <summary>Writes generated code without touching a file that already has exactly that content.</summary>
internal static class GeneratedFileWriter
{
    /// <summary>Returns true when the file was written, false when it was already up to date.</summary>
    public static bool WriteIfChanged(string path, string content)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(path) && File.ReadAllText(path) == content)
        {
            return false;
        }

        File.WriteAllText(path, content);
        return true;
    }
}
