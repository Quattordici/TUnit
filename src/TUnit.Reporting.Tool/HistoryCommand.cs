using TUnit.Engine.Reporters.Aggregation;
using TUnit.Engine.Reporters.History;
using TUnit.Engine.Reporters.Html;

namespace TUnit.Reporting.Tool;

/// <summary><c>tunit-report history append</c>: records one run in a history store.</summary>
internal static class HistoryCommand
{
    internal const int DefaultKeep = 200;

    /// <summary>
    /// Suite name for a run merged from a directory. Fixed rather than derived from the merged
    /// report ("3 Test Suites"), so adding a test project doesn't break every test's history.
    /// </summary>
    internal const string DirectorySuiteName = "All tests";

    internal static int Run(string[] args, ToolContext context)
    {
        if (args.Length < 2 || args[1] != "append")
        {
            context.Error.WriteLine("error: expected 'tunit-report history append'.");
            return 2;
        }

        string? report = null;
        string? store = null;
        string? suite = null;
        string? commit = null;
        string? branch = null;
        var keep = DefaultKeep;

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-r" or "--report":
                    report = OptionReader.RequireValue(args, ref i);
                    break;
                case "-s" or "--store":
                    store = OptionReader.RequireValue(args, ref i);
                    break;
                case "--keep":
                    keep = OptionReader.RequirePositiveInt(args, ref i);
                    break;
                case "--suite":
                    suite = OptionReader.RequireValue(args, ref i);
                    break;
                case "--commit":
                    commit = OptionReader.RequireValue(args, ref i);
                    break;
                case "--branch":
                    branch = OptionReader.RequireValue(args, ref i);
                    break;
                default:
                    context.Error.WriteLine($"error: unknown option '{args[i]}'.");
                    return 2;
            }
        }

        if (report is null || store is null)
        {
            context.Error.WriteLine("error: --report and --store are required.");
            return 2;
        }

        if (LoadReport(Path.GetFullPath(report), context) is not { } loaded)
        {
            return 2;
        }

        var recorded = RunRecord.FromReport(loaded.Data, context.UtcNow());
        var run = recorded with
        {
            Suite = suite ?? loaded.Suite,
            // An overridden commit invalidates the detected commit link.
            CommitSha = commit ?? recorded.CommitSha,
            CommitUrl = commit is null ? recorded.CommitUrl : null,
            Branch = branch ?? recorded.Branch,
        };

        var historyStore = new FileRunHistoryStore(Path.GetFullPath(store));
        if (historyStore.Append(run, keep))
        {
            context.Out.WriteLine($"Appended run of {run.Suite} ({run.Counts.Total} tests) to {historyStore.FilePath}.");
        }
        else
        {
            context.Out.WriteLine($"Run of {run.Suite} at {run.Timestamp:u} is already in {historyStore.FilePath}; nothing appended.");
        }

        return 0;
    }

    // A directory is merged into one run, so a CI job with several test projects
    // (or target frameworks) adds a single point to the trend.
    private static (ReportData Data, string Suite)? LoadReport(string path, ToolContext context)
    {
        if (Directory.Exists(path))
        {
            var (suites, skipped) = SidecarLoader.Load(path);
            if (suites.Count == 0)
            {
                context.Error.WriteLine($"error: no TUnit report sidecars (*{ReportDataJson.SidecarExtension}) found under {path}.");
                return null;
            }

            if (skipped > 0)
            {
                context.Out.WriteLine($"warning: skipped {skipped} unreadable or incompatible sidecar file(s).");
            }

            return (ReportDataMerger.Merge(suites), DirectorySuiteName);
        }

        if (!File.Exists(path))
        {
            context.Error.WriteLine($"error: report not found: {path}");
            return null;
        }

        if (ReportDataJson.TryDeserialize(File.ReadAllBytes(path)) is { } data)
        {
            return (data, data.AssemblyName);
        }

        context.Error.WriteLine($"error: {path} is not a readable TUnit report sidecar.");
        return null;
    }
}
