using System.Text;
using TUnit.Engine.Configuration;
using TUnit.Engine.Reporters.Aggregation;
using TUnit.Engine.Reporters.Html;

namespace TUnit.Reporting.Tool;

internal static class Program
{
    private const string Usage =
        """
        tunit-report — merge TUnit test reports and track how test runs trend over time.

        Usage:
          tunit-report merge --directory <dir> [options]
          tunit-report history append --report <file|dir> --store <dir> [--keep <n>]
          tunit-report trends --store <dir> [options]

        merge options:
          -d, --directory <dir>    Directory scanned recursively for *.tunit-report.json
                                   sidecars (written by TUnit next to each HTML report).
          -o, --output <file>      Path for the merged HTML report.
                                   Default: <directory>/merged-report.html
          --github-summary         Also write the merged summary block to the file that
                                   $GITHUB_STEP_SUMMARY points at (or stdout when unset).
          --style <style>          Summary style: collapsible (default) or full.
          --fail-on-failures       Exit with code 1 when any merged test failed.

        history append options:
          -r, --report <file|dir>  A *.tunit-report.json sidecar, or a directory whose
                                   sidecars are merged into a single run.
          -s, --store <dir>        History directory (history.jsonl is created on demand).
          --keep <n>               Keep only the newest n runs. Default: 200.

        trends options:
          -s, --store <dir>        History directory written by 'history append'.
          -o, --output <file>      Path for the trends HTML page.
                                   Default: <store>/trends.html
          --last <n>               Analyze only the newest n runs. Default: 50.
          --branch <name>          Analyze only runs from this branch.

          -h, --help               Show this help.

        Exit codes: 0 success · 1 failures present (with --fail-on-failures) · 2 error.
        """;

    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        switch (args[0])
        {
            case "merge":
                return RunMerge(args);
            case "history":
                return HistoryCommand.Run(args);
            case "trends":
                return TrendsCommand.Run(args);
            default:
                Console.Error.WriteLine($"error: unknown command '{args[0]}'.");
                Console.WriteLine(Usage);
                return 2;
        }
    }

    private static int RunMerge(string[] args)
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
                case "-h" or "--help":
                    Console.WriteLine(Usage);
                    return 0;
                default:
                    Console.Error.WriteLine($"error: unknown option '{args[i]}'.");
                    return 2;
            }
        }

        if (directory is null)
        {
            Console.Error.WriteLine("error: --directory is required.");
            return 2;
        }

        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"error: directory not found: {directory}");
            return 2;
        }

        var (suites, skipped) = SidecarLoader.Load(directory);
        if (suites.Count == 0)
        {
            Console.Error.WriteLine(
                $"error: no TUnit report sidecars (*{ReportDataJson.SidecarExtension}) found under {directory}. " +
                "Sidecars are written by TUnit next to each HTML report unless TUNIT_DISABLE_JSON_REPORT is set.");
            return 2;
        }

        if (skipped > 0)
        {
            Console.WriteLine($"warning: skipped {skipped} unreadable or incompatible sidecar file(s).");
        }

        var merged = ReportDataMerger.Merge(suites);
        output ??= Path.Combine(directory, ReportDataJson.MergedReportFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, HtmlReportGenerator.GenerateHtml(merged), Encoding.UTF8);

        var s = merged.Summary;
        Console.WriteLine($"Merged {suites.Count} suite(s): {s.Total} tests — {s.Passed} passed, {s.TotalFailed} failed, {s.Skipped} skipped.");
        Console.WriteLine($"Merged HTML report written to: {output}");

        if (githubSummary)
        {
            WriteGitHubSummary(suites, collapsible);
        }

        return failOnFailures && s.TotalFailed + s.Cancelled > 0 ? 1 : 0;
    }

    private static void WriteGitHubSummary(List<ReportData> suites, bool collapsible)
    {
        var serverUrl = Environment.GetEnvironmentVariable(EnvironmentConstants.GitHubServerUrl)
            ?? EnvironmentConstants.GitHubDefaultServerUrl;

        var content = AggregatedSummaryWriter.Render(suites, collapsible, serverUrl);

        var summaryPath = Environment.GetEnvironmentVariable(EnvironmentConstants.GitHubStepSummary);
        if (string.IsNullOrEmpty(summaryPath))
        {
            Console.WriteLine("GITHUB_STEP_SUMMARY is not set — printing the merged summary to stdout:");
            Console.WriteLine(content);
            return;
        }

        if (GitHubSummaryRegion.ReplaceOrAppend(summaryPath!, content))
        {
            Console.WriteLine("Merged summary written to the GitHub step summary.");
        }
        else
        {
            Console.Error.WriteLine($"warning: could not update the GitHub step summary at {summaryPath}.");
        }
    }
}
