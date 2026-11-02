using TUnit.Engine.Reporters.History;

namespace TUnit.UnitTests;

internal static class RunHistoryBuilder
{
    internal static DateTimeOffset Day(int day) => new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero).AddDays(day - 1);

    internal static RunRecord Run(DateTimeOffset timestamp, params (string Id, TestRunStatus Status, double DurationMs)[] tests)
        => Run(timestamp, "Tests", "main", tests);

    internal static RunRecord Run(DateTimeOffset timestamp, string branch, params (string Id, TestRunStatus Status, double DurationMs)[] tests)
        => Run(timestamp, "Tests", branch, tests);

    internal static RunRecord Run(DateTimeOffset timestamp, (string Id, TestRunStatus Status, double DurationMs, int Attempts) test)
        => Build(timestamp, "Tests", "main", [new TestRunEntry(test.Id, $"Tests.{test.Id}", test.Status, test.DurationMs, test.Attempts)]);

    internal static RunRecord Run(DateTimeOffset timestamp, string suite, string branch, (string Id, TestRunStatus Status, double DurationMs)[] tests)
        => Build(timestamp, suite, branch, tests.Select(t => new TestRunEntry(t.Id, $"Tests.{t.Id}", t.Status, t.DurationMs, 1)).ToArray());

    internal static RunRecord Run(DateTimeOffset timestamp, string suite, (string Id, TestRunStatus Status, double DurationMs) test)
        => Run(timestamp, suite, "main", [test]);

    internal static TestRunEntry Entry(string id, TestRunStatus status, double durationMs = 10, int attempts = 1)
        => new(id, $"Tests.{id}", status, durationMs, attempts);

    internal static RunRecord WithEntries(DateTimeOffset timestamp, params TestRunEntry[] tests)
        => Build(timestamp, "Tests", "main", tests);

    /// <summary>One run per status, a day apart, all for the same test.</summary>
    internal static RunRecord[] Sequence(string id, params TestRunStatus[] statuses)
        => statuses.Select((status, i) => WithEntries(Day(i + 1), Entry(id, status))).ToArray();

    internal static RunRecord Build(DateTimeOffset timestamp, string suite, string branch, TestRunEntry[] tests)
        => new(
            timestamp,
            suite,
            CommitSha: "abc123",
            Branch: branch,
            PullRequestNumber: null,
            DurationMs: tests.Sum(t => t.DurationMs),
            Counts: new RunCounts(
                Total: tests.Length,
                Passed: tests.Count(t => t.Status == TestRunStatus.Passed),
                Failed: tests.Count(t => t.Status == TestRunStatus.Failed),
                Skipped: tests.Count(t => t.Status == TestRunStatus.Skipped),
                Cancelled: tests.Count(t => t.Status == TestRunStatus.Cancelled),
                Flaky: tests.Count(t => t.Status == TestRunStatus.Passed && t.Attempts > 1)),
            Tests: tests);
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tunit-history-" + Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
