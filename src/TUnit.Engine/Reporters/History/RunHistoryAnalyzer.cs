namespace TUnit.Engine.Reporters.History;

/// <param name="Last">How many of the most recent runs to analyze.</param>
/// <param name="Branch">Only analyze runs from this branch; <see langword="null"/> for all.</param>
internal sealed record TrendOptions(int Last = 50, string? Branch = null);

/// <summary>How one test behaved across the analyzed runs.</summary>
internal sealed record TestTrend(
    string Suite,
    string Id,
    string Name,
    int Runs,
    int Failures,
    int FlakyRuns,
    double MedianDurationMs,
    TestRunStatus LastStatus,
    bool IsNewlyFailing)
{
    internal double FailureRate => Runs == 0 ? 0 : (double)Failures / Runs;
}

internal sealed class TrendReport
{
    internal TrendReport(IReadOnlyList<RunRecord> runs, IReadOnlyList<TestTrend> tests)
    {
        Runs = runs;
        Tests = tests;
    }

    /// <summary>The analyzed runs, oldest first.</summary>
    internal IReadOnlyList<RunRecord> Runs { get; }

    internal IReadOnlyList<TestTrend> Tests { get; }

    internal IReadOnlyList<TestTrend> Slowest(int count)
        => Tests.OrderByDescending(static t => t.MedianDurationMs).ThenBy(static t => t.Name, StringComparer.Ordinal).Take(count).ToList();

    internal IReadOnlyList<TestTrend> MostFailing(int count)
        => Tests.Where(static t => t.Failures > 0)
            .OrderByDescending(static t => t.Failures)
            .ThenBy(static t => t.Name, StringComparer.Ordinal)
            .Take(count)
            .ToList();

    internal IReadOnlyList<TestTrend> NewlyFailing()
        => Tests.Where(static t => t.IsNewlyFailing).OrderBy(static t => t.Name, StringComparer.Ordinal).ToList();
}

/// <summary>Turns stored runs into per-run and per-test trends.</summary>
internal static class RunHistoryAnalyzer
{
    internal static TrendReport Analyze(IReadOnlyList<RunRecord> history, TrendOptions options)
    {
        var filtered = history
            .Where(r => options.Branch is null || string.Equals(r.Branch, options.Branch, StringComparison.Ordinal))
            .OrderBy(static r => r.Timestamp)
            .ToList();
        var runs = filtered.Skip(Math.Max(0, filtered.Count - options.Last)).ToList();

        return new TrendReport(runs, BuildTestTrends(runs));
    }

    private static List<TestTrend> BuildTestTrends(List<RunRecord> runs)
    {
        // A test can only be "newly failing" if it failed in the latest run of its own suite;
        // suites stored as separate records don't all run at the same moment.
        var latestRunPerSuite = new Dictionary<string, RunRecord>(StringComparer.Ordinal);
        var entriesPerTest = new Dictionary<(string Suite, string Id), List<(RunRecord Run, TestRunEntry Entry)>>();
        foreach (var run in runs)
        {
            latestRunPerSuite[run.Suite] = run;
            foreach (var entry in run.Tests)
            {
                var key = (run.Suite, entry.Id);
                if (!entriesPerTest.TryGetValue(key, out var entries))
                {
                    entriesPerTest[key] = entries = [];
                }
                entries.Add((run, entry));
            }
        }

        var trends = new List<TestTrend>(entriesPerTest.Count);
        foreach (var pair in entriesPerTest)
        {
            var entries = pair.Value;
            var executed = entries.Where(static e => e.Entry.Status is TestRunStatus.Passed or TestRunStatus.Failed).ToList();
            var last = entries[entries.Count - 1];
            var isNewlyFailing = last.Entry.Status == TestRunStatus.Failed
                && ReferenceEquals(last.Run, latestRunPerSuite[pair.Key.Suite])
                && (entries.Count == 1 || entries[entries.Count - 2].Entry.Status != TestRunStatus.Failed);

            trends.Add(new TestTrend(
                pair.Key.Suite,
                pair.Key.Id,
                last.Entry.Name,
                executed.Count,
                executed.Count(static e => e.Entry.Status == TestRunStatus.Failed),
                executed.Count(static e => e.Entry.Status == TestRunStatus.Passed && e.Entry.Attempts > 1),
                Median(executed.Select(static e => e.Entry.DurationMs)),
                last.Entry.Status,
                isNewlyFailing));
        }

        return trends;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(static v => v).ToList();
        if (sorted.Count == 0)
        {
            return 0;
        }

        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
