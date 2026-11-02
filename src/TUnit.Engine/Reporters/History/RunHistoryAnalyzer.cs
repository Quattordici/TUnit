namespace TUnit.Engine.Reporters.History;

/// <param name="Last">How many of the most recent runs to analyze.</param>
/// <param name="Branch">Only analyze runs from this branch; <see langword="null"/> for all.</param>
internal sealed record TrendOptions(int Last = 50, string? Branch = null);

/// <summary>One test's outcome in one run.</summary>
internal sealed record TestResultPoint(DateTimeOffset Timestamp, TestRunStatus Status, double DurationMs, int Attempts)
{
    internal bool PassedAfterRetry => Status == TestRunStatus.Passed && Attempts > 1;
}

/// <summary>How one test behaved across the analyzed runs.</summary>
/// <param name="Runs">Runs in which the test executed (passed or failed); skips don't count.</param>
/// <param name="FlakyRuns">Runs in which the test passed only after a retry.</param>
/// <param name="Flips">Changes between passed and failed across consecutive executed runs.</param>
/// <param name="Blips">Executed results unlike both neighbors (a lone failure between passes, or the reverse).</param>
/// <param name="LastDurationMs">Duration in the latest run of its suite, when it executed there.</param>
/// <param name="BaselineDurationMs">Median duration of the earlier executed runs, once there are enough of them.</param>
/// <param name="IsInLatestRun">Whether the test is part of the latest run of its suite.</param>
/// <param name="RecentResults">Up to <see cref="RecentResultCount"/> latest results, oldest first.</param>
internal sealed record TestTrend(
    string Suite,
    string Id,
    string Name,
    int Runs,
    int Failures,
    int FlakyRuns,
    int Flips,
    int Blips,
    double MedianDurationMs,
    double? LastDurationMs,
    double? BaselineDurationMs,
    TestRunStatus LastStatus,
    bool IsInLatestRun,
    bool IsNewlyFailing,
    IReadOnlyList<TestResultPoint> RecentResults)
{
    internal const int RecentResultCount = 20;

    internal double FailureRate => Runs == 0 ? 0 : (double)Failures / Runs;

    /// <summary>
    /// Passed only after a retry, or had a one-off result between opposite outcomes. A lasting
    /// change, such as a regression and its later fix, is not flakiness.
    /// </summary>
    internal bool IsFlaky => FlakyRuns > 0 || Blips > 0;

    internal int FlakinessScore => Blips + FlakyRuns;

    /// <summary>Latest duration relative to the baseline, e.g. 2.0 for twice as slow.</summary>
    internal double? Slowdown => LastDurationMs is { } last && BaselineDurationMs is > 0 and var baseline
        ? last / baseline
        : null;
}

internal sealed class TrendReport
{
    /// <summary>A test counts as slowing down at this multiple of its baseline...</summary>
    internal const double SlowdownRatio = 1.5;

    /// <summary>...and only when that is also this much slower, to ignore jitter in fast tests.</summary>
    internal const double MinimumSlowdownMs = 50;

    internal TrendReport(IReadOnlyList<RunRecord> runs, IReadOnlyList<TestTrend> tests, TrendOptions options)
    {
        Runs = runs;
        Tests = tests;
        Options = options;
    }

    /// <summary>The analyzed runs, oldest first.</summary>
    internal IReadOnlyList<RunRecord> Runs { get; }

    internal IReadOnlyList<TestTrend> Tests { get; }

    internal TrendOptions Options { get; }

    internal RunRecord? Latest => Runs.Count > 0 ? Runs[Runs.Count - 1] : null;

    internal RunRecord? Previous => Runs.Count > 1 ? Runs[Runs.Count - 2] : null;

    /// <summary>Latest pass rate minus the previous run's, as a fraction (0.05 = 5 points).</summary>
    internal double? PassRateChange => Latest?.Counts.PassRate is { } latest && Previous?.Counts.PassRate is { } previous
        ? latest - previous
        : null;

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

    internal IReadOnlyList<TestTrend> Flakiest(int count)
        => Tests.Where(static t => t.IsFlaky)
            .OrderByDescending(static t => t.FlakinessScore)
            .ThenBy(static t => t.Name, StringComparer.Ordinal)
            .Take(count)
            .ToList();

    internal IReadOnlyList<TestTrend> SlowingDown(int count)
        => Tests.Where(static t => t.IsInLatestRun
                && t.Slowdown >= SlowdownRatio
                && t.LastDurationMs - t.BaselineDurationMs >= MinimumSlowdownMs)
            .OrderByDescending(static t => t.Slowdown)
            .ThenBy(static t => t.Name, StringComparer.Ordinal)
            .Take(count)
            .ToList();
}

/// <summary>Turns stored runs into per-run and per-test trends.</summary>
internal static class RunHistoryAnalyzer
{
    /// <summary>Earlier executed runs needed before a test's latest duration is compared with them.</summary>
    internal const int MinimumBaselineRuns = 3;

    internal static TrendReport Analyze(IReadOnlyList<RunRecord> history, TrendOptions options)
    {
        var filtered = history
            .Where(r => options.Branch is null || string.Equals(r.Branch, options.Branch, StringComparison.Ordinal))
            .OrderBy(static r => r.Timestamp)
            .ToList();
        var runs = filtered.Skip(Math.Max(0, filtered.Count - options.Last)).ToList();

        return new TrendReport(runs, BuildTestTrends(runs), options);
    }

    private static List<TestTrend> BuildTestTrends(List<RunRecord> runs)
    {
        // "Latest" is per suite: suites stored as separate records don't all run at the same moment.
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
            trends.Add(BuildTestTrend(pair.Key.Suite, pair.Key.Id, pair.Value, latestRunPerSuite[pair.Key.Suite]));
        }

        return trends;
    }

    private static TestTrend BuildTestTrend(string suite, string id, List<(RunRecord Run, TestRunEntry Entry)> entries, RunRecord latestSuiteRun)
    {
        var executed = entries.Where(static e => IsExecuted(e.Entry.Status)).Select(static e => e.Entry).ToList();
        var last = entries[entries.Count - 1];
        var isInLatestRun = ReferenceEquals(last.Run, latestSuiteRun);
        var lastExecutedInLatestRun = isInLatestRun && IsExecuted(last.Entry.Status);

        var flips = 0;
        var blips = 0;
        for (var i = 1; i < executed.Count; i++)
        {
            if (executed[i].Status != executed[i - 1].Status)
            {
                flips++;
                if (i + 1 < executed.Count && executed[i + 1].Status == executed[i - 1].Status)
                {
                    blips++;
                }
            }
        }

        var earlier = lastExecutedInLatestRun ? executed.Take(executed.Count - 1).ToList() : executed;
        var baseline = lastExecutedInLatestRun && earlier.Count >= MinimumBaselineRuns
            ? Median(earlier.Select(static e => e.DurationMs))
            : (double?)null;

        var isNewlyFailing = isInLatestRun
            && last.Entry.Status == TestRunStatus.Failed
            && (entries.Count == 1 || entries[entries.Count - 2].Entry.Status != TestRunStatus.Failed);

        var recent = entries
            .Skip(Math.Max(0, entries.Count - TestTrend.RecentResultCount))
            .Select(static e => new TestResultPoint(e.Run.Timestamp, e.Entry.Status, e.Entry.DurationMs, e.Entry.Attempts))
            .ToList();

        return new TestTrend(
            suite,
            id,
            last.Entry.Name,
            executed.Count,
            executed.Count(static e => e.Status == TestRunStatus.Failed),
            executed.Count(static e => e.Status == TestRunStatus.Passed && e.Attempts > 1),
            flips,
            blips,
            Median(executed.Select(static e => e.DurationMs)),
            lastExecutedInLatestRun ? last.Entry.DurationMs : null,
            baseline,
            last.Entry.Status,
            isInLatestRun,
            isNewlyFailing,
            recent);
    }

    private static bool IsExecuted(TestRunStatus status) => status is TestRunStatus.Passed or TestRunStatus.Failed;

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
