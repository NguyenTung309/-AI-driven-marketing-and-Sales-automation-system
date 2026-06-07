using Clawbot.Agents.Core.Ads;

namespace Clawbot.Infrastructure.Ads;

public sealed class AdsConnectorResolver(
    IEnumerable<IAdsPlatformConnector> connectors) : IAdsConnectorResolver
{
    private readonly Dictionary<string, IAdsPlatformConnector> _byPlatform =
        connectors.ToDictionary(c => c.Platform, StringComparer.OrdinalIgnoreCase);

    public IAdsPlatformConnector? Resolve(string platform) =>
        _byPlatform.TryGetValue(platform, out var connector) ? connector : null;
}
