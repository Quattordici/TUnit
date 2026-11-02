using System.Text;

namespace TUnit.Engine.Reporters.History;

/// <summary>
/// Renders a <see cref="TrendReport"/> as compact GitHub-flavored Markdown for a CI job
/// summary: the latest run against the previous one, plus the tests worth a look.
/// </summary>
internal static class TrendsMarkdownGenerator
{
    internal const int MaxRows = 10;

    private const string SparkBlocks = "▁▂▃▄▅▆▇█";

    internal static string Generate(TrendReport report)
    {
        var sb = new StringBuilder();
        sb.Append("### 📈 Test trends\n\n");

        if (report.Latest is not { } latest)
        {
            sb.Append("No runs in history yet. Append a run with `tunit-report history append`.\n");
            return sb.ToString();
        }

        var first = report.Runs[0];
        sb.Append(TrendFormat.Count(report.Runs.Count, "run", "runs"))
          .Append(" from ").Append(TrendFormat.Date(first.Timestamp))
          .Append(" to ").Append(TrendFormat.Date(latest.Timestamp));
        if (report.Options.Branch is { } branch)
        {
            sb.Append(" on `").Append(branch).Append('`');
        }
        sb.Append(".\n\n");

        AppendOverview(sb, report, latest);
        AppendTests(sb, "🆕 Newly failing", report.NewlyFailing(),
            static t => "failed in the latest run");
        AppendTests(sb, "🎲 Flaky", report.Flakiest(int.MaxValue),
            static t => FlakinessDetail(t));
        AppendTests(sb, "🐢 Slowing down", report.SlowingDown(int.MaxValue),
            static t => $"{TrendFormat.Duration(t.LastDurationMs!.Value)}, {TrendFormat.Ratio(t.Slowdown!.Value)} its usual {TrendFormat.Duration(t.BaselineDurationMs!.Value)}");

        return sb.ToString();
    }

    internal static string FlakinessDetail(TestTrend trend)
    {
        var parts = new List<string>(2);
        if (trend.Flips > 0)
        {
            parts.Add($"{TrendFormat.Count(trend.Flips, "flip", "flips")} between pass and fail");
        }
        if (trend.FlakyRuns > 0)
        {
            parts.Add($"passed after retry {TrendFormat.Count(trend.FlakyRuns, "time", "times")}");
        }
        return string.Join(", ", parts) + $" in {TrendFormat.Count(trend.Runs, "run", "runs")}";
    }

    /// <summary>
    /// Maps values onto block characters. Scales between <paramref name="min"/> and
    /// <paramref name="max"/> when given, otherwise between the values' own extremes;
    /// a flat series renders as a mid-height line.
    /// </summary>
    internal static string Sparkline(IReadOnlyList<double> values, double? min = null, double? max = null)
    {
        if (values.Count == 0)
        {
            return "";
        }

        var low = min ?? values.Min();
        var high = max ?? values.Max();
        var sb = new StringBuilder(values.Count);
        foreach (var value in values)
        {
            var index = high - low <= 0
                ? 3
                : (int)Math.Round((Math.Min(Math.Max(value, low), high) - low) / (high - low) * (SparkBlocks.Length - 1));
            sb.Append(SparkBlocks[index]);
        }
        return sb.ToString();
    }

    private static void AppendOverview(StringBuilder sb, TrendReport report, RunRecord latest)
    {
        var previous = report.Previous;
        var passRates = report.Runs.Where(static r => r.Counts.PassRate.HasValue).Select(static r => r.Counts.PassRate!.Value).ToList();
        var durations = report.Runs.Select(static r => r.DurationMs).ToList();

        sb.Append("| | Latest | vs previous | Trend |\n|---|---:|---:|---|\n");
        sb.Append("| Pass rate | ").Append(latest.Counts.PassRate is { } rate ? TrendFormat.Percent(rate) : "n/a")
          .Append(" | ").Append(report.PassRateChange is { } change ? TrendFormat.PointsChange(change) : "")
          .Append(" | ").Append(Sparkline(passRates, min: 0, max: 1)).Append(" |\n");
        sb.Append("| Duration | ").Append(TrendFormat.Duration(latest.DurationMs))
          .Append(" | ").Append(previous is null ? "" : TrendFormat.DurationChange(latest.DurationMs - previous.DurationMs))
          .Append(" | ").Append(Sparkline(durations)).Append(" |\n");
        sb.Append("| Tests | ").Append(latest.Counts.Total)
          .Append(" | ").Append(previous is null ? "" : TrendFormat.CountChange(latest.Counts.Total - previous.Counts.Total))
          .Append(" | |\n");
        sb.Append("| Failed | ").Append(latest.Counts.Failed)
          .Append(" | ").Append(previous is null ? "" : TrendFormat.CountChange(latest.Counts.Failed - previous.Counts.Failed))
          .Append(" | |\n\n");
    }

    private static void AppendTests(StringBuilder sb, string title, IReadOnlyList<TestTrend> tests, Func<TestTrend, string> detail)
    {
        if (tests.Count == 0)
        {
            return;
        }

        sb.Append("**").Append(title).Append(" (").Append(tests.Count).Append(")**\n\n")
          .Append("| Test | |\n|---|---|\n");
        foreach (var test in tests.Take(MaxRows))
        {
            sb.Append("| ").Append(Code(test.Name)).Append(" | ").Append(EscapeCell(detail(test))).Append(" |\n");
        }
        if (tests.Count > MaxRows)
        {
            sb.Append("| …and ").Append(tests.Count - MaxRows).Append(" more | |\n");
        }
        sb.Append('\n');
    }

    // A code span may contain backticks if its delimiter run is longer than any run inside;
    // GFM tables additionally need pipes escaped, even inside code spans.
    private static string Code(string text)
    {
        var escaped = EscapeCell(text);
        if (text.IndexOf('`') < 0)
        {
            return "`" + escaped + "`";
        }

        var padding = text.StartsWith("`", StringComparison.Ordinal) || text.EndsWith("`", StringComparison.Ordinal) ? " " : "";
        return "``" + padding + escaped + padding + "``";
    }

    private static string EscapeCell(string text) => text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
