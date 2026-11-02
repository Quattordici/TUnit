using System.IO;
using System.Text;
using TUnit.Engine.Reporters.Aggregation;

namespace TUnit.Engine.Reporters.History;

/// <summary>Where run history is kept. The file store is the only implementation today.</summary>
internal interface IRunHistoryStore
{
    /// <summary>All readable runs, oldest first.</summary>
    IReadOnlyList<RunRecord> Load();

    /// <summary>
    /// Adds <paramref name="run"/> and trims the history to the newest <paramref name="keep"/> runs.
    /// Returns <see langword="false"/> when the same run is already stored.
    /// </summary>
    bool Append(RunRecord run, int keep);
}

/// <summary>
/// Run history as a JSON Lines file (<c>history.jsonl</c>) in a directory, e.g. one that CI
/// restores and saves with a cache step. Writes go through <see cref="AtomicFile"/> so a
/// killed append never truncates the existing history, and appends hold an exclusive lock
/// file so concurrent appends (parallel jobs on one machine) never drop each other's runs.
/// </summary>
internal sealed class FileRunHistoryStore : IRunHistoryStore
{
    internal const string FileName = "history.jsonl";

    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(25);

    public FileRunHistoryStore(string directory)
    {
        FilePath = Path.Combine(directory, FileName);
    }

    internal string FilePath { get; }

    public IReadOnlyList<RunRecord> Load()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        var runs = new List<RunRecord>();
        foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
        {
            if (RunHistoryJson.TryDeserialize(line) is { } run)
            {
                runs.Add(run);
            }
        }

        // Stable sort: runs with equal timestamps (e.g. per-suite records of one CI run)
        // keep their append order.
        return runs.OrderBy(static r => r.Timestamp).ToList();
    }

    public bool Append(RunRecord run, int keep)
    {
        if (keep < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(keep), keep, "Must keep at least one run.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        using var appendLock = AcquireLock();

        var runs = Load().ToList();
        var key = run.Key;
        if (runs.Any(r => r.Key == key))
        {
            return false;
        }

        runs.Add(run);
        var kept = runs.OrderBy(static r => r.Timestamp).Skip(Math.Max(0, runs.Count - keep)).ToList();

        var content = new StringBuilder();
        foreach (var r in kept)
        {
            content.Append(RunHistoryJson.Serialize(r)).Append('\n');
        }

        // GetBytes writes no byte-order mark, keeping every line plain JSON for line-based tools.
        AtomicFile.WriteAllBytes(FilePath, Encoding.UTF8.GetBytes(content.ToString()));
        return true;
    }

    // Read-modify-write must not interleave: FileShare.None is an exclusive lock across
    // processes (flock on Unix), and also between two opens within one process.
    private FileStream AcquireLock()
    {
        var lockPath = FilePath + ".lock";
        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(LockRetryDelay);
            }
        }
    }
}
