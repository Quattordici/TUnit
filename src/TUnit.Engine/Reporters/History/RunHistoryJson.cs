using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TUnit.Engine.Reporters.History;

/// <summary>
/// One-line JSON form of a <see cref="RunRecord"/>, so a history file is JSON Lines:
/// appendable, diffable, and a torn last line costs one run rather than the whole file.
/// Hand-written like <c>ReportDataJson</c> to stay Native-AOT compatible; unknown
/// properties are ignored on read for forward compatibility.
/// </summary>
internal static class RunHistoryJson
{
    /// <summary>Bump when the line shape changes incompatibly; readers skip newer lines.</summary>
    internal const int SchemaVersion = 1;

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false };

    internal static string Serialize(RunRecord run)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteNumber("v", SchemaVersion);
            w.WriteString("timestamp", run.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            w.WriteString("suite", run.Suite);
            WriteOptionalString(w, "commit", run.CommitSha);
            WriteOptionalString(w, "branch", run.Branch);
            WriteOptionalString(w, "pr", run.PullRequestNumber);
            WriteOptionalString(w, "commitUrl", run.CommitUrl);
            w.WriteNumber("durationMs", run.DurationMs);

            w.WriteStartObject("counts");
            w.WriteNumber("total", run.Counts.Total);
            w.WriteNumber("passed", run.Counts.Passed);
            w.WriteNumber("failed", run.Counts.Failed);
            w.WriteNumber("skipped", run.Counts.Skipped);
            w.WriteNumber("cancelled", run.Counts.Cancelled);
            w.WriteNumber("flaky", run.Counts.Flaky);
            w.WriteEndObject();

            w.WriteStartArray("tests");
            foreach (var test in run.Tests)
            {
                w.WriteStartObject();
                w.WriteString("id", test.Id);
                w.WriteString("name", test.Name);
                w.WriteString("status", StatusToString(test.Status));
                w.WriteNumber("durationMs", test.DurationMs);
                w.WriteNumber("attempts", test.Attempts);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Reads one history line. Returns <see langword="null"/> (never throws) for a malformed,
    /// foreign or newer-schema line, so one bad line never makes the whole history unreadable.
    /// </summary>
    internal static RunRecord? TryDeserialize(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            return Read(doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static RunRecord? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("v", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var schemaVersion)
            || schemaVersion > SchemaVersion
            || GetString(root, "suite") is not { } suite
            || !DateTimeOffset.TryParse(GetString(root, "timestamp"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
        {
            return null;
        }

        var counts = root.TryGetProperty("counts", out var c) && c.ValueKind == JsonValueKind.Object
            ? new RunCounts(GetInt(c, "total"), GetInt(c, "passed"), GetInt(c, "failed"), GetInt(c, "skipped"), GetInt(c, "cancelled"), GetInt(c, "flaky"))
            : new RunCounts(0, 0, 0, 0, 0, 0);

        var tests = new List<TestRunEntry>();
        if (root.TryGetProperty("tests", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in array.EnumerateArray())
            {
                tests.Add(new TestRunEntry(
                    GetString(t, "id") ?? "",
                    GetString(t, "name") ?? "",
                    StatusFromString(GetString(t, "status")),
                    GetDouble(t, "durationMs"),
                    Math.Max(1, GetInt(t, "attempts"))));
            }
        }

        return new RunRecord(
            timestamp,
            suite,
            GetString(root, "commit"),
            GetString(root, "branch"),
            GetString(root, "pr"),
            GetDouble(root, "durationMs"),
            counts,
            tests)
        {
            CommitUrl = GetString(root, "commitUrl"),
        };
    }

    private static string StatusToString(TestRunStatus status) => status switch
    {
        TestRunStatus.Passed => "passed",
        TestRunStatus.Failed => "failed",
        TestRunStatus.Skipped => "skipped",
        _ => "cancelled",
    };

    private static TestRunStatus StatusFromString(string? status) => status switch
    {
        "passed" => TestRunStatus.Passed,
        "failed" => TestRunStatus.Failed,
        "skipped" => TestRunStatus.Skipped,
        _ => TestRunStatus.Cancelled,
    };

    private static void WriteOptionalString(Utf8JsonWriter w, string name, string? value)
    {
        if (value is not null)
        {
            w.WriteString(name, value);
        }
    }

    // GetProperty-style access on a non-object (e.g. "tests":[7]) throws InvalidOperationException,
    // which TryDeserialize maps to "not a usable line".
    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int GetInt(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static double GetDouble(JsonElement e, string name)
        => e.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.Number
           && v.TryGetDouble(out var d)
           && !double.IsNaN(d) && !double.IsInfinity(d)
            ? d
            : 0;
}
