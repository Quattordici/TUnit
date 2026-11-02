namespace TUnit.Reporting.Tool;

internal static class Program
{
    internal const string Usage =
        """
        tunit-report — merge TUnit test reports and track how test runs trend over time.

        Usage:
          tunit-report merge --directory <dir> [options]
          tunit-report history append --report <file|dir> --store <dir> [options]
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
          --suite <name>           Name the run's suite. Default: the test assembly for a
                                   single sidecar, "All tests" for a directory.
          --commit <sha>           Record this commit (default: detected by TUnit in CI).
          --branch <name>          Record this branch (default: detected by TUnit in CI).

        trends options:
          -s, --store <dir>        History directory written by 'history append'.
          -o, --output <file>      Path for the trends HTML page.
                                   Default: <store>/trends.html
          --last <n>               Analyze only the newest n runs. Default: 50.
          --branch <name>          Analyze only runs from this branch.
          --github-summary         Also write a Markdown trends summary to the file that
                                   $GITHUB_STEP_SUMMARY points at (or stdout when unset).

          -h, --help               Show this help.

        Exit codes: 0 success · 1 failures present (merge --fail-on-failures) · 2 error.
        """;

    public static int Main(string[] args) => Run(args, ToolContext.FromEnvironment());

    internal static int Run(string[] args, ToolContext context)
    {
        try
        {
            return Dispatch(args, context);
        }
        catch (Exception ex)
        {
            context.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int Dispatch(string[] args, ToolContext context)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            context.Out.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        if (args.Skip(1).Any(static a => a is "-h" or "--help"))
        {
            context.Out.WriteLine(Usage);
            return 0;
        }

        switch (args[0])
        {
            case "merge":
                return MergeCommand.Run(args, context);
            case "history":
                return HistoryCommand.Run(args, context);
            case "trends":
                return TrendsCommand.Run(args, context);
            default:
                context.Error.WriteLine($"error: unknown command '{args[0]}'.");
                context.Out.WriteLine(Usage);
                return 2;
        }
    }
}
