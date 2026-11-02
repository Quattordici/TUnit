using System.Globalization;

namespace TUnit.Engine.Reporters.History;

/// <summary>Display formatting shared by the HTML and Markdown trend views.</summary>
internal static class TrendFormat
{
    internal static string Percent(double fraction)
        => (fraction * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    /// <summary>A pass-rate change in percentage points, always signed: "+2.5 pts", "−10 pts", "±0 pts".</summary>
    internal static string PointsChange(double fraction)
    {
        var points = Math.Round(fraction * 100, 1);
        return points switch
        {
            > 0 => "+" + points.ToString("0.#", CultureInfo.InvariantCulture) + " pts",
            < 0 => "−" + (-points).ToString("0.#", CultureInfo.InvariantCulture) + " pts",
            _ => "±0 pts",
        };
    }

    internal static string Duration(double ms) => ms switch
    {
        < 1000 => ms.ToString("0", CultureInfo.InvariantCulture) + " ms",
        < 60_000 => (ms / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " s",
        _ => (ms / 60_000).ToString("0.0", CultureInfo.InvariantCulture) + " min",
    };

    /// <summary>A signed duration change: "+1.2 s", "−300 ms".</summary>
    internal static string DurationChange(double ms) => ms switch
    {
        > 0 => "+" + Duration(ms),
        < 0 => "−" + Duration(-ms),
        _ => "±0 ms",
    };

    internal static string CountChange(int change) => change switch
    {
        > 0 => "+" + change.ToString(CultureInfo.InvariantCulture),
        < 0 => "−" + (-change).ToString(CultureInfo.InvariantCulture),
        _ => "±0",
    };

    internal static string Ratio(double ratio) => ratio.ToString("0.0", CultureInfo.InvariantCulture) + "×";

    internal static string Date(DateTimeOffset timestamp)
        => timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    internal static string ShortDate(DateTimeOffset timestamp)
        => timestamp.UtcDateTime.ToString("MMM d", CultureInfo.InvariantCulture);

    internal static string ShortSha(string sha) => sha.Length > 7 ? sha.Substring(0, 7) : sha;

    internal static string Count(int value, string singular, string plural)
        => value.ToString(CultureInfo.InvariantCulture) + " " + (value == 1 ? singular : plural);

    internal static string StatusName(TestResultPoint result) => result.Status switch
    {
        TestRunStatus.Passed when result.PassedAfterRetry => $"passed after {result.Attempts} attempts",
        TestRunStatus.Passed => "passed",
        TestRunStatus.Failed => "failed",
        TestRunStatus.Skipped => "skipped",
        _ => "cancelled",
    };
}
