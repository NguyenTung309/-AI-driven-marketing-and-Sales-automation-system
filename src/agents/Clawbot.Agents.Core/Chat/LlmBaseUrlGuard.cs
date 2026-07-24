using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Clawbot.Agents.Core.Chat;

// Phase 2.14: outbound SSRF guard for LLM/provider/publisher base URLs.
// HTTPS (or operator-private HTTP), no redirects, UseProxy=false, validate every A/AAAA,
// reject mixed public/private answers and DNS rebinding. Private hosts require operator
// exact-origin allowlist or explicit allowPrivateHosts — never a tenant-controlled boolean.
public static class LlmBaseUrlGuard
{
    private static readonly ConcurrentDictionary<string, Lazy<HttpClient>> GuardedClients = new(StringComparer.OrdinalIgnoreCase);

    // Test seam — production uses DNS.
    internal static Func<string, IPAddress[]> ResolveHostAddresses { get; set; } = Dns.GetHostAddresses;

    public static bool IsAllowedBaseUrl(
        string baseUrl,
        bool allowPrivateHosts = false,
        IReadOnlyCollection<string>? allowedPrivateOrigins = null)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return false;
        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return false;

        var hostClass = ClassifyHost(uri);
        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            // Public cleartext is never allowed. Private HTTP only with operator grant.
            if (hostClass != HostClass.KnownPrivate)
                return false;
            return IsPrivateAccessGranted(uri, allowPrivateHosts, allowedPrivateOrigins);
        }

        return hostClass switch
        {
            HostClass.Public => true,
            HostClass.KnownPrivate => IsPrivateAccessGranted(uri, allowPrivateHosts, allowedPrivateOrigins),
            // Mixed / unresolved / empty DNS: fail closed unless operator explicitly grants.
            _ => IsPrivateAccessGranted(uri, allowPrivateHosts, allowedPrivateOrigins),
        };
    }

    public static HttpClient CreateGuardedHttpClient(
        Uri baseUri,
        bool allowPrivateHosts = false,
        int timeoutSeconds = 120,
        IReadOnlyCollection<string>? allowedPrivateOrigins = null)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!IsAllowedBaseUrl(baseUri.ToString(), allowPrivateHosts, allowedPrivateOrigins))
            throw new InvalidOperationException("Configured base URL is not allowed.");

        var origin = baseUri.GetLeftPart(UriPartial.Authority);
        var knownPrivate = ClassifyHost(baseUri) == HostClass.KnownPrivate;
        var allowDirectPrivate = knownPrivate
            && IsPrivateAccessGranted(baseUri, allowPrivateHosts, allowedPrivateOrigins);
        var cacheKey = $"{allowDirectPrivate}:{timeoutSeconds}:{origin}";
        return GuardedClients.GetOrAdd(
            cacheKey,
            _ => new Lazy<HttpClient>(() =>
                CreateClient(new Uri(origin, UriKind.Absolute), allowDirectPrivate, timeoutSeconds))).Value;
    }

    private enum HostClass
    {
        Public = 0,
        KnownPrivate = 1,
        UnresolvedOrMixed = 2,
    }

    private static HostClass ClassifyHost(Uri uri)
    {
        var host = uri.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return HostClass.KnownPrivate;
        }

        if (IPAddress.TryParse(host, out var literalIp))
            return IsPrivateOrLocalAddress(literalIp) ? HostClass.KnownPrivate : HostClass.Public;

        try
        {
            var addresses = ResolveHostAddresses(host);
            if (addresses.Length == 0)
                return HostClass.UnresolvedOrMixed;
            if (addresses.Any(IsPrivateOrLocalAddress))
            {
                // All-private → known private; mixed → unresolved/mixed (fail closed).
                return addresses.All(IsPrivateOrLocalAddress)
                    ? HostClass.KnownPrivate
                    : HostClass.UnresolvedOrMixed;
            }

            return HostClass.Public;
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return HostClass.UnresolvedOrMixed;
        }
    }

    private static bool IsPrivateAccessGranted(
        Uri uri,
        bool allowPrivateHosts,
        IReadOnlyCollection<string>? allowedPrivateOrigins)
    {
        if (allowPrivateHosts)
            return true;
        if (allowedPrivateOrigins is null || allowedPrivateOrigins.Count == 0)
            return false;

        var origin = uri.GetLeftPart(UriPartial.Authority);
        foreach (var allowed in allowedPrivateOrigins)
        {
            if (string.IsNullOrWhiteSpace(allowed))
                continue;
            if (string.Equals(origin, allowed.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                return true;
            if (Uri.TryCreate(allowed.Trim(), UriKind.Absolute, out var allowedUri)
                && string.Equals(
                    origin,
                    allowedUri.GetLeftPart(UriPartial.Authority),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static HttpClient CreateClient(Uri baseUri, bool allowDirectPrivate, int timeoutSeconds) =>
        new(CreateHandler(allowDirectPrivate), disposeHandler: true)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

    private static SocketsHttpHandler CreateHandler(bool allowDirectPrivate)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        if (allowDirectPrivate)
            return handler;

        handler.ConnectCallback = async (context, cancellationToken) =>
        {
            // Resolve at connect time (defeats simple DNS rebinding after allow-check).
            var addresses = ResolvePublicAddresses(context.DnsEndPoint.Host);
            if (addresses.Length == 0)
                throw new SocketException((int)SocketError.HostUnreachable);

            var port = context.DnsEndPoint.Port;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken)
                        .ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                }
            }

            throw new SocketException((int)SocketError.HostUnreachable);
        };
        return handler;
    }

    private static IPAddress[] ResolvePublicAddresses(string host)
    {
        IPAddress[] addresses;
        try
        {
            addresses = ResolveHostAddresses(host);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return [];
        }

        if (addresses.Length == 0 || addresses.Any(IsPrivateOrLocalAddress))
            return [];
        return addresses;
    }

    private static bool IsPrivateOrLocalAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip))
            return true;
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            return true;
        if (ip.Equals(IPAddress.None) || ip.Equals(IPAddress.Broadcast))
            return true;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
            return true;

        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] switch
            {
                0 => true,
                10 => true,
                100 when bytes[1] is >= 64 and <= 127 => true,
                127 => true,
                169 when bytes[1] == 254 => true,
                172 when bytes[1] is >= 16 and <= 31 => true,
                192 when bytes[1] == 168 => true,
                >= 224 and <= 239 => true,
                _ => false,
            };
        }

        // Unique local IPv6 (fc00::/7)
        return (bytes[0] & 0xfe) == 0xfc;
    }
}
