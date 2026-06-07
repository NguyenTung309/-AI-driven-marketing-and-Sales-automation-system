namespace Clawbot.Agents.Core.Content;

public static class ContentRepurposeMapper
{
    public static IReadOnlyList<string> NormalizeTargets(IEnumerable<string> targetPlatforms)
    {
        ArgumentNullException.ThrowIfNull(targetPlatforms);

        var targets = targetPlatforms
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targets.Count == 0)
            throw new ArgumentException("target platforms required", nameof(targetPlatforms));

        return targets;
    }
}
