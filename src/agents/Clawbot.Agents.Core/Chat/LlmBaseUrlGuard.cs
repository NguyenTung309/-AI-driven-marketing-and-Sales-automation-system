using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Clawbot.Agents.Core.Chat;

public static class LlmBaseUrlGuard
{
    private static readonly ConcurrentDictionary<string, Lazy<HttpClient>> GuardedClients = new(StringComparer.OrdinalIgnoreCase);

    internal static Func<string, IPAddress[]> ResolveHostAddresses { get; set; } = Dns.GetHostAddresses;

    public static bool IsAllowedBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrWhiteSpace(uri.UserInfo)) return false;
        return !IsPrivateHost(uri);
    }

    public static HttpClient CreateGuardedHttpClient(Uri baseUri)
    {
        var origin = baseUri.GetLeftPart(UriPartial.Authority);
        return GuardedClients.GetOrAdd(origin, key => new Lazy<HttpClient>(() => CreateClient(new Uri(key, UriKind.Absolute)))).Value;
    }

    private static HttpClient CreateClient(Uri baseUri) =>
        new(CreateHandler()) { BaseAddress = baseUri };

    private static SocketsHttpHandler CreateHandler() =>
        new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = ResolvePublicAddresses(context.DnsEndPoint.Host);
                var port = context.DnsEndPoint.Port;
                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                    }
                }

                throw new SocketException((int)SocketError.HostUnreachable);
            },
        };

    private static bool IsPrivateHost(Uri uri)
    {
        var host = uri.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (IPAddress.TryParse(host, out var literalIp))
            return IsPrivateOrLocalAddress(literalIp);

        try
        {
            var addresses = ResolvePublicAddresses(host);
            return addresses.Length == 0;
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return true;
        }
    }

    private static IPAddress[] ResolvePublicAddresses(string host)
    {
        var addresses = ResolveHostAddresses(host);
        return addresses.Length == 0 || addresses.Any(IsPrivateOrLocalAddress)
            ? []
            : addresses;
    }

    private static bool IsPrivateOrLocalAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;
        if (ip.Equals(IPAddress.None) || ip.Equals(IPAddress.Broadcast)) return true;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;

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

        return (bytes[0] & 0xfe) == 0xfc;
    }
}
