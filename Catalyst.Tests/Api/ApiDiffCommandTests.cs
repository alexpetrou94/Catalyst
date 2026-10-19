using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Catalyst.Tests.Api;

/// <summary>A fact that is skipped, with a reason, only when git is not installed (CI has it).</summary>
public sealed class GitFactAttribute : FactAttribute
{
    public GitFactAttribute()
    {
        try
        {
            using Process? git = Process.Start(new ProcessStartInfo("git", "--version") { RedirectStandardOutput = true, UseShellExecute = false });
            git?.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Skip = "git is not on PATH, so the git: base cannot be tested here.";
        }
    }
}

// Replaces the static consoles and changes the working directory, so these run one at a time.
[Collection("AnsiConsole")]
public class ApiDiffCommandTests
{
    private static CommandRun Api(params string[] arguments) => CommandHarness.Run(["api", .. arguments]);

    private static string CreateTempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "catalyst-diff-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Writes the edge-cases spec twice: as is, and with a parameter removed and an operation added.</summary>
    private static (string Old, string New) WriteVersions(string dir)
    {
        string oldPath = Path.Combine(dir, "old.json");
        string newPath = Path.Combine(dir, "new.json");
        File.Copy(TestPaths.SpecPath("edge-cases"), oldPath);

        JsonObject spec = (JsonObject)JsonNode.Parse(File.ReadAllText(oldPath))!;
        ((JsonObject)spec["paths"]!).Remove("/ping");
        ((JsonObject)spec["paths"]!)["/extra"] = JsonNode.Parse("""{ "get": { "operationId": "getExtra", "responses": { "200": { "description": "ok" } } } }""");
        File.WriteAllText(newPath, spec.ToJsonString());

        return (oldPath, newPath);
    }

    [Fact]
    public void Diff_PrintsBreakingChangesFirst_AndASummary()
    {
        string dir = CreateTempDirectory();
        try
        {
            (string oldPath, string newPath) = WriteVersions(dir);

            CommandRun run = Api("diff", "--base", oldPath, "--source", newPath);

            Assert.Equal(0, run.ExitCode);
            Assert.Equal(string.Empty, run.Error);
            string[] lines = run.Output.Split('\n', StringSplitOptions.TrimEntries);
            Assert.Equal("Breaking changes (1)", lines[0]);
            Assert.Equal("BREAKING  GET /ping  client.get.ping: removed", lines[1]);
            Assert.Equal("Other changes (1)", lines[2]);
            Assert.Equal("change    GET /extra  client.get.extra: added", lines[3]);
            Assert.Contains("1 breaking, 1 non-breaking change(s).", run.Output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Diff_Says_NoChanges_ForIdenticalSpecs()
    {
        CommandRun run = Api("diff", "--base", TestPaths.SpecPath("petstore"), "--source", TestPaths.SpecPath("petstore"));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("No changes.", run.Output.Trim());
    }

    [Fact]
    public void Diff_Json_HasTheEnvelopeChangesAndSummary()
    {
        string dir = CreateTempDirectory();
        try
        {
            (string oldPath, string newPath) = WriteVersions(dir);

            CommandRun run = Api("diff", "--base", oldPath, "--source", newPath, "--json");
            JsonElement root = JsonDocument.Parse(run.Output).RootElement;

            Assert.Equal(string.Empty, run.Error);
            Assert.Equal("api diff", root.GetProperty("command").GetString());
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("breaking").GetInt32());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("nonBreaking").GetInt32());

            JsonElement removed = root.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("kind").GetString() == "OPERATION_REMOVED");
            Assert.Equal("breaking", removed.GetProperty("severity").GetString());
            Assert.Equal("GET", removed.GetProperty("operation").GetProperty("method").GetString());
            Assert.Equal("/ping", removed.GetProperty("operation").GetProperty("path").GetString());
            Assert.Equal("client.get.ping", removed.GetProperty("operation").GetProperty("call").GetString());
            Assert.Equal(JsonValueKind.Null, removed.GetProperty("type").ValueKind);
            Assert.Equal("removed", removed.GetProperty("message").GetString());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Diff_FailOnBreaking_ChangesTheExitCodeOnly_WhenSomethingBreaks()
    {
        string dir = CreateTempDirectory();
        try
        {
            (string oldPath, string newPath) = WriteVersions(dir);

            CommandRun breaking = Api("diff", "--base", oldPath, "--source", newPath, "--fail-on-breaking");
            CommandRun clean = Api("diff", "--base", oldPath, "--source", oldPath, "--fail-on-breaking");
            CommandRun additive = Api("diff", "--base", newPath, "--source", oldPath);
            JsonElement json = JsonDocument.Parse(Api("diff", "--base", oldPath, "--source", newPath, "--fail-on-breaking", "--json").Output).RootElement;

            Assert.Equal(1, breaking.ExitCode);
            Assert.Equal(0, clean.ExitCode);
            Assert.Equal(0, additive.ExitCode);
            Assert.Equal(1, json.GetProperty("exitCode").GetInt32());
            Assert.False(json.GetProperty("success").GetBoolean());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Diff_RequiresABase_AsAUsageError()
    {
        CommandRun run = Api("diff", "--source", TestPaths.SpecPath("minimal"));

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("--base", run.Error);
    }

    [Fact]
    public void Diff_ReportsAMissingBase_WithTheBaseNotFoundCode()
    {
        string dir = CreateTempDirectory();
        try
        {
            CommandRun missingFile = Api("diff", "--base", Path.Combine(dir, "nope.json"), "--source", TestPaths.SpecPath("minimal"), "--json");
            // dir is not a git repository, so git cannot read anything there.
            string previous = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(dir);
            CommandRun notARepo;
            try
            {
                notARepo = Api("diff", "--base", "git:HEAD:spec.json", "--source", TestPaths.SpecPath("minimal"), "--json");
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }

            foreach (CommandRun run in new[] { missingFile, notARepo })
            {
                JsonElement root = JsonDocument.Parse(run.Output).RootElement;
                Assert.Equal(1, run.ExitCode);
                Assert.Equal("BASE_NOT_FOUND", root.GetProperty("error").GetProperty("code").GetString());
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [GitFact]
    public void Diff_ReadsTheBaseFromGit_WithAPathOrFromTheSourceFile()
    {
        string repo = CreateTempDirectory();
        string previous = Directory.GetCurrentDirectory();
        try
        {
            string spec = Path.Combine(repo, "spec.json");
            File.Copy(TestPaths.SpecPath("edge-cases"), spec);
            Git(repo, "init", "-q");
            Git(repo, "add", "spec.json");
            Git(repo, "-c", "user.email=test@example.com", "-c", "user.name=Test", "commit", "-q", "-m", "spec");

            // The working copy now differs from HEAD: one operation is gone.
            JsonObject edited = (JsonObject)JsonNode.Parse(File.ReadAllText(spec))!;
            ((JsonObject)edited["paths"]!).Remove("/ping");
            File.WriteAllText(spec, edited.ToJsonString());
            Directory.SetCurrentDirectory(repo);

            CommandRun withPath = Api("diff", "--base", "git:HEAD:spec.json", "--source", spec, "--json");
            CommandRun sameFile = Api("diff", "--base", "git:HEAD", "--source", spec, "--json");

            foreach (CommandRun run in new[] { withPath, sameFile })
            {
                JsonElement root = JsonDocument.Parse(run.Output).RootElement;
                Assert.Equal(0, run.ExitCode);
                JsonElement change = Assert.Single(root.GetProperty("changes").EnumerateArray());
                Assert.Equal("OPERATION_REMOVED", change.GetProperty("kind").GetString());
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            DeleteRepository(repo);
        }
    }

    private static void Git(string directory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
    }

    /// <summary>Git marks object files read-only, which Directory.Delete refuses on Windows.</summary>
    private static void DeleteRepository(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(directory, recursive: true);
    }
}
