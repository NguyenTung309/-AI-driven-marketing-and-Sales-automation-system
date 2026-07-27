using System.Net;
using System.Net.Sockets;

namespace Clawbot.Infrastructure.Content.Publishing;

public interface IPublicUrlSafetyValidator
{
    Task<bool> IsSafeAsync(Uri uri, CancellationToken cancellationToken = default);
}

internal interface IHostAddressResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

internal sealed class SystemHostAddressResolver : IHostAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

internal sealed class DnsPublicUrlSafetyValidator : IPublicUrlSafetyValidator
{
    private static readonly TimeSpan DefaultResolutionTimeout = TimeSpan.FromSeconds(2);

    private readonly IHostAddressResolver _resolver;
    private readonly TimeSpan _resolutionTimeout;

    public DnsPublicUrlSafetyValidator(IHostAddressResolver resolver)
        : this(resolver, DefaultResolutionTimeout)
    {
    }

    internal DnsPublicUrlSafetyValidator(
        IHostAddressResolver resolver,
        TimeSpan resolutionTimeout)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(resolutionTimeout, TimeSpan.Zero);

        _resolver = resolver;
        _resolutionTimeout = resolutionTimeout;
    }

    public async Task<bool> IsSafeAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();

        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.');
        if (host.Length >= 2 && host[0] == '[' && host[^1] == ']')
            host = host[1..^1];
        if (host.Length == 0
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IPAddress.TryParse(host, out var literalAddress))
            return IsPublicAddress(literalAddress);

        if (!host.Contains('.') || Uri.CheckHostName(host) != UriHostNameType.Dns)
            return false;

        IPAddress[] addresses;
        using var resolutionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        resolutionCts.CancelAfter(_resolutionTimeout);
        try
        {
            var resolutionTask = _resolver.ResolveAsync(host, resolutionCts.Token);
            addresses = await resolutionTask
                .WaitAsync(_resolutionTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (TimeoutException)
        {
            resolutionCts.Cancel();
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        return addresses.Length > 0 && addresses.All(IsPublicAddress);
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address))
            return false;

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicIpv4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPublicIpv6(address),
            _ => false,
        };
    }

    private static bool IsPublicIpv4(byte[] bytes)
    {
        var first = bytes[0];
        var second = bytes[1];
        var third = bytes[2];

        return first is > 0 and < 224
            && first != 10
            && first != 127
            && !(first == 100 && second is >= 64 and <= 127)
            && !(first == 169 && second == 254)
            && !(first == 172 && second is >= 16 and <= 31)
            && !(first == 192 && second == 0 && third == 0)
            && !(first == 192 && second == 0 && third == 2)
            && !(first == 192 && second == 31 && third == 196)
            && !(first == 192 && second == 52 && third == 193)
            && !(first == 192 && second == 88 && third == 99)
            && !(first == 192 && second == 168)
            && !(first == 192 && second == 175 && third == 48)
            && !(first == 198 && second is 18 or 19)
            && !(first == 198 && second == 51 && third == 100)
            && !(first == 203 && second == 0 && third == 113);
    }

    private static bool IsPublicIpv6(IPAddress address)
    {
        if (address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        var isGlobalUnicast = (bytes[0] & 0xe0) == 0x20;
        if (!isGlobalUnicast)
            return false;

        return !HasPrefix(bytes, [0x20, 0x01, 0x00], 23)
            && !HasPrefix(bytes, [0x20, 0x01, 0x0d, 0xb8], 32)
            && !HasPrefix(bytes, [0x20, 0x02], 16)
            && !HasPrefix(bytes, [0x3f, 0xff, 0x00], 20);
    }

    private static bool HasPrefix(byte[] address, byte[] prefix, int prefixLength)
    {
        var fullBytes = prefixLength / 8;
        for (var index = 0; index < fullBytes; index++)
        {
            if (address[index] != prefix[index])
                return false;
        }

        var remainingBits = prefixLength % 8;
        if (remainingBits == 0)
            return true;

        var mask = (byte)(0xff << (8 - remainingBits));
        return (address[fullBytes] & mask) == (prefix[fullBytes] & mask);
    }
}
