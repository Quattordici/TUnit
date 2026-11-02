using TUnit.Engine.Reporters.Aggregation;
using TUnit.Engine.Reporters.Html;

namespace TUnit.Reporting.Tool.Tests;

/// <summary>Runs <c>tunit-report</c> in-process inside a scratch directory.</summary>
internal sealed class ToolHarness : IDisposable
{
    internal static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, string> _environment = new(StringComparer.Ordinal);

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "tunit-report-tests-" + Guid.NewGuid().ToString("N"));

    public string Store => Path.Combine(Root, "history");

    public ToolHarness() => Directory.CreateDirectory(Root);

    public void SetEnvironment(string name, string value) => _environment[name] = value;

    public ToolResult Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ToolContext(stdout, stderr, name => _environment.TryGetValue(name, out var value) ? value : null, static () => Now);

        var exitCode = Program.Run(args, context);

        return new ToolResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Writes a sidecar the way TUnit does and returns its path.</summary>
    public string WriteSidecar(string relativeDirectory, string assemblyName, string timestamp = "14 Jul 2026, 10:00:00 UTC",
        params (string Id, string Status, double DurationMs)[] tests)
    {
        var directory = Path.Combine(Root, relativeDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, assemblyName + ReportDataJson.SidecarExtension);
        File.WriteAllText(path, ReportDataJson.Serialize(Sidecar(assemblyName, timestamp, tests)));
        return path;
    }

    public string ReadHistory() => File.ReadAllText(Path.Combine(Store, "history.jsonl"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static ReportData Sidecar(string assemblyName, string timestamp, (string Id, string Status, double DurationMs)[] tests)
    {
        var results = tests.Select(t => new ReportTestResult
        {
            Id = t.Id,
            DisplayName = t.Id,
            MethodName = t.Id,
            ClassName = "SampleTests",
            Status = t.Status,
            DurationMs = t.DurationMs,
        }).ToArray();

        var summary = new ReportSummary
        {
            Total = results.Length,
            Passed = results.Count(r => r.Status == "passed"),
            Failed = results.Count(r => r.Status == "failed"),
            Skipped = results.Count(r => r.Status == "skipped"),
        };

        return new ReportData
        {
            AssemblyName = assemblyName,
            MachineName = "test-machine",
            Timestamp = timestamp,
            TUnitVersion = "1.0.0",
            OperatingSystem = "Linux",
            RuntimeVersion = ".NET 10.0.0",
            TotalDurationMs = results.Sum(r => r.DurationMs),
            CommitSha = "abc1234def",
            Branch = "main",
            Summary = summary,
            Groups =
            [
                new ReportTestGroup
                {
                    ClassName = "SampleTests",
                    Namespace = assemblyName,
                    Summary = summary,
                    Tests = results,
                },
            ],
        };
    }
}

internal sealed record ToolResult(int ExitCode, string Out, string Error);
