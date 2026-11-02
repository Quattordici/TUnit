namespace TUnit.Reporting.Tool.Tests;

public class ProgramTests
{
    [Test]
    public async Task No_Arguments_Prints_Usage_And_Fails()
    {
        using var tool = new ToolHarness();

        var result = tool.Run();

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Out).Contains("Usage:");
    }

    [Test]
    [Arguments("--help")]
    [Arguments("history", "append", "--help")]
    [Arguments("trends", "-h")]
    [Arguments("merge", "--help")]
    public async Task Help_Prints_Usage_For_Every_Command(params string[] args)
    {
        using var tool = new ToolHarness();

        var result = tool.Run(args);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Out).Contains("tunit-report trends --store <dir>");
    }

    [Test]
    public async Task Unknown_Command_Fails()
    {
        using var tool = new ToolHarness();

        var result = tool.Run("frobnicate");

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Error).Contains("unknown command 'frobnicate'");
    }

    [Test]
    public async Task Merge_Still_Writes_A_Combined_Report()
    {
        using var tool = new ToolHarness();
        tool.WriteSidecar("one", "One.Tests", tests: [("a", "passed", 10)]);
        tool.WriteSidecar("two", "Two.Tests", tests: [("b", "failed", 10)]);

        var result = tool.Run("merge", "--directory", tool.Root, "--fail-on-failures");

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Out).Contains("Merged 2 suite(s): 2 tests");
        await Assert.That(File.Exists(Path.Combine(tool.Root, "merged-report.html"))).IsTrue();
    }
}
