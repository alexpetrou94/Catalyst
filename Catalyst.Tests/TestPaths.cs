namespace Catalyst.Tests;

/// <summary>Locations of the test fixtures, snapshots and the TypeScript project.</summary>
internal static class TestPaths
{
    /// <summary>Names of the specs under Fixtures/Specs that have a snapshot.</summary>
    public static readonly string[] SnapshotSpecs =
    [
        "enterprise", "minimal", "edge-cases", "petstore", "eshop-catalog", "spotify", "museum",
    ];

    /// <summary>The Catalyst.Tests source folder. Snapshots are updated here, not in the build output.</summary>
    public static string ProjectDirectory { get; } = FindProjectDirectory();

    public static string TypeScriptDirectory => Path.Combine(ProjectDirectory, "TypeScript");

    public static string SpecPath(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "Specs", name + ".json");
    }

    public static string FixturePath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
    }

    public static string SnapshotPath(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Snapshots", name + ".ts");
    }

    public static string SourceSnapshotPath(string name)
    {
        return Path.Combine(ProjectDirectory, "Snapshots", name + ".ts");
    }

    private static string FindProjectDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Catalyst.Tests.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate Catalyst.Tests.csproj above " + AppContext.BaseDirectory);
    }
}
