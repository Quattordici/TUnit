using System.Text;
using TUnit.Engine.Configuration;
using TUnit.Engine.Reporters.Aggregation;
using TUnit.Engine.Reporters.Html;

namespace TUnit.Reporting.Tool;

/// <summary><c>tunit-report merge</c>: merges report sidecars into one HTML report and job summary.</summary>
internal static class MergeCommand
{
    internal static int Run(string[] args, ToolContext context)
    {
        string? directory = null;
        string? output = null;
        var githubSummary = false;
        var collapsible = true;
        var failOnFailures = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-d" or "--directory":
                    directory = OptionReader.RequireValue(args, ref i);
                    break;
                case "-o" or "--output":
                    output = OptionReader.RequireValue(args, ref i);
                    break;
                case "--github-summary":
                    githubSummary = true;
                    break;
                case "--style":
                    collapsible = OptionReader.RequireValue(args, ref i).ToLowerInvariant() switch
                    {
                        "collapsible" => true,
                        "full" => false,
                        var other => throw new ArgumentException($"unknown --style '{other}' (expected 'collapsible' or 'full')"),
                    };
                    break;
                case "--fail-on-failures":
                    failOnFailures = true;
                    break;
                default:
                    context.Error.WriteLine($"error: unknown option '{args[i]}'.");
                    return 2;
            }
        }

        if (directory is null)
        {
            context.Error.WriteLine("error: --directory is required.");
            return 2;
        }

        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory))
        {
            context.Error.WriteLine($"error: directory not found: {directory}");
            return 2;
        }

        var (suites, skipped) = SidecarLoader.Load(directory);
        if (suites.Count == 0)
        {
            context.Error.WriteLine(
                $"error: no TUnit report sidecars (*{ReportDataJson.SidecarExtension}) found under {directory}. " +
                "Sidecars are written by TUnit next to each HTML report unless TUNIT_DISABLE_JSON_REPORT is set.");
            return 2;
        }

        if (skipped > 0)
        {
            context.Out.WriteLine($"warning: skipped {skipped} unreadable or incompatible sidecar file(s).");
        }

        var merged = ReportDataMerger.Merge(suites);
        output ??= Path.Combine(directory, ReportDataJson.MergedReportFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, HtmlReportGenerator.GenerateHtml(merged), Encoding.UTF8);

        var s = merged.Summary;
        context.Out.WriteLine($"Merged {suites.Count} suite(s): {s.Total} tests — {s.Passed} passed, {s.TotalFailed} failed, {s.Skipped} skipped.");
        context.Out.WriteLine($"Merged HTML report written to: {output}");

        if (githubSummary)
        {
            WriteGitHubSummary(suites, collapsible, context);
        }

        return failOnFailures && s.TotalFailed + s.Cancelled > 0 ? 1 : 0;
    }

    private static void WriteGitHubSummary(List<ReportData> suites, bool collapsible, ToolContext context)
    {
        var serverUrl = context.GetEnvironmentVariable(EnvironmentConstants.GitHubServerUrl)
            ?? EnvironmentConstants.GitHubDefaultServerUrl;

        var content = AggregatedSummaryWriter.Render(suites, collapsible, serverUrl);

        var summaryPath = context.GetEnvironmentVariable(EnvironmentConstants.GitHubStepSummary);
        if (string.IsNullOrEmpty(summaryPath))
        {
            context.Out.WriteLine("GITHUB_STEP_SUMMARY is not set — printing the merged summary to stdout:");
            context.Out.WriteLine(content);
            return;
        }

        if (GitHubSummaryRegion.ReplaceOrAppend(summaryPath!, content))
        {
            context.Out.WriteLine("Merged summary written to the GitHub step summary.");
        }
        else
        {
            context.Error.WriteLine($"warning: could not update the GitHub step summary at {summaryPath}.");
        }
    }
}
