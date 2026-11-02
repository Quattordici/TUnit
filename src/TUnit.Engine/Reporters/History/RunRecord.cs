using System.Globalization;
using TUnit.Engine.Reporters.Html;

namespace TUnit.Engine.Reporters.History;

/// <summary>
/// One test run as kept in the run history: the run's metadata plus a compact
/// status/duration entry per test. Deliberately much smaller than the report sidecar
/// (no output, exceptions or spans) so hundreds of runs stay cheap to cache in CI.
/// </summary>
internal sealed record RunRecord(
    DateTimeOffset Timestamp,
    string Suite,
    string? CommitSha,
    string? Branch,
    string? PullRequestNumber,
    double DurationMs,
    RunCounts Counts,
    IReadOnlyList<TestRunEntry> Tests)
{
    /// <summary>The write format of <see cref="ReportData.Timestamp"/>.</summary>
    internal const string ReportTimestampFormat = "dd MMM yyyy, HH:mm:ss 'UTC'";

    /// <summary>Link to the commit's page on the source-control provider, when it could be derived.</summary>
    internal string? CommitUrl { get; init; }

    /// <summary>Identifies a run so appending the same report twice is a no-op.</summary>
    internal string Key => $"{Timestamp.UtcTicks}|{Suite}|{CommitSha}";

    internal static RunRecord FromReport(ReportData report, DateTimeOffset fallbackTimestamp)
    {
        var tests = new List<TestRunEntry>(report.Summary.Total);
        foreach (var group in report.Groups)
        {
            foreach (var test in group.Tests)
            {
                tests.Add(new TestRunEntry(
                    test.Id,
                    $"{test.ClassName}.{test.DisplayName}",
                    MapStatus(test.Status),
                    test.DurationMs,
                    test.RetryAttempt + 1));
            }
        }

        var summary = report.Summary;
        return new RunRecord(
            TryParseReportTimestamp(report.Timestamp) ?? fallbackTimestamp,
            report.AssemblyName,
            report.CommitSha,
            report.Branch,
            report.PullRequestNumber,
            report.TotalDurationMs,
            new RunCounts(summary.Total, summary.Passed, summary.TotalFailed, summary.Skipped, summary.Cancelled, summary.Flaky),
            tests)
        {
            CommitUrl = CommitLink.FromSourceLinks(report.SourceLinks, report.CommitSha),
        };
    }

    internal static DateTimeOffset? TryParseReportTimestamp(string timestamp)
        => DateTimeOffset.TryParseExact(timestamp, ReportTimestampFormat, CultureInfo.InvariantCulture,
               DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    // Unknown statuses count as cancelled: they neither passed nor are a test failure.
    private static TestRunStatus MapStatus(string status) => status switch
    {
        "passed" => TestRunStatus.Passed,
        "failed" or "error" or "timedOut" => TestRunStatus.Failed,
        "skipped" => TestRunStatus.Skipped,
        _ => TestRunStatus.Cancelled,
    };
}

internal sealed record RunCounts(int Total, int Passed, int Failed, int Skipped, int Cancelled, int Flaky)
{
    /// <summary>Passed share of the tests that actually ran; <see langword="null"/> when none ran.</summary>
    internal double? PassRate => Passed + Failed == 0 ? null : (double)Passed / (Passed + Failed);
}

internal sealed record TestRunEntry(string Id, string Name, TestRunStatus Status, double DurationMs, int Attempts);

internal enum TestRunStatus
{
    Passed,
    Failed,
    Skipped,
    Cancelled,
}
