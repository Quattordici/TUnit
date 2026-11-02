namespace TUnit.Reporting.Tool;

/// <summary>
/// The process-level dependencies every command uses, injected so commands can be
/// exercised in-process: console writers, environment variables and the clock.
/// </summary>
internal sealed record ToolContext(
    TextWriter Out,
    TextWriter Error,
    Func<string, string?> GetEnvironmentVariable,
    Func<DateTimeOffset> UtcNow)
{
    internal static ToolContext FromEnvironment()
        => new(Console.Out, Console.Error, Environment.GetEnvironmentVariable, static () => DateTimeOffset.UtcNow);
}
