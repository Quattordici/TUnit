using TUnit.Engine.Reporters.Aggregation;
using TUnit.Engine.Reporters.Html;

namespace TUnit.Reporting.Tool;

/// <summary>Finds and reads the effective report sidecars under a directory.</summary>
internal static class SidecarLoader
{
    internal static (List<ReportData> Suites, int Skipped) Load(string directory)
    {
        var suites = new List<ReportData>();
        var skipped = 0;
        // The same suite's sidecar can appear twice in one tree (the copy next to the HTML
        // report plus the copy in a TUNIT_AGGREGATE_DIR inside the scanned directory).
        // Both are byte-identical output of the same writer, so dedupe on a content hash —
        // digests, not whole file bodies, are what stays alive across the scan.
        var seenDigests = new HashSet<string>(StringComparer.Ordinal);
        using var sha = System.Security.Cryptography.SHA256.Create();
        // IgnoreInaccessible: an unreadable or concurrently deleted subdirectory must not
        // abort the walk — SearchOption.AllDirectories would throw mid-enumeration, before
        // the per-file guard below ever ran.
        var enumeration = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        var effectiveSidecars = ReportDataJson.SelectEffectiveSidecars(
            Directory.EnumerateFiles(directory, "*" + ReportDataJson.SidecarExtension, enumeration));
        foreach (var file in effectiveSidecars)
        {
            // This command is the designated final merge, so no publisher should still
            // be active. Skip any suite whose stable publication lock is still held.
            if (ReportDataJson.IsSidecarPublicationInProgress(file))
            {
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Locked, deleted mid-enumeration, or unreadable — count it with the other
                // skipped sidecars rather than failing the whole merge over one bad file.
                skipped++;
                continue;
            }

            if (ReportDataJson.IsSidecarExcluded(file, bytes))
            {
                continue;
            }

            if (!seenDigests.Add(Convert.ToBase64String(sha.ComputeHash(bytes))))
            {
                continue;
            }

            if (ReportDataJson.TryDeserialize(bytes) is { } data)
            {
                suites.Add(data);
            }
            else
            {
                skipped++;
            }
        }
        return (suites, skipped);
    }
}
