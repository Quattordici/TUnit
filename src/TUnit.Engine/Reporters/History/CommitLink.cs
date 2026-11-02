namespace TUnit.Engine.Reporters.History;

/// <summary>
/// Derives a commit page URL from the source-link templates the report already carries,
/// so the history needs no provider detection of its own.
/// </summary>
internal static class CommitLink
{
    // Blob segment of each provider's file link, and the matching commit segment.
    // GitLab's "/-/blob/" must be tried before GitHub's plain "/blob/".
    private static readonly (string Blob, string Commit)[] Providers =
    [
        ("/-/blob/", "/-/commit/"), // GitLab
        ("/blob/", "/commit/"),     // GitHub and GitHub Enterprise
        ("/src/", "/commits/"),     // Bitbucket
    ];

    internal static string? FromSourceLinks(SourceLinkTemplates? links, string? commitSha)
    {
        if (links is null || string.IsNullOrEmpty(commitSha))
        {
            return null;
        }

        foreach (var (blob, commit) in Providers)
        {
            var index = links.LineUrl.IndexOf(blob + commitSha + "/", StringComparison.Ordinal);
            if (index > 0)
            {
                return links.LineUrl.Substring(0, index) + commit + commitSha;
            }
        }

        return null;
    }
}
