using TUnit.Engine.Reporters.Aggregation;
using TUnit.Engine.Reporters.History;

namespace TUnit.Reporting.Tool;

/// <summary><c>tunit-report trends</c>: renders a history store as an HTML trends page.</summary>
internal static class TrendsCommand
{
    internal const string DefaultOutputFileName = "trends.html";

    internal static int Run(string[] args)
    {
        string? store = null;
        string? output = null;
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
                default:
                    Console.Error.WriteLine($"error: unknown option '{args[i]}'.");
                    return 2;
            }
        }

        if (store is null)
        {
            Console.Error.WriteLine("error: --store is required.");
            return 2;
        }

        store = Path.GetFullPath(store);
        var report = RunHistoryAnalyzer.Analyze(new FileRunHistoryStore(store).Load(), options);
        if (report.Runs.Count == 0)
        {
            Console.WriteLine($"warning: no runs found in {store}{(options.Branch is null ? "" : $" for branch '{options.Branch}'")}.");
        }

        output = Path.GetFullPath(output ?? Path.Combine(store, DefaultOutputFileName));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        AtomicFile.WriteAllText(output, TrendsHtmlGenerator.Generate(report));

        Console.WriteLine($"Trends for {report.Runs.Count} run(s) written to: {output}");
        return 0;
    }
}
