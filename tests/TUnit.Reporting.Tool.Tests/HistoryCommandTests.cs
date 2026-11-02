using TUnit.Engine.Reporters.History;

namespace TUnit.Reporting.Tool.Tests;

public class HistoryCommandTests
{
    [Test]
    public async Task Append_From_A_Sidecar_Records_One_Run_Named_After_The_Assembly()
    {
        using var tool = new ToolHarness();
        var sidecar = tool.WriteSidecar("results", "My.Tests", tests: [("a", "passed", 10), ("b", "failed", 20)]);

        var result = tool.Run("history", "append", "--report", sidecar, "--store", tool.Store);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Out).Contains("Appended run of My.Tests (2 tests)");
        var run = new FileRunHistoryStore(tool.Store).Load().Single();
        await Assert.That(run.Suite).IsEqualTo("My.Tests");
        await Assert.That(run.Counts.Failed).IsEqualTo(1);
        await Assert.That(run.CommitSha).IsEqualTo("abc1234def");
    }

    [Test]
    public async Task Append_From_A_Directory_Merges_Every_Sidecar_Into_One_Stable_Run()
    {
        using var tool = new ToolHarness();
        tool.WriteSidecar(Path.Combine("results", "one"), "One.Tests", tests: [("a", "passed", 10)]);
        tool.WriteSidecar(Path.Combine("results", "two"), "Two.Tests", tests: [("b", "passed", 10), ("c", "skipped", 0)]);

        var result = tool.Run("history", "append", "--report", Path.Combine(tool.Root, "results"), "--store", tool.Store);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        var run = new FileRunHistoryStore(tool.Store).Load().Single();
        await Assert.That(run.Suite).IsEqualTo(HistoryCommand.DirectorySuiteName);
        await Assert.That(run.Counts.Total).IsEqualTo(3);
    }

    [Test]
    public async Task Append_Applies_Suite_Commit_And_Branch_Overrides()
    {
        using var tool = new ToolHarness();
        var sidecar = tool.WriteSidecar("results", "My.Tests", tests: [("a", "passed", 10)]);

        tool.Run("history", "append", "--report", sidecar, "--store", tool.Store,
            "--suite", "Backend", "--commit", "fedcba9", "--branch", "release/2.0");

        var run = new FileRunHistoryStore(tool.Store).Load().Single();
        await Assert.That(run.Suite).IsEqualTo("Backend");
        await Assert.That(run.CommitSha).IsEqualTo("fedcba9");
        await Assert.That(run.Branch).IsEqualTo("release/2.0");
    }

    [Test]
    public async Task Append_Twice_Keeps_A_Single_Run()
    {
        using var tool = new ToolHarness();
        var sidecar = tool.WriteSidecar("results", "My.Tests", tests: [("a", "passed", 10)]);

        tool.Run("history", "append", "--report", sidecar, "--store", tool.Store);
        var second = tool.Run("history", "append", "--report", sidecar, "--store", tool.Store);

        await Assert.That(second.ExitCode).IsEqualTo(0);
        await Assert.That(second.Out).Contains("already in");
        await Assert.That(new FileRunHistoryStore(tool.Store).Load().Count).IsEqualTo(1);
    }

    [Test]
    public async Task Append_Uses_The_Clock_When_The_Report_Has_No_Usable_Timestamp()
    {
        using var tool = new ToolHarness();
        var sidecar = tool.WriteSidecar("results", "My.Tests", timestamp: "", tests: [("a", "passed", 10)]);

        tool.Run("history", "append", "--report", sidecar, "--store", tool.Store);

        await Assert.That(new FileRunHistoryStore(tool.Store).Load().Single().Timestamp).IsEqualTo(ToolHarness.Now);
    }

    [Test]
    [Arguments("history")]
    [Arguments("history", "prepend")]
    [Arguments("history", "append", "--store", "x")]
    [Arguments("history", "append", "--report", "x")]
    [Arguments("history", "append", "--report", "x", "--store", "y", "--keep", "0")]
    [Arguments("history", "append", "--report", "x", "--store", "y", "--keep")]
    [Arguments("history", "append", "--bogus")]
    public async Task Invalid_Arguments_Fail_With_Exit_Code_2(params string[] args)
    {
        using var tool = new ToolHarness();

        var result = tool.Run(args);

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Error).StartsWith("error:");
    }

    [Test]
    public async Task Missing_Or_Unreadable_Reports_Fail_With_Exit_Code_2()
    {
        using var tool = new ToolHarness();
        var notASidecar = Path.Combine(tool.Root, "junk.tunit-report.json");
        File.WriteAllText(notASidecar, "not json");
        Directory.CreateDirectory(Path.Combine(tool.Root, "empty"));

        var missing = tool.Run("history", "append", "--report", Path.Combine(tool.Root, "nope"), "--store", tool.Store);
        var unreadable = tool.Run("history", "append", "--report", notASidecar, "--store", tool.Store);
        var empty = tool.Run("history", "append", "--report", Path.Combine(tool.Root, "empty"), "--store", tool.Store);

        await Assert.That(missing.ExitCode).IsEqualTo(2);
        await Assert.That(missing.Error).Contains("not found");
        await Assert.That(unreadable.ExitCode).IsEqualTo(2);
        await Assert.That(unreadable.Error).Contains("not a readable TUnit report sidecar");
        await Assert.That(empty.ExitCode).IsEqualTo(2);
        await Assert.That(empty.Error).Contains("no TUnit report sidecars");
    }
}
