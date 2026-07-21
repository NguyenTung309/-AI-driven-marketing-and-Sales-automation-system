using System.Collections.Frozen;

namespace Clawbot.SharedKernel.Content;

public static class ContentPlatformCatalog
{
    private static readonly IReadOnlyList<string> WritablePlatforms =
        Array.AsReadOnly(["facebook", "zalo", "instagram"]);

    private static readonly FrozenSet<string> WritableSet =
        WritablePlatforms.ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlyList<string> Writable => WritablePlatforms;

    public static bool TryNormalizeWritable(string? platform, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(platform))
            return false;

        var candidate = platform.Trim().ToLowerInvariant();
        if (!WritableSet.Contains(candidate))
            return false;

        normalized = candidate;
        return true;
    }

    public static IReadOnlyList<string> NormalizeWritable(IEnumerable<string> platforms)
    {
        ArgumentNullException.ThrowIfNull(platforms);

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var platform in platforms)
        {
            if (!TryNormalizeWritable(platform, out var value))
                throw new ArgumentException($"unsupported platform '{platform}'", nameof(platforms));

            if (seen.Add(value!))
                normalized.Add(value!);
        }

        if (normalized.Count == 0)
            throw new ArgumentException("target platforms required", nameof(platforms));

        return normalized;
    }
}

// Compatibility name retained for the public Phase 1 contract while production code uses the catalog.
public static class ContentPlatform
{
    public static IReadOnlyList<string> Writable => ContentPlatformCatalog.Writable;

    public static bool TryNormalizeWritable(string? platform, out string? normalized) =>
        ContentPlatformCatalog.TryNormalizeWritable(platform, out normalized);
}
