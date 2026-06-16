using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.SharedKernel.Security;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Channels;

// M06/M13 — PancakeConfigResolver tenant-DB → appsettings → defaults cascade.
public sealed class PancakeConfigResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);

    private static IConfiguration Config(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public async Task Returns_decrypted_db_row_when_present()
    {
        using var fx = new TestAppDb();
        var row = PancakeConfig.Create(fx.TenantId, Now);
        row.UpdateAccessToken("CIPHER", Now);
        fx.Db.PancakeConfigs.Add(row);
        await fx.Db.SaveChangesAsync();

        var enc = Substitute.For<IEncryptor>();
        enc.Decrypt("CIPHER").Returns("PLAIN");

        var sut = new PancakeConfigResolver(fx.Db, enc, Config(new Dictionary<string, string?>()), NullLogger<PancakeConfigResolver>.Instance);
        var result = await sut.ResolveAsync(fx.TenantId);

        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("PLAIN");
        result.BaseUrl.Should().Be(row.BaseUrl);
    }

    [Fact]
    public async Task Falls_back_to_appsettings_when_no_db_row()
    {
        using var fx = new TestAppDb();
        var enc = Substitute.For<IEncryptor>();
        var sut = new PancakeConfigResolver(fx.Db, enc, Config(new Dictionary<string, string?>
        {
            ["Channels:Pancake:BaseUrl"] = "https://custom.example",
            ["Channels:Pancake:AuthMode"] = "BEARER",
        }), NullLogger<PancakeConfigResolver>.Instance);

        var result = await sut.ResolveAsync(Guid.NewGuid());

        result.Should().NotBeNull();
        result!.BaseUrl.Should().Be("https://custom.example");
        result.AuthMode.Should().Be("bearer"); // normalized to lowercase
    }

    [Fact]
    public async Task Empty_tenant_skips_db_and_uses_appsettings()
    {
        using var fx = new TestAppDb();
        var enc = Substitute.For<IEncryptor>();
        var sut = new PancakeConfigResolver(fx.Db, enc, Config(new Dictionary<string, string?>
        {
            ["Channels:Pancake:BaseUrl"] = "https://fallback",
        }), NullLogger<PancakeConfigResolver>.Instance);

        var result = await sut.ResolveAsync(Guid.Empty);

        result.Should().NotBeNull();
        result!.BaseUrl.Should().Be("https://fallback");
    }

    [Fact]
    public async Task Returns_null_when_no_db_row_and_no_section()
    {
        using var fx = new TestAppDb();
        var enc = Substitute.For<IEncryptor>();
        var sut = new PancakeConfigResolver(fx.Db, enc, Config(new Dictionary<string, string?>()), NullLogger<PancakeConfigResolver>.Instance);

        var result = await sut.ResolveAsync(Guid.NewGuid());

        result.Should().BeNull();
    }
}
