namespace TUnit.Reporting.Tool.Tests;

public class TrendsCommandTests
{
    [Test]
    public async Task Trends_Writes_The_Page_Next_To_The_History_By_Default()
    {
        using var tool = new ToolHarness();
        AppendRuns(tool);

        var result = tool.Run("trends", "--store", tool.Store);

        var page = Path.Combine(tool.Store, "trends.html");
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Out).Contains("Trends for 2 run(s) written to: " + page);
        await Assert.That(File.ReadAllText(page)).Contains("Test trends");
    }

    [Test]
    public async Task Trends_Honors_Output_Last_And_Branch()
    {
        using var tool = new ToolHarness();
        AppendRuns(tool);
        var output = Path.Combine(tool.Root, "site", "trends.html");

        var result = tool.Run("trends", "--store", tool.Store, "--output", output, "--last", "1", "--branch", "main");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Out).Contains("Trends for 1 run(s)");
        await Assert.That(File.ReadAllText(output)).Contains("on <strong>main</strong>");
    }

    [Test]
    public async Task Trends_Warns_But_Succeeds_When_The_History_Is_Empty()
    {
        using var tool = new ToolHarness();

        var result = tool.Run("trends", "--store", tool.Store);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Out).Contains("warning: no runs found");
    }

    [Test]
    public async Task GitHub_Summary_Is_Appended_To_The_Step_Summary_File()
    {
        using var tool = new ToolHarness();
        AppendRuns(tool);
        var summary = Path.Combine(tool.Root, "summary.md");
        File.WriteAllText(summary, "existing content\n");
        tool.SetEnvironment("GITHUB_STEP_SUMMARY", summary);

        var result = tool.Run("trends", "--store", tool.Store, "--github-summary");

        var written = File.ReadAllText(summary);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(written).StartsWith("existing content\n");
        await Assert.That(written).Contains("### 📈 Test trends");
        await Assert.That(written).Contains("`SampleTests.b`");
    }

    [Test]
    public async Task GitHub_Summary_Goes_To_Stdout_Outside_GitHub_Actions()
    {
        using var tool = new ToolHarness();
        AppendRuns(tool);

        var result = tool.Run("trends", "--store", tool.Store, "--github-summary");

        await Assert.That(result.Out).Contains("GITHUB_STEP_SUMMARY is not set");
        await Assert.That(result.Out).Contains("### 📈 Test trends");
    }

    [Test]
    [Arguments("trends")]
    [Arguments("trends", "--store", "x", "--last", "none")]
    [Arguments("trends", "--store", "x", "--bogus")]
    public async Task Invalid_Arguments_Fail_With_Exit_Code_2(params string[] args)
    {
        using var tool = new ToolHarness();

        var result = tool.Run(args);

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Error).StartsWith("error:");
    }

    // Two runs of one suite: "b" passes, then regresses.
    private static void AppendRuns(ToolHarness tool)
    {
        var first = tool.WriteSidecar("run1", "My.Tests", "14 Jul 2026, 10:00:00 UTC", ("a", "passed", 10), ("b", "passed", 10));
        var second = tool.WriteSidecar("run2", "My.Tests", "15 Jul 2026, 10:00:00 UTC", ("a", "passed", 10), ("b", "failed", 10));
        tool.Run("history", "append", "--report", first, "--store", tool.Store);
        tool.Run("history", "append", "--report", second, "--store", tool.Store);
    }
}
