using TUnit.Assertions.Enums;
using TUnit.Engine.Reporters.History;
using TUnit.Engine.Reporters.Html;

namespace TUnit.UnitTests;

public class RunRecordTests
{
    [Test]
    public async Task FromReport_Copies_Run_Metadata_And_Counts()
    {
        var report = TestReportData.Build("My.Tests");

        var record = RunRecord.FromReport(report, DateTimeOffset.MinValue);

        await Assert.That(record.Timestamp).IsEqualTo(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero));
        await Assert.That(record.Suite).IsEqualTo("My.Tests");
        await Assert.That(record.CommitSha).IsEqualTo("abc123");
        await Assert.That(record.Branch).IsEqualTo("main");
        await Assert.That(record.PullRequestNumber).IsEqualTo("17");
        await Assert.That(record.DurationMs).IsEqualTo(1234.5);
        await Assert.That(record.Counts).IsEqualTo(new RunCounts(Total: 3, Passed: 2, Failed: 1, Skipped: 0, Cancelled: 0, Flaky: 1));
    }

    [Test]
    public async Task FromReport_Flattens_Tests_With_Status_Duration_And_Attempts()
    {
        var record = RunRecord.FromReport(TestReportData.Build("My.Tests"), DateTimeOffset.MinValue);

        await Assert.That(record.Tests.Count).IsEqualTo(3);
        await Assert.That(record.Tests[0]).IsEqualTo(new TestRunEntry("test-1", "CalculatorTests.Adds", TestRunStatus.Passed, 10, 1));
        await Assert.That(record.Tests[1]).IsEqualTo(new TestRunEntry("test-2", "CalculatorTests.Divides", TestRunStatus.Failed, 20, 1));
        await Assert.That(record.Tests[2]).IsEqualTo(new TestRunEntry("test-3", "CalculatorTests.RetriesThenPasses", TestRunStatus.Passed, 30, 2));
    }

    [Test]
    [Arguments("passed", nameof(TestRunStatus.Passed))]
    [Arguments("failed", nameof(TestRunStatus.Failed))]
    [Arguments("error", nameof(TestRunStatus.Failed))]
    [Arguments("timedOut", nameof(TestRunStatus.Failed))]
    [Arguments("skipped", nameof(TestRunStatus.Skipped))]
    [Arguments("cancelled", nameof(TestRunStatus.Cancelled))]
    [Arguments("something-new", nameof(TestRunStatus.Cancelled))]
    public async Task FromReport_Maps_Report_Statuses(string reportStatus, string expected)
    {
        var report = TestReportData.BuildWithSingleTest("A", start: null, durationMs: 5);
        var test = report.Groups[0].Tests[0];
        report.Groups[0].Tests[0] = new ReportTestResult
        {
            Id = test.Id,
            DisplayName = test.DisplayName,
            MethodName = test.MethodName,
            ClassName = test.ClassName,
            Status = reportStatus,
        };

        var record = RunRecord.FromReport(report, DateTimeOffset.MinValue);

        await Assert.That(record.Tests[0].Status).IsEqualTo(Enum.Parse<TestRunStatus>(expected));
    }

    [Test]
    public async Task FromReport_Uses_Fallback_When_Timestamp_Is_Unparseable()
    {
        var report = TestReportData.BuildWithSingleTest("A", start: null, durationMs: 5);
        var withBadTimestamp = new ReportData
        {
            AssemblyName = report.AssemblyName,
            MachineName = report.MachineName,
            Timestamp = "not a date",
            TUnitVersion = report.TUnitVersion,
            OperatingSystem = report.OperatingSystem,
            RuntimeVersion = report.RuntimeVersion,
            Summary = report.Summary,
            Groups = report.Groups,
        };
        var fallback = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var record = RunRecord.FromReport(withBadTimestamp, fallback);

        await Assert.That(record.Timestamp).IsEqualTo(fallback);
    }
}

public class RunHistoryJsonTests
{
    [Test]
    public async Task Serialize_Then_Deserialize_RoundTrips()
    {
        var original = RunHistoryBuilder.Run(
            new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero),
            ("t1", TestRunStatus.Passed, 10),
            ("t2", TestRunStatus.Failed, 20.5));

        var line = RunHistoryJson.Serialize(original);
        var restored = RunHistoryJson.TryDeserialize(line);

        await Assert.That(line).DoesNotContain("\n");
        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.Timestamp).IsEqualTo(original.Timestamp);
        await Assert.That(restored.Suite).IsEqualTo(original.Suite);
        await Assert.That(restored.CommitSha).IsEqualTo(original.CommitSha);
        await Assert.That(restored.Branch).IsEqualTo(original.Branch);
        await Assert.That(restored.PullRequestNumber).IsEqualTo(original.PullRequestNumber);
        await Assert.That(restored.DurationMs).IsEqualTo(original.DurationMs);
        await Assert.That(restored.Counts).IsEqualTo(original.Counts);
        await Assert.That(restored.Tests).IsEquivalentTo(original.Tests);
    }

    [Test]
    [Arguments("")]
    [Arguments("not json")]
    [Arguments("[]")]
    [Arguments("{}")]
    [Arguments("""{"v":999,"timestamp":"2026-07-14T10:00:00Z","suite":"A"}""")]
    [Arguments("""{"v":1,"timestamp":"yesterday","suite":"A"}""")]
    [Arguments("""{"v":1,"timestamp":"2026-07-14T10:00:00Z","suite":"A","tests":[7]}""")]
    public async Task TryDeserialize_Rejects_Invalid_Lines(string line)
    {
        await Assert.That(RunHistoryJson.TryDeserialize(line)).IsNull();
    }
}

public class FileRunHistoryStoreTests
{
    [Test]
    public async Task Load_Returns_Empty_When_Store_Does_Not_Exist()
    {
        using var dir = new TempDirectory();
        var store = new FileRunHistoryStore(Path.Combine(dir.Path, "missing"));

        await Assert.That(store.Load()).IsEmpty();
    }

    [Test]
    public async Task Append_Then_Load_Returns_Runs_Oldest_First()
    {
        using var dir = new TempDirectory();
        var store = new FileRunHistoryStore(dir.Path);
        var newer = RunHistoryBuilder.Run(RunHistoryBuilder.Day(2), ("t1", TestRunStatus.Passed, 10));
        var older = RunHistoryBuilder.Run(RunHistoryBuilder.Day(1), ("t1", TestRunStatus.Failed, 10));

        store.Append(newer, keep: 10);
        store.Append(older, keep: 10);
        var runs = store.Load();

        await Assert.That(runs.Select(r => r.Timestamp)).IsEquivalentTo([RunHistoryBuilder.Day(1), RunHistoryBuilder.Day(2)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Append_Skips_A_Run_That_Is_Already_Stored()
    {
        using var dir = new TempDirectory();
        var store = new FileRunHistoryStore(dir.Path);
        var run = RunHistoryBuilder.Run(RunHistoryBuilder.Day(1), ("t1", TestRunStatus.Passed, 10));

        var first = store.Append(run, keep: 10);
        var second = store.Append(run, keep: 10);

        await Assert.That(first).IsTrue();
        await Assert.That(second).IsFalse();
        await Assert.That(store.Load().Count).IsEqualTo(1);
    }

    [Test]
    public async Task Append_Keeps_Only_The_Most_Recent_Runs()
    {
        using var dir = new TempDirectory();
        var store = new FileRunHistoryStore(dir.Path);

        for (var day = 1; day <= 5; day++)
        {
            store.Append(RunHistoryBuilder.Run(RunHistoryBuilder.Day(day), ("t1", TestRunStatus.Passed, 10)), keep: 3);
        }

        await Assert.That(store.Load().Select(r => r.Timestamp))
            .IsEquivalentTo([RunHistoryBuilder.Day(3), RunHistoryBuilder.Day(4), RunHistoryBuilder.Day(5)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Load_Skips_Corrupt_Lines()
    {
        using var dir = new TempDirectory();
        var store = new FileRunHistoryStore(dir.Path);
        store.Append(RunHistoryBuilder.Run(RunHistoryBuilder.Day(1), ("t1", TestRunStatus.Passed, 10)), keep: 10);
        File.AppendAllText(store.FilePath, "{ torn line\n");

        await Assert.That(store.Load().Count).IsEqualTo(1);
    }
}

public class RunHistoryAnalyzerTests
{
    [Test]
    public async Task Analyze_Limits_To_The_Last_Runs()
    {
        var runs = Enumerable.Range(1, 5)
            .Select(day => RunHistoryBuilder.Run(RunHistoryBuilder.Day(day), ("t1", TestRunStatus.Passed, 10)))
            .ToList();

        var report = RunHistoryAnalyzer.Analyze(runs, new TrendOptions(Last: 2));

        await Assert.That(report.Runs.Select(r => r.Timestamp))
            .IsEquivalentTo([RunHistoryBuilder.Day(4), RunHistoryBuilder.Day(5)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Analyze_Filters_By_Branch()
    {
        var runs = new[]
        {
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(1), branch: "main", ("t1", TestRunStatus.Passed, 10)),
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(2), branch: "feature", ("t1", TestRunStatus.Failed, 10)),
        };

        var report = RunHistoryAnalyzer.Analyze(runs, new TrendOptions(Branch: "main"));

        await Assert.That(report.Runs.Count).IsEqualTo(1);
        await Assert.That(report.Tests.Single().Failures).IsEqualTo(0);
    }

    [Test]
    public async Task Analyze_Aggregates_Per_Test_Failures_Flakiness_And_Median_Duration()
    {
        var runs = new[]
        {
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(1), ("t1", TestRunStatus.Passed, 10)),
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(2), ("t1", TestRunStatus.Failed, 40)),
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(3), ("t1", TestRunStatus.Passed, 20, 2)),
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(4), ("t1", TestRunStatus.Skipped, 0)),
        };

        var trend = RunHistoryAnalyzer.Analyze(runs, new TrendOptions()).Tests.Single();

        await Assert.That(trend.Name).IsEqualTo("Tests.t1");
        await Assert.That(trend.Runs).IsEqualTo(3);
        await Assert.That(trend.Failures).IsEqualTo(1);
        await Assert.That(trend.FlakyRuns).IsEqualTo(1);
        await Assert.That(trend.MedianDurationMs).IsEqualTo(20);
        await Assert.That(trend.FailureRate).IsEqualTo(1d / 3);
    }

    [Test]
    public async Task NewlyFailing_Includes_Tests_That_Failed_In_The_Latest_Run_After_Passing()
    {
        var runs = new[]
        {
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(1), ("stable-failure", TestRunStatus.Failed, 10), ("regressed", TestRunStatus.Passed, 10), ("recovered", TestRunStatus.Failed, 10)),
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(2), ("stable-failure", TestRunStatus.Failed, 10), ("regressed", TestRunStatus.Failed, 10), ("recovered", TestRunStatus.Passed, 10), ("brand-new", TestRunStatus.Failed, 10)),
        };

        var report = RunHistoryAnalyzer.Analyze(runs, new TrendOptions());

        await Assert.That(report.NewlyFailing().Select(t => t.Id)).IsEquivalentTo(["brand-new", "regressed"]);
    }

    [Test]
    public async Task NewlyFailing_Ignores_Tests_Missing_From_The_Latest_Run()
    {
        var runs = new[]
        {
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(1), ("removed", TestRunStatus.Failed, 10)),
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(2), ("other", TestRunStatus.Passed, 10)),
        };

        var report = RunHistoryAnalyzer.Analyze(runs, new TrendOptions());

        await Assert.That(report.NewlyFailing()).IsEmpty();
    }

    [Test]
    public async Task Slowest_And_MostFailing_Are_Ranked_And_Limited()
    {
        var runs = new[]
        {
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(1), ("fast", TestRunStatus.Failed, 1), ("medium", TestRunStatus.Passed, 50), ("slow", TestRunStatus.Failed, 900)),
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(2), ("fast", TestRunStatus.Failed, 1), ("medium", TestRunStatus.Passed, 50), ("slow", TestRunStatus.Passed, 900)),
        };

        var report = RunHistoryAnalyzer.Analyze(runs, new TrendOptions());

        await Assert.That(report.Slowest(2).Select(t => t.Id)).IsEquivalentTo(["slow", "medium"], CollectionOrdering.Matching);
        await Assert.That(report.MostFailing(5).Select(t => t.Id)).IsEquivalentTo(["fast", "slow"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Tests_With_The_Same_Id_In_Different_Suites_Are_Tracked_Separately()
    {
        var runs = new[]
        {
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(1), suite: "A", ("t1", TestRunStatus.Passed, 10)),
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(1), suite: "B", ("t1", TestRunStatus.Failed, 10)),
        };

        var report = RunHistoryAnalyzer.Analyze(runs, new TrendOptions());

        await Assert.That(report.Tests.Count).IsEqualTo(2);
    }
}

public class RunCountsTests
{
    [Test]
    [Arguments(10, 8, 2, 0, 0.8)]
    [Arguments(10, 5, 0, 5, 1.0)]
    public async Task PassRate_Excludes_Skipped_Tests(int total, int passed, int failed, int skipped, double expected)
    {
        var counts = new RunCounts(total, passed, failed, skipped, Cancelled: 0, Flaky: 0);

        await Assert.That(counts.PassRate).IsEqualTo(expected);
    }

    [Test]
    public async Task PassRate_Is_Null_When_Nothing_Ran()
    {
        await Assert.That(new RunCounts(3, 0, 0, 3, 0, 0).PassRate).IsNull();
    }
}

public class TrendsHtmlGeneratorTests
{
    [Test]
    public async Task Generate_Renders_A_Self_Contained_Page_With_Charts_And_Tables()
    {
        var runs = new[]
        {
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(1), ("t1", TestRunStatus.Passed, 10)),
            RunHistoryBuilder.Run(RunHistoryBuilder.Day(2), ("t1", TestRunStatus.Failed, 20)),
        };

        var html = TrendsHtmlGenerator.Generate(RunHistoryAnalyzer.Analyze(runs, new TrendOptions()));

        await Assert.That(html).StartsWith("<!DOCTYPE html>");
        await Assert.That(html).Contains("<svg");
        await Assert.That(html).Contains("Newly failing");
        await Assert.That(html).Contains("Slowest tests");
        await Assert.That(html).Contains("Most failing tests");
        await Assert.That(html).Contains("Tests.t1");
        await Assert.That(html).DoesNotContain("<script src=");
    }

    [Test]
    public async Task Generate_Encodes_Test_Names()
    {
        var runs = new[] { RunHistoryBuilder.Run(RunHistoryBuilder.Day(1), ("<b>evil</b>", TestRunStatus.Failed, 10)) };

        var html = TrendsHtmlGenerator.Generate(RunHistoryAnalyzer.Analyze(runs, new TrendOptions()));

        await Assert.That(html).DoesNotContain("<b>evil</b>");
        await Assert.That(html).Contains("&lt;b&gt;evil&lt;/b&gt;");
    }

    [Test]
    public async Task Generate_Explains_When_There_Is_No_History()
    {
        var html = TrendsHtmlGenerator.Generate(RunHistoryAnalyzer.Analyze([], new TrendOptions()));

        await Assert.That(html).Contains("No runs in history yet");
    }
}
