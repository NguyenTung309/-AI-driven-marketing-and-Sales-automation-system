using Clawbot.Api.Common.Pagination;
using FluentAssertions;

namespace Clawbot.Api.Tests.Pagination;

public sealed class PageRequestTests
{
    [Fact]
    public void Create_ClampsInvalidPageAndSize()
    {
        var a = PageRequest.Create(0, 0);
        a.Page.Should().Be(1);
        a.PageSize.Should().Be(PageRequest.DefaultPageSize);

        var b = PageRequest.Create(-5, 9999);
        b.Page.Should().Be(1);
        b.PageSize.Should().Be(PageRequest.DefaultPageSize);
    }

    [Fact]
    public void Create_KeepsValidValues()
    {
        var r = PageRequest.Create(3, 25);
        r.Page.Should().Be(3);
        r.PageSize.Should().Be(25);
        r.Skip.Should().Be(50);
    }

    [Fact]
    public void CreateClamped_UsesMathClamp()
    {
        var r = PageRequest.CreateClamped(2, 500, defaultPageSize: 30, maxPageSize: 100);
        r.Page.Should().Be(2);
        r.PageSize.Should().Be(100);
    }
}
