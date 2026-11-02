using System.Globalization;
using System.Net;
using System.Text;

namespace TUnit.Engine.Reporters.History;

/// <summary>
/// Renders a <see cref="TrendReport"/> as one self-contained HTML page: inline CSS and
/// server-rendered SVG charts, no scripts, so it works offline and as a CI artifact.
/// </summary>
internal static class TrendsHtmlGenerator
{
    internal const int TableRows = 15;

    private const int ChartWidth = 720;
    private const int ChartHeight = 180;
    private const int ChartPadding = 28;

    internal static string Generate(TrendReport report)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
          .Append("<title>TUnit Test Trends</title>\n<style>").Append(Css).Append("</style>\n</head>\n<body>\n<main>\n")
          .Append("<h1>Test trends</h1>\n");

        if (report.Runs.Count == 0)
        {
            sb.Append("<p class=\"muted\">No runs in history yet. Append a run with <code>tunit-report history append</code>.</p>\n");
        }
        else
        {
            AppendOverview(sb, report);
            AppendCharts(sb, report.Runs);
            AppendTable(sb, "Newly failing", "Failed in the latest run after passing before.", report.NewlyFailing(),
                "Last result", static t => "failed");
            AppendTable(sb, "Most failing tests", "Ranked by failures across the analyzed runs.", report.MostFailing(TableRows),
                "Failures", static t => $"{t.Failures} of {t.Runs} ({Percent(t.FailureRate)})");
            AppendTable(sb, "Slowest tests", "Ranked by median duration.", report.Slowest(TableRows),
                "Median", static t => FormatDuration(t.MedianDurationMs));
        }

        sb.Append("</main>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static void AppendOverview(StringBuilder sb, TrendReport report)
    {
        var first = report.Runs[0];
        var latest = report.Runs[report.Runs.Count - 1];
        var flaky = report.Tests.Count(static t => t.FlakyRuns > 0);

        sb.Append("<p class=\"muted\">")
          .Append(report.Runs.Count.ToString(CultureInfo.InvariantCulture)).Append(report.Runs.Count == 1 ? " run" : " runs")
          .Append(" from ").Append(FormatDate(first.Timestamp)).Append(" to ").Append(FormatDate(latest.Timestamp)).Append("</p>\n")
          .Append("<section class=\"tiles\">\n");
        AppendTile(sb, "Latest pass rate", latest.Counts.PassRate is { } rate ? Percent(rate) : "n/a");
        AppendTile(sb, "Latest duration", FormatDuration(latest.DurationMs));
        AppendTile(sb, "Tests in latest run", latest.Counts.Total.ToString(CultureInfo.InvariantCulture));
        AppendTile(sb, "Tests that were flaky", flaky.ToString(CultureInfo.InvariantCulture));
        sb.Append("</section>\n");
    }

    private static void AppendTile(StringBuilder sb, string label, string value)
        => sb.Append("<div class=\"tile\"><div class=\"label\">").Append(Encode(label))
             .Append("</div><div class=\"value\">").Append(Encode(value)).Append("</div></div>\n");

    private static void AppendCharts(StringBuilder sb, IReadOnlyList<RunRecord> runs)
    {
        AppendChart(sb, "Pass rate", runs, static r => r.Counts.PassRate is { } rate ? rate * 100 : null, fixedMax: 100,
            static v => v.ToString("0", CultureInfo.InvariantCulture) + "%");
        AppendChart(sb, "Duration", runs, static r => r.DurationMs, fixedMax: null, FormatDuration);
    }

    private static void AppendChart(StringBuilder sb, string title, IReadOnlyList<RunRecord> runs,
        Func<RunRecord, double?> value, double? fixedMax, Func<double, string> format)
    {
        var points = new List<(double X, double Y, RunRecord Run, double Value)>();
        var max = fixedMax ?? Math.Max(1, runs.Select(value).Where(static v => v.HasValue).Select(static v => v!.Value).DefaultIfEmpty(1).Max());
        var plotWidth = ChartWidth - 2 * ChartPadding;
        var plotHeight = ChartHeight - 2 * ChartPadding;
        for (var i = 0; i < runs.Count; i++)
        {
            if (value(runs[i]) is not { } v)
            {
                continue;
            }

            var x = ChartPadding + (runs.Count == 1 ? plotWidth / 2.0 : plotWidth * i / (double)(runs.Count - 1));
            var y = ChartPadding + plotHeight - plotHeight * Math.Min(v, max) / max;
            points.Add((x, y, runs[i], v));
        }

        sb.Append("<section class=\"card\">\n<h2>").Append(Encode(title)).Append("</h2>\n")
          .Append("<svg viewBox=\"0 0 ").Append(ChartWidth).Append(' ').Append(ChartHeight)
          .Append("\" role=\"img\" aria-label=\"").Append(Encode(title)).Append(" per run\">\n")
          .Append("<line class=\"axis\" x1=\"").Append(ChartPadding).Append("\" y1=\"").Append(ChartHeight - ChartPadding)
          .Append("\" x2=\"").Append(ChartWidth - ChartPadding).Append("\" y2=\"").Append(ChartHeight - ChartPadding).Append("\"/>\n")
          .Append("<text class=\"tick\" x=\"").Append(ChartPadding).Append("\" y=\"").Append(ChartPadding - 8).Append("\">")
          .Append(Encode(format(max))).Append("</text>\n");

        if (points.Count > 1)
        {
            sb.Append("<polyline class=\"line\" points=\"");
            foreach (var p in points)
            {
                sb.Append(Coordinate(p.X)).Append(',').Append(Coordinate(p.Y)).Append(' ');
            }
            sb.Append("\"/>\n");
        }

        foreach (var p in points)
        {
            sb.Append("<circle class=\"dot\" cx=\"").Append(Coordinate(p.X)).Append("\" cy=\"").Append(Coordinate(p.Y)).Append("\" r=\"4\"><title>")
              .Append(Encode($"{FormatDate(p.Run.Timestamp)}{(p.Run.CommitSha is { } sha ? " · " + ShortSha(sha) : "")} · {format(p.Value)}"))
              .Append("</title></circle>\n");
        }

        sb.Append("</svg>\n</section>\n");
    }

    private static void AppendTable(StringBuilder sb, string title, string description, IReadOnlyList<TestTrend> tests,
        string valueHeader, Func<TestTrend, string> value)
    {
        sb.Append("<section class=\"card\">\n<h2>").Append(Encode(title)).Append("</h2>\n<p class=\"muted\">")
          .Append(Encode(description)).Append("</p>\n");

        if (tests.Count == 0)
        {
            sb.Append("<p class=\"muted\">None.</p>\n</section>\n");
            return;
        }

        sb.Append("<div class=\"scroll\"><table>\n<thead><tr><th>Test</th><th>Suite</th><th>")
          .Append(Encode(valueHeader)).Append("</th></tr></thead>\n<tbody>\n");
        foreach (var test in tests)
        {
            sb.Append("<tr><td class=\"name\">").Append(Encode(test.Name)).Append("</td><td>").Append(Encode(test.Suite))
              .Append("</td><td class=\"num\">").Append(Encode(value(test))).Append("</td></tr>\n");
        }
        sb.Append("</tbody>\n</table></div>\n</section>\n");
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Coordinate(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Percent(double fraction) => (fraction * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string FormatDate(DateTimeOffset timestamp)
        => timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string ShortSha(string sha) => sha.Length > 7 ? sha.Substring(0, 7) : sha;

    private static string FormatDuration(double ms) => ms switch
    {
        < 1000 => ms.ToString("0", CultureInfo.InvariantCulture) + " ms",
        < 60_000 => (ms / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " s",
        _ => (ms / 60_000).ToString("0.0", CultureInfo.InvariantCulture) + " min",
    };

    private const string Css =
        """
        :root { --bg:#f7f7f8; --card:#fff; --text:#1d1d1f; --muted:#6b6b76; --border:#e3e3e8; --accent:#3b6fd8; --axis:#c9c9d1; }
        @media (prefers-color-scheme: dark) { :root { --bg:#151518; --card:#1f1f24; --text:#ececf1; --muted:#9a9aa6; --border:#2e2e36; --accent:#7aa2f7; --axis:#44444f; } }
        * { box-sizing: border-box; }
        body { margin:0; background:var(--bg); color:var(--text); font:15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
        main { max-width: 960px; margin: 0 auto; padding: 24px 16px 48px; }
        h1 { margin: 0 0 4px; font-size: 26px; }
        h2 { margin: 0 0 8px; font-size: 17px; }
        .muted { color: var(--muted); margin: 0 0 12px; }
        .tiles { display:grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap:12px; margin: 16px 0; }
        .tile, .card { background:var(--card); border:1px solid var(--border); border-radius:10px; padding:14px 16px; }
        .card { margin: 16px 0; }
        .label { color: var(--muted); font-size: 13px; }
        .value { font-size: 22px; font-weight: 600; font-variant-numeric: tabular-nums; }
        svg { width:100%; height:auto; display:block; }
        .axis { stroke: var(--axis); stroke-width: 1; }
        .tick { fill: var(--muted); font-size: 12px; }
        .line { fill:none; stroke: var(--accent); stroke-width: 2; }
        .dot { fill: var(--accent); }
        .scroll { overflow-x:auto; }
        table { width:100%; border-collapse: collapse; font-size: 14px; }
        th, td { text-align:left; padding: 6px 8px; border-bottom: 1px solid var(--border); vertical-align: top; }
        th { color: var(--muted); font-weight: 500; }
        td.name { word-break: break-word; }
        td.num { white-space: nowrap; font-variant-numeric: tabular-nums; }
        code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 13px; }
        """;
}
