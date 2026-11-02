using TUnit.Engine.Reporters.Aggregation;
using TUnit.Engine.Reporters.History;
using TUnit.Engine.Reporters.Html;

namespace TUnit.Reporting.Tool;

/// <summary><c>tunit-report history append</c>: records one run in a history store.</summary>
internal static class HistoryCommand
{
    internal const int DefaultKeep = 200;

    internal static int Run(string[] args)
    {
        if (args.Length < 2 || args[1] != "append")
        {
            Console.Error.WriteLine("error: expected 'tunit-report history append'.");
            return 2;
        }

        string? report = null;
        string? store = null;
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
                default:
                    Console.Error.WriteLine($"error: unknown option '{args[i]}'.");
                    return 2;
            }
        }

        if (report is null || store is null)
        {
            Console.Error.WriteLine("error: --report and --store are required.");
            return 2;
        }

        if (LoadReport(Path.GetFullPath(report)) is not { } data)
        {
            return 2;
        }

        var run = RunRecord.FromReport(data, DateTimeOffset.UtcNow);
        var historyStore = new FileRunHistoryStore(Path.GetFullPath(store));
        if (historyStore.Append(run, keep))
        {
            Console.WriteLine($"Appended run of {run.Suite} ({run.Counts.Total} tests) to {historyStore.FilePath}.");
        }
        else
        {
            Console.WriteLine($"Run of {run.Suite} at {run.Timestamp:u} is already in {historyStore.FilePath}; nothing appended.");
        }

        return 0;
    }

    // A directory is merged into one run, so a CI job with several test projects
    // (or target frameworks) adds a single point to the trend.
    private static ReportData? LoadReport(string path)
    {
        if (Directory.Exists(path))
        {
            var (suites, _) = SidecarLoader.Load(path);
            if (suites.Count == 0)
            {
                Console.Error.WriteLine($"error: no TUnit report sidecars (*{ReportDataJson.SidecarExtension}) found under {path}.");
                return null;
            }
            return ReportDataMerger.Merge(suites);
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"error: report not found: {path}");
            return null;
        }

        if (ReportDataJson.TryDeserialize(File.ReadAllBytes(path)) is { } data)
        {
            return data;
        }

        Console.Error.WriteLine($"error: {path} is not a readable TUnit report sidecar.");
        return null;
    }
}
