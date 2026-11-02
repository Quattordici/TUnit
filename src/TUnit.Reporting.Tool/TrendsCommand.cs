using TUnit.Engine.Configuration;
using TUnit.Engine.Reporters.Aggregation;
using TUnit.Engine.Reporters.History;

namespace TUnit.Reporting.Tool;

/// <summary><c>tunit-report trends</c>: renders a history store as an HTML trends page.</summary>
internal static class TrendsCommand
{
    internal const string DefaultOutputFileName = "trends.html";

    internal static int Run(string[] args, ToolContext context)
    {
        string? store = null;
        string? output = null;
        var githubSummary = false;
        var options = new TrendOptions();

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-s" or "--store":
                    store = OptionReader.RequireValue(args, ref i);
                    break;
                case "-o" or "--output":
                    output = OptionReader.RequireValue(args, ref i);
                    break;
                case "--last":
                    options = options with { Last = OptionReader.RequirePositiveInt(args, ref i) };
                    break;
                case "--branch":
                    options = options with { Branch = OptionReader.RequireValue(args, ref i) };
                    break;
                case "--github-summary":
                    githubSummary = true;
                    break;
                default:
                    context.Error.WriteLine($"error: unknown option '{args[i]}'.");
                    return 2;
            }
        }

        if (store is null)
        {
            context.Error.WriteLine("error: --store is required.");
            return 2;
        }

        store = Path.GetFullPath(store);
        var report = RunHistoryAnalyzer.Analyze(new FileRunHistoryStore(store).Load(), options);
        if (report.Runs.Count == 0)
        {
            context.Out.WriteLine($"warning: no runs found in {store}{(options.Branch is null ? "" : $" for branch '{options.Branch}'")}.");
        }

        output = Path.GetFullPath(output ?? Path.Combine(store, DefaultOutputFileName));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        AtomicFile.WriteAllText(output, TrendsHtmlGenerator.Generate(report));
        context.Out.WriteLine($"Trends for {report.Runs.Count} run(s) written to: {output}");

        if (githubSummary)
        {
            WriteGitHubSummary(TrendsMarkdownGenerator.Generate(report), context);
        }

        return 0;
    }

    // Appended rather than spliced into a marked region: the trends summary is written once,
    // after the tests, and must sit alongside whatever the job already wrote.
    private static void WriteGitHubSummary(string markdown, ToolContext context)
    {
        var summaryPath = context.GetEnvironmentVariable(EnvironmentConstants.GitHubStepSummary);
        if (string.IsNullOrEmpty(summaryPath))
        {
            context.Out.WriteLine("GITHUB_STEP_SUMMARY is not set — printing the trends summary to stdout:");
            context.Out.WriteLine(markdown);
            return;
        }

        File.AppendAllText(summaryPath!, "\n" + markdown);
        context.Out.WriteLine("Trends summary written to the GitHub step summary.");
    }
}
