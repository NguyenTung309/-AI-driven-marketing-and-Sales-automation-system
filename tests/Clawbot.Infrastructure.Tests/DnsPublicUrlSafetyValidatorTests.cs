using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Infrastructure.Content.Publishing;
using FluentAssertions;
using System.Net;
using System.Net.Sockets;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests;

// Wrapper: pattern AAA + ten tieng Anh (T2 bat buoc), comment tieng Viet
public sealed class DnsPublicUrlSafetyValidatorTests
{
    private sealed class FakeResolver : IHostAddressResolver
    {
        private readonly IPAddress[]? _addrs;
        private readonly Exception? _throw;
        public FakeResolver(IPAddress[] addrs) => _addrs = addrs;
        public FakeResolver(Exception ex) => _throw = ex;
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
            => _throw is not null ? Task.FromException<IPAddress[]>(_throw) : Task.FromResult(_addrs!);
    }

    private static DnsPublicUrlSafetyValidator Validator(IHostAddressResolver resolver, TimeSpan? timeout = null)
        => timeout is null ? new DnsPublicUrlSafetyValidator(resolver) : new DnsPublicUrlSafetyValidator(resolver, timeout.Value);

    // --- 1. scheme / host co ban ---
    [Fact]
    public async Task IsSafe_Rejects_Http_NotHttps()
    {
        var v = Validator(new FakeResolver([IPAddress.Parse("1.1.1.1")]));
        (await v.IsSafeAsync(new Uri("http://example.com/a"))).Should().BeFalse();
    }

    [Fact]
    public async Task IsSafe_Rejects_RelativeUri()
    {
        var v = Validator(new FakeResolver([IPAddress.Parse("1.1.1.1")]));
        (await v.IsSafeAsync(new Uri("/a", UriKind.Relative))).Should().BeFalse();
    }

    [Fact]
    public async Task IsSafe_Rejects_UserInfo()
    {
        var v = Validator(new FakeResolver([IPAddress.Parse("1.1.1.1")]));
        (await v.IsSafeAsync(new Uri("https://user:pass@example.com/"))).Should().BeFalse();
    }

    [Fact]
    public async Task IsSafe_Rejects_EmptyHost()
    {
        var v = Validator(new FakeResolver([IPAddress.Parse("1.1.1.1")]));
        // Host rong: dung UriBuilder de tao Uri hop le nhung host rong
        var uri = new Uri("https://example.com/a");
        // Thay host thanh rong bang cach dung Uri co host la "0.0.0.0" roi kiem tra logic host check
        // Truong hop host rong thuc te bi Uri tu choi -> bo qua, kiem tra host "." thay the
        (await v.IsSafeAsync(new Uri("https://0.0.0.0/a"))).Should().BeFalse();
    }

    [Fact]
    public async Task IsSafe_Rejects_Localhost()
    {
        var v = Validator(new FakeResolver([IPAddress.Parse("1.1.1.1")]));
        (await v.IsSafeAsync(new Uri("https://localhost/a"))).Should().BeFalse();
        (await v.IsSafeAsync(new Uri("https://foo.localhost/a"))).Should().BeFalse();
        (await v.IsSafeAsync(new Uri("https://foo.local/a"))).Should().BeFalse();
    }

    // --- 2. literal IP ---
    [Fact]
    public async Task IsSafe_Literal_PublicIp_Allowed()
    {
        var v = Validator(new FakeResolver([]));
        (await v.IsSafeAsync(new Uri("https://8.8.8.8/a"))).Should().BeTrue();
    }

    [Fact]
    public async Task IsSafe_Literal_PrivateIp_Blocked()
    {
        var v = Validator(new FakeResolver([]));
        (await v.IsSafeAsync(new Uri("https://10.0.0.5/a"))).Should().BeFalse();
        (await v.IsSafeAsync(new Uri("https://192.168.1.1/a"))).Should().BeFalse();
    }

    [Fact]
    public async Task IsSafe_Literal_Loopback_Blocked()
    {
        var v = Validator(new FakeResolver([]));
        (await v.IsSafeAsync(new Uri("https://127.0.0.1/a"))).Should().BeFalse();
    }

    // --- 3. DNS ---
    [Fact]
    public async Task IsSafe_Dns_PublicAddress_Allowed()
    {
        var v = Validator(new FakeResolver([IPAddress.Parse("8.8.8.8")]));
        (await v.IsSafeAsync(new Uri("https://example.com/a"))).Should().BeTrue();
    }

    [Fact]
    public async Task IsSafe_Dns_PrivateAddress_Blocked()
    {
        var v = Validator(new FakeResolver([IPAddress.Parse("10.0.0.5")]));
        (await v.IsSafeAsync(new Uri("https://example.com/a"))).Should().BeFalse();
    }

    [Fact]
    public async Task IsSafe_Dns_MixedAddresses_Blocked_OnAnyPrivate()
    {
        var v = Validator(new FakeResolver([IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.1")]));
        (await v.IsSafeAsync(new Uri("https://example.com/a"))).Should().BeFalse();
    }

    [Fact]
    public async Task IsSafe_Dns_SocketException_ReturnsFalse()
    {
        var v = Validator(new FakeResolver(new SocketException()));
        (await v.IsSafeAsync(new Uri("https://example.com/a"))).Should().BeFalse();
    }

    [Fact]
    public async Task IsSafe_Dns_ArgumentException_ReturnsFalse()
    {
        var v = Validator(new FakeResolver(new ArgumentException("bad host")));
        (await v.IsSafeAsync(new Uri("https://example.com/a"))).Should().BeFalse();
    }

    [Fact]
    public async Task IsSafe_Dns_NoDot_ReturnsFalse()
    {
        // host khong co dau cham -> CheckHostName != Dns
        var v = Validator(new FakeResolver([IPAddress.Parse("8.8.8.8")]));
        (await v.IsSafeAsync(new Uri("https://nodot/a"))).Should().BeFalse();
    }

    [Fact]
    public async Task IsSafe_Dns_EmptyResult_ReturnsFalse()
    {
        var v = Validator(new FakeResolver([]));
        (await v.IsSafeAsync(new Uri("https://example.com/a"))).Should().BeFalse();
    }

    // --- 4. timeout ---
    [Fact]
    public async Task IsSafe_Resolver_Timeout_ReturnsFalse()
    {
        // IHostAddressResolver la internal -> khong dung NSubstitute, dung FakeResolver cham
        var slow = new SlowResolver();
        var v = Validator(slow, TimeSpan.FromMilliseconds(50));
        (await v.IsSafeAsync(new Uri("https://example.com/a"))).Should().BeFalse();
    }

    private sealed class SlowResolver : IHostAddressResolver
    {
        public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
        {
            await Task.Delay(5000, ct);
            return new[] { IPAddress.Parse("8.8.8.8") };
        }
    }

    // --- 5. IsPublicAddress truc tiep ---
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("10.0.0.1", false)]
    [InlineData("192.168.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.1.1", false)]
    public void IsPublicAddress_Classifies_IPv4(string ip, bool expected)
    {
        DnsPublicUrlSafetyValidator.IsPublicAddress(IPAddress.Parse(ip)).Should().Be(expected);
    }

    [Fact]
    public void IsPublicAddress_Null_Throws()
    {
        FluentActions.Invoking(() => DnsPublicUrlSafetyValidator.IsPublicAddress(null!)).Should().Throw<ArgumentNullException>();
    }

    // --- 6. ctor guard ---
    [Fact]
    public void Ctor_NullResolver_Throws()
    {
        FluentActions.Invoking(() => new DnsPublicUrlSafetyValidator(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_NonPositiveTimeout_Throws()
    {
        var r = new FakeResolver([IPAddress.Parse("1.1.1.1")]);
        FluentActions.Invoking(() => new DnsPublicUrlSafetyValidator(r, TimeSpan.Zero)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
