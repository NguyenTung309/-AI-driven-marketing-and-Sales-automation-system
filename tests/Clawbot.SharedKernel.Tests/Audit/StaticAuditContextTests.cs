using System.Net;
using Clawbot.SharedKernel.Audit;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Audit;

public sealed class StaticAuditContextTests
{
    [Fact]
    public void Constructor_SetsAllFields()
    {
        var userId = Guid.NewGuid();
        var ip = IPAddress.Parse("203.0.113.7");

        var context = new StaticAuditContext(userId, ip, "Mozilla/5.0");

        context.UserId.Should().Be(userId);
        context.IpAddress.Should().Be(ip);
        context.UserAgent.Should().Be("Mozilla/5.0");
    }

    [Fact]
    public void Defaults_AreAllNull()
    {
        // Job/gRPC scope không có HTTP context — audit chạy với ngữ cảnh rỗng, không được throw.
        var context = new StaticAuditContext();

        context.UserId.Should().BeNull();
        context.IpAddress.Should().BeNull();
        context.UserAgent.Should().BeNull();
    }

    [Fact]
    public void ImplementsIAuditContext()
    {
        var context = new StaticAuditContext(Guid.NewGuid());

        context.Should().BeAssignableTo<IAuditContext>();
        context.UserId.Should().NotBeNull();
    }
}
