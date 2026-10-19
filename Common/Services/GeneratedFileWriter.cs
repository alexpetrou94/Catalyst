namespace Catalyst.Common.Services;

/// <summary>Writes generated code without touching a file that already has exactly that content.</summary>
internal static class GeneratedFileWriter
{
    /// <summary>How a file on disk relates to freshly generated content.</summary>
    public enum FileState
    {
        Missing,
        Different,
        Same,
    }

    /// <summary>The comparison both writing and --check use, so "unchanged" and "up to date" always agree.</summary>
    public static FileState Compare(string path, string content)
    {
        if (!File.Exists(path))
        {
            return FileState.Missing;
        }

        return File.ReadAllText(path) == content ? FileState.Same : FileState.Different;
    }

    /// <summary>Returns true when the file was written, false when it was already up to date.</summary>
    public static bool WriteIfChanged(string path, string content)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (Compare(path, content) == FileState.Same)
        {
            return false;
        }

        File.WriteAllText(path, content);
        return true;
    }
}
