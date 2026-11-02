using TUnit.Assertions.Enums;
using TUnit.Engine.Reporters;
using TUnit.Engine.Reporters.History;
using static TUnit.Engine.Reporters.History.TestRunStatus;
using static TUnit.UnitTests.RunHistoryBuilder;

namespace TUnit.UnitTests;

public class TestTrendInsightsTests
{
    [Test]
    public async Task Flips_Count_Status_Changes_Between_Executed_Runs_Ignoring_Skips()
    {
        var runs = Sequence("t1", Passed, Failed, Skipped, Failed, Passed, Failed);

        var trend = RunHistoryAnalyzer.Analyze(runs, new TrendOptions()).Tests.Single();

        await Assert.That(trend.Flips).IsEqualTo(3);
    }

    [Test]
    public async Task Blips_Count_One_Off_Results_Between_Opposite_Outcomes()
    {
        var runs = Sequence("t1", Passed, Failed, Passed, Passed, Failed, Failed, Failed, Passed, Failed, Passed);

        var trend = RunHistoryAnalyzer.Analyze(runs, new TrendOptions()).Tests.Single();

        await Assert.That(trend.Blips).IsEqualTo(3);
    }

    [Test]
    public async Task A_Regression_Followed_By_A_Fix_Is_Not_Flaky()
    {
        var runs = Sequence("t1", Passed, Passed, Failed, Failed, Failed, Passed, Passed);

        var trend = RunHistoryAnalyzer.Analyze(runs, new TrendOptions()).Tests.Single();

        await Assert.That(trend.Flips).IsEqualTo(2);
        await Assert.That(trend.IsFlaky).IsFalse();
    }

    [Test]
    public async Task RecentResults_Are_Chronological_And_Capped()
    {
        var statuses = Enumerable.Range(0, TestTrend.RecentResultCount + 5)
            .Select(i => i % 2 == 0 ? Passed : Failed)
            .ToArray();

        var trend = RunHistoryAnalyzer.Analyze(Sequence("t1", statuses), new TrendOptions(Last: 100)).Tests.Single();

        await Assert.That(trend.RecentResults.Count).IsEqualTo(TestTrend.RecentResultCount);
        await Assert.That(trend.RecentResults.Select(r => r.Status))
            .IsEquivalentTo(statuses.Skip(5), CollectionOrdering.Matching);
    }

    [Test]
    public async Task RecentResults_Remember_Duration_And_Retries()
    {
        var runs = new[]
        {
            WithEntries(Day(1), Entry("t1", Passed, durationMs: 12, attempts: 3)),
        };

        var result = RunHistoryAnalyzer.Analyze(runs, new TrendOptions()).Tests.Single().RecentResults.Single();

        await Assert.That(result.Timestamp).IsEqualTo(Day(1));
        await Assert.That(result.DurationMs).IsEqualTo(12);
        await Assert.That(result.Attempts).IsEqualTo(3);
    }

    [Test]
    public async Task LastDuration_And_Slowdown_Compare_The_Latest_Run_With_Earlier_Runs()
    {
        var runs = new[]
        {
            WithEntries(Day(1), Entry("t1", Passed, 100)),
            WithEntries(Day(2), Entry("t1", Passed, 120)),
            WithEntries(Day(3), Entry("t1", Passed, 80)),
            WithEntries(Day(4), Entry("t1", Passed, 300)),
        };

        var trend = RunHistoryAnalyzer.Analyze(runs, new TrendOptions()).Tests.Single();

        await Assert.That(trend.LastDurationMs).IsEqualTo(300);
        await Assert.That(trend.Slowdown).IsEqualTo(3.0);
    }

    [Test]
    public async Task Slowdown_Needs_Enough_Earlier_Runs()
    {
        var runs = new[]
        {
            WithEntries(Day(1), Entry("t1", Passed, 100)),
            WithEntries(Day(2), Entry("t1", Passed, 900)),
        };

        var trend = RunHistoryAnalyzer.Analyze(runs, new TrendOptions()).Tests.Single();

        await Assert.That(trend.Slowdown).IsNull();
    }
}

public class TrendReportInsightsTests
{
    [Test]
    public async Task Flakiest_Includes_Retry_Passes_And_Repeated_Flips_But_Not_Steady_Or_Single_Changes()
    {
        var runs = new[]
        {
            WithEntries(Day(1), Entry("flipping", Passed), Entry("retried", Passed, attempts: 2), Entry("regressed", Passed), Entry("broken", Failed), Entry("steady", Passed)),
            WithEntries(Day(2), Entry("flipping", Failed), Entry("retried", Passed), Entry("regressed", Failed), Entry("broken", Failed), Entry("steady", Passed)),
            WithEntries(Day(3), Entry("flipping", Passed), Entry("retried", Passed), Entry("regressed", Failed), Entry("broken", Failed), Entry("steady", Passed)),
            WithEntries(Day(4), Entry("flipping", Failed), Entry("retried", Passed, attempts: 3), Entry("regressed", Failed), Entry("broken", Failed), Entry("steady", Passed)),
        };

        var flakiest = RunHistoryAnalyzer.Analyze(runs, new TrendOptions()).Flakiest(10);

        await Assert.That(flakiest.Select(t => t.Id)).IsEquivalentTo(["flipping", "retried"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task SlowingDown_Lists_Tests_Clearly_Slower_Than_Their_Median()
    {
        var runs = new[]
        {
            WithEntries(Day(1), Entry("slower", Passed, 100), Entry("noise", Passed, 2), Entry("steady", Passed, 500)),
            WithEntries(Day(2), Entry("slower", Passed, 100), Entry("noise", Passed, 2), Entry("steady", Passed, 500)),
            WithEntries(Day(3), Entry("slower", Passed, 100), Entry("noise", Passed, 2), Entry("steady", Passed, 500)),
            WithEntries(Day(4), Entry("slower", Passed, 400), Entry("noise", Passed, 9), Entry("steady", Passed, 550)),
        };

        var slowing = RunHistoryAnalyzer.Analyze(runs, new TrendOptions()).SlowingDown(10);

        await Assert.That(slowing.Select(t => t.Id)).IsEquivalentTo(["slower"]);
    }

    [Test]
    public async Task SlowingDown_Ignores_Tests_Missing_From_The_Latest_Run()
    {
        var runs = new[]
        {
            WithEntries(Day(1), Entry("gone", Passed, 100), Entry("other", Passed)),
            WithEntries(Day(2), Entry("gone", Passed, 100), Entry("other", Passed)),
            WithEntries(Day(3), Entry("gone", Passed, 100), Entry("other", Passed)),
            WithEntries(Day(4), Entry("gone", Passed, 900), Entry("other", Passed)),
            WithEntries(Day(5), Entry("other", Passed)),
        };

        await Assert.That(RunHistoryAnalyzer.Analyze(runs, new TrendOptions()).SlowingDown(10)).IsEmpty();
    }

    [Test]
    public async Task Latest_And_Previous_Runs_Expose_Deltas()
    {
        var runs = new[]
        {
            WithEntries(Day(1), Entry("a", Passed), Entry("b", Failed)),
            WithEntries(Day(2), Entry("a", Passed), Entry("b", Passed)),
        };

        var report = RunHistoryAnalyzer.Analyze(runs, new TrendOptions());

        await Assert.That(report.Latest!.Timestamp).IsEqualTo(Day(2));
        await Assert.That(report.Previous!.Timestamp).IsEqualTo(Day(1));
        await Assert.That(report.PassRateChange).IsEqualTo(0.5);
    }

    [Test]
    public async Task Latest_And_Previous_Are_Null_Without_Enough_Runs()
    {
        var report = RunHistoryAnalyzer.Analyze([WithEntries(Day(1), Entry("a", Passed))], new TrendOptions());

        await Assert.That(report.Latest).IsNotNull();
        await Assert.That(report.Previous).IsNull();
        await Assert.That(report.PassRateChange).IsNull();
    }
}

public class CommitLinkTests
{
    [Test]
    [Arguments("https://github.com/acme/repo/blob/abc123/{path}#L{line}", "https://github.com/acme/repo/commit/abc123")]
    [Arguments("https://ghe.example.com/acme/repo/blob/abc123/{path}#L{line}", "https://ghe.example.com/acme/repo/commit/abc123")]
    [Arguments("https://gitlab.com/acme/repo/-/blob/abc123/{path}#L{line}", "https://gitlab.com/acme/repo/-/commit/abc123")]
    [Arguments("https://bitbucket.org/acme/repo/src/abc123/{path}#lines-{line}", "https://bitbucket.org/acme/repo/commits/abc123")]
    public async Task FromSourceLinks_Derives_The_Commit_Page(string lineUrl, string expected)
    {
        var links = new SourceLinkTemplates(lineUrl, lineUrl, null);

        await Assert.That(CommitLink.FromSourceLinks(links, "abc123")).IsEqualTo(expected);
    }

    [Test]
    public async Task FromSourceLinks_Returns_Null_When_It_Cannot_Tell()
    {
        await Assert.That(CommitLink.FromSourceLinks(null, "abc123")).IsNull();
        await Assert.That(CommitLink.FromSourceLinks(new SourceLinkTemplates("https://example.com/{path}", "https://example.com/{path}", null), "abc123")).IsNull();
        await Assert.That(CommitLink.FromSourceLinks(new SourceLinkTemplates("https://github.com/a/b/blob/abc123/{path}", "x", null), null)).IsNull();
    }

    [Test]
    public async Task FromReport_Records_The_Commit_Link()
    {
        var record = RunRecord.FromReport(TestReportData.Build("My.Tests"), DateTimeOffset.MinValue);

        await Assert.That(record.CommitUrl).IsEqualTo("https://github.com/acme/repo/commit/abc123");
    }

    [Test]
    public async Task Commit_Link_Survives_The_History_File()
    {
        var record = RunRecord.FromReport(TestReportData.Build("My.Tests"), DateTimeOffset.MinValue);

        var restored = RunHistoryJson.TryDeserialize(RunHistoryJson.Serialize(record));

        await Assert.That(restored!.CommitUrl).IsEqualTo(record.CommitUrl);
    }
}

public class FileRunHistoryStoreConcurrencyTests
{
    [Test]
    public async Task Concurrent_Appends_Keep_Every_Run()
    {
        using var dir = new TempDirectory();
        var runs = Enumerable.Range(1, 12).Select(day => WithEntries(Day(day), Entry("t1", Passed))).ToArray();

        await Task.WhenAll(runs.Select(run => Task.Run(() => new FileRunHistoryStore(dir.Path).Append(run, keep: 100))));

        await Assert.That(new FileRunHistoryStore(dir.Path).Load().Count).IsEqualTo(12);
    }
}

public class TrendsMarkdownGeneratorTests
{
    [Test]
    public async Task Generate_Summarizes_The_Latest_Run_And_Highlights()
    {
        var runs = new[]
        {
            WithEntries(Day(1), Entry("ok", Passed), Entry("regressed", Passed), Entry("flipping", Failed)),
            WithEntries(Day(2), Entry("ok", Passed), Entry("regressed", Passed), Entry("flipping", Passed)),
            WithEntries(Day(3), Entry("ok", Passed), Entry("regressed", Failed), Entry("flipping", Failed)),
        };

        var markdown = TrendsMarkdownGenerator.Generate(RunHistoryAnalyzer.Analyze(runs, new TrendOptions()));

        await Assert.That(markdown).StartsWith("### ");
        await Assert.That(markdown).Contains("3 runs");
        await Assert.That(markdown).Contains("Pass rate");
        await Assert.That(markdown).Contains("Newly failing");
        await Assert.That(markdown).Contains("`Tests.regressed`");
        await Assert.That(markdown).Contains("Flaky");
        await Assert.That(markdown).Contains("`Tests.flipping`");
    }

    [Test]
    public async Task Sparkline_Scales_Values_To_Block_Characters()
    {
        await Assert.That(TrendsMarkdownGenerator.Sparkline([0, 0.5, 1])).IsEqualTo("▁▅█");
        await Assert.That(TrendsMarkdownGenerator.Sparkline([0.9, 0.9], min: 0, max: 1)).IsEqualTo("▇▇");
        await Assert.That(TrendsMarkdownGenerator.Sparkline([5, 5])).IsEqualTo("▄▄");
        await Assert.That(TrendsMarkdownGenerator.Sparkline([])).IsEqualTo("");
    }

    [Test]
    public async Task Generate_Keeps_Test_Names_From_Breaking_Markdown()
    {
        var runs = new[] { WithEntries(Day(1), Entry("a|b`c", Failed)) };

        var markdown = TrendsMarkdownGenerator.Generate(RunHistoryAnalyzer.Analyze(runs, new TrendOptions()));

        await Assert.That(markdown).Contains("``Tests.a\\|b`c``");
    }

    [Test]
    public async Task Generate_Explains_When_There_Is_No_History()
    {
        var markdown = TrendsMarkdownGenerator.Generate(RunHistoryAnalyzer.Analyze([], new TrendOptions()));

        await Assert.That(markdown).Contains("No runs in history yet");
    }
}

public class TrendsHtmlInsightsTests
{
    [Test]
    public async Task Generate_Shows_Result_Dots_Flaky_And_Slowing_Tables_And_Recent_Runs()
    {
        var runs = Sequence("t1", Passed, Failed, Passed, Failed);

        var html = TrendsHtmlGenerator.Generate(RunHistoryAnalyzer.Analyze(runs, new TrendOptions()));

        await Assert.That(html).Contains("Flaky tests");
        await Assert.That(html).Contains("Slowing down");
        await Assert.That(html).Contains("Recent runs");
        await Assert.That(html).Contains("class=\"r fail\"");
        await Assert.That(html).Contains("class=\"r pass\"");
    }

    [Test]
    public async Task Generate_Links_Commits_When_The_Url_Is_Known()
    {
        var run = WithEntries(Day(1), Entry("t1", Passed)) with { CommitUrl = "https://github.com/acme/repo/commit/abc123?x=1&y=2" };

        var html = TrendsHtmlGenerator.Generate(RunHistoryAnalyzer.Analyze([run], new TrendOptions()));

        await Assert.That(html).Contains("href=\"https://github.com/acme/repo/commit/abc123?x=1&amp;y=2\"");
    }

    [Test]
    public async Task Generate_Does_Not_Link_Non_Http_Commit_Urls()
    {
        var run = WithEntries(Day(1), Entry("t1", Passed)) with { CommitUrl = "javascript:alert(1)" };

        var html = TrendsHtmlGenerator.Generate(RunHistoryAnalyzer.Analyze([run], new TrendOptions()));

        await Assert.That(html).DoesNotContain("javascript:");
    }

    [Test]
    public async Task Generate_Mentions_The_Branch_Filter()
    {
        var run = WithEntries(Day(1), Entry("t1", Passed));

        var html = TrendsHtmlGenerator.Generate(RunHistoryAnalyzer.Analyze([run], new TrendOptions(Branch: "main")));

        await Assert.That(html).Contains("on <strong>main</strong>");
    }
}
