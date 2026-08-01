using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.Infrastructure.Persistence;
using Clawbot.Infrastructure.Security;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests;

public sealed class PancakePageTokenServiceSqlServerTests
{
    private const string ConnectionVariable = "CLAWBOT_SQLSERVER_TEST_CONNECTION";

    [SqlServerFact]
    public async Task MintAndStoreAsync_SerializesConcurrentMintingForCanonicalPage()
    {
        // Arrange
        var sourceConnection = Environment.GetEnvironmentVariable(ConnectionVariable)!;
        await using var fixture = await SqlServerFixture.CreateAsync(sourceConnection);
        await using var firstDb = fixture.CreateContext();
        await using var secondDb = fixture.CreateContext();
        var tenantId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
        var encryptor = CreateEncryptor();
        var gateway = new CoordinatedMintGateway();
        var firstService = CreateService(firstDb, encryptor, gateway, now);
        var secondService = CreateService(secondDb, encryptor, gateway, now);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Act
        var firstMint = firstService.MintAndStoreAsync(
            tenantId,
            "page-1",
            "Page one",
            "facebook",
            "user-token",
            timeout.Token);
        await gateway.FirstEntered.WaitAsync(timeout.Token);

        var secondMint = secondService.MintAndStoreAsync(
            tenantId,
            "page-1",
            "Page one",
            "facebook",
            "user-token",
            timeout.Token);
        var enteredBeforeRelease = await Task.WhenAny(
            gateway.SecondEntered,
            Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token));
        gateway.ReleaseFirst();
        var results = await Task.WhenAll(firstMint, secondMint);

        // Assert
        enteredBeforeRelease.Should().NotBe(gateway.SecondEntered);
        gateway.MaximumConcurrentCalls.Should().Be(1);
        gateway.CallCount.Should().Be(2);
        results.Select(result => result.PageAccessToken)
            .Should().Equal("page-token-1", "page-token-2");

        await using var assertionDb = fixture.CreateContext();
        var inboxes = await assertionDb.Inboxes
            .IgnoreQueryFilters()
            .Where(inbox => inbox.TenantId == tenantId
                && inbox.Platform == "facebook"
                && inbox.ExternalPageId == "page-1")
            .ToListAsync(timeout.Token);
        inboxes.Should().ContainSingle();
        inboxes[0].IsActive.Should().BeTrue();
        PancakeTokenCipher.DecryptOrRaw(encryptor, inboxes[0].EncryptedAccessToken!)
            .Should().Be("page-token-2");
    }

    private static PancakePageTokenService CreateService(
        AppDbContext db,
        AesEncryptor encryptor,
        IPageTokenMintGateway gateway,
        DateTimeOffset now)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        return new PancakePageTokenService(
            db,
            encryptor,
            Substitute.For<IPancakePageTokenResolver>(),
            gateway,
            clock,
            NullLogger<PancakePageTokenService>.Instance);
    }

    private static AesEncryptor CreateEncryptor() =>
        new(Options.Create(new EncryptionOptions
        {
            Base64Key = Convert.ToBase64String(
                Enumerable.Repeat((byte)0x52, 32).ToArray()),
        }));

    private sealed class CoordinatedMintGateway : IPageTokenMintGateway
    {
        private readonly TaskCompletionSource _firstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirst =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeCalls;
        private int _callCount;
        private int _maximumConcurrentCalls;

        public Task FirstEntered => _firstEntered.Task;
        public Task SecondEntered => _secondEntered.Task;
        public int CallCount => Volatile.Read(ref _callCount);
        public int MaximumConcurrentCalls => Volatile.Read(ref _maximumConcurrentCalls);

        public void ReleaseFirst() => _releaseFirst.TrySetResult();

        public async Task<string> MintAsync(
            string userAccessToken,
            string pageId,
            CancellationToken ct = default)
        {
            var callNumber = Interlocked.Increment(ref _callCount);
            var activeCalls = Interlocked.Increment(ref _activeCalls);
            UpdateMaximum(activeCalls);

            try
            {
                if (callNumber == 1)
                {
                    _firstEntered.TrySetResult();
                    await _releaseFirst.Task.WaitAsync(ct);
                }
                else
                {
                    _secondEntered.TrySetResult();
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
                return $"page-token-{callNumber}";
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        private void UpdateMaximum(int candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maximumConcurrentCalls);
                if (candidate <= current)
                    return;
                if (Interlocked.CompareExchange(
                        ref _maximumConcurrentCalls,
                        candidate,
                        current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class SqlServerFixture(
        string masterConnection,
        string databaseConnection,
        string databaseName) : IAsyncDisposable
    {
        public static async Task<SqlServerFixture> CreateAsync(string sourceConnection)
        {
            var databaseName = $"clawbot_token_lock_{Guid.NewGuid():N}";
            var masterBuilder = new SqlConnectionStringBuilder(sourceConnection)
            {
                InitialCatalog = "master",
                TrustServerCertificate = true,
            };
            var databaseBuilder = new SqlConnectionStringBuilder(masterBuilder.ConnectionString)
            {
                InitialCatalog = databaseName,
            };

            await using (var master = new SqlConnection(masterBuilder.ConnectionString))
            {
                await master.OpenAsync();
                await using var create = master.CreateCommand();
                create.CommandText = $"CREATE DATABASE [{databaseName}]";
                await create.ExecuteNonQueryAsync();
            }

            var fixture = new SqlServerFixture(
                masterBuilder.ConnectionString,
                databaseBuilder.ConnectionString,
                databaseName);
            try
            {
                await using var db = fixture.CreateContext();
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE dbo.inboxes (
                        id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                        tenant_id UNIQUEIDENTIFIER NOT NULL,
                        name NVARCHAR(200) NOT NULL,
                        platform NVARCHAR(32) NOT NULL,
                        external_page_id NVARCHAR(128) NOT NULL,
                        avatar_url NVARCHAR(2048) NULL,
                        encrypted_access_token NVARCHAR(MAX) NULL,
                        encrypted_refresh_token NVARCHAR(MAX) NULL,
                        encrypted_webhook_secret NVARCHAR(MAX) NULL,
                        token_expires_at DATETIMEOFFSET NULL,
                        page_token_minted_at DATETIMEOFFSET NULL,
                        sender_id NVARCHAR(255) NULL,
                        is_active BIT NOT NULL,
                        created_at DATETIMEOFFSET NOT NULL,
                        updated_at DATETIMEOFFSET NOT NULL,
                        deleted_at DATETIMEOFFSET NULL
                    );
                    CREATE UNIQUE INDEX UX_inboxes_tenant_platform_external_active
                        ON dbo.inboxes (tenant_id, platform, external_page_id)
                        WHERE is_active = 1 AND deleted_at IS NULL;
                    """);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public AppDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(databaseConnection)
                .Options;
            return new AppDbContext(options, new NullTenantAccessor());
        }

        public async ValueTask DisposeAsync()
        {
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection(masterConnection);
            await master.OpenAsync();
            await using var drop = master.CreateCommand();
            drop.CommandText = $"""
                IF DB_ID(N'{databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{databaseName}]
                        SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{databaseName}];
                END
                """;
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class SqlServerFactAttribute : FactAttribute
    {
        public SqlServerFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(ConnectionVariable)))
            {
                Skip = $"Set {ConnectionVariable} to run SQL Server integration tests.";
            }
        }
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;

        public TenantContext Require() =>
            throw new InvalidOperationException("No tenant in integration test scope.");
    }
}
