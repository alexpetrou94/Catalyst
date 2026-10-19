namespace Catalyst.Tests.Generation;

/// <summary>
/// Generates a client for every spec fixture and compares it with the checked-in snapshot. This is the
/// one place that asserts the shape of generated code; the snapshots are also compiled with tsc and
/// exercised at runtime (see GeneratedTypeScriptTests).
/// Set CATALYST_UPDATE_SNAPSHOTS=1 to rewrite the snapshots after an intended change.
/// </summary>
public class SnapshotTests
{
    public static IEnumerable<object[]> Specs() => TestPaths.SnapshotSpecs.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Specs))]
    public void Generate_MatchesSnapshot(string name)
    {
        string code = SpecLoader.CreateGenerator(SpecLoader.Load(name)).Generate();

        if (Environment.GetEnvironmentVariable("CATALYST_UPDATE_SNAPSHOTS") == "1")
        {
            // Write to the source tree: the copy in the build output is overwritten on the next build.
            string target = TestPaths.SourceSnapshotPath(name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, Normalize(code) + "\n");
            return;
        }

        string snapshotPath = TestPaths.SnapshotPath(name);
        Assert.True(
            File.Exists(snapshotPath),
            $"No snapshot for '{name}'. Create it with: set CATALYST_UPDATE_SNAPSHOTS=1 and run dotnet test (it writes Snapshots/{name}.ts).");

        Assert.Equal(Normalize(File.ReadAllText(snapshotPath)), Normalize(code));
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n").Trim();
}
