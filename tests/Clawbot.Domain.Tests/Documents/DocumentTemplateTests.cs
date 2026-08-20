using Clawbot.Domain.Documents;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Documents;

public sealed class DocumentTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var tpl = DocumentTemplate.Create(TenantId, "quote-v1", "quote", "<h1>Quote</h1>", Now, "[{\"name\":\"client\"}]");

        tpl.TenantId.Should().Be(TenantId);
        tpl.Code.Should().Be("quote-v1");
        tpl.DocType.Should().Be("quote");
        tpl.TemplateHtml.Should().Be("<h1>Quote</h1>");
        tpl.FieldsJson.Should().Be("[{\"name\":\"client\"}]");
        tpl.CreatedAt.Should().Be(Now);
        tpl.UpdatedAt.Should().Be(Now);
        tpl.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Create_NullFieldsJson_DefaultsToEmptyArray()
    {
        var tpl = DocumentTemplate.Create(TenantId, "c", "brochure", "<p></p>", Now);

        tpl.FieldsJson.Should().Be("[]");
    }

    [Fact]
    public void Update_ChangesDocTypeAndHtml()
    {
        var tpl = DocumentTemplate.Create(TenantId, "c", "quote", "<old/>", Now);

        tpl.Update("brochure", "<new/>", "[{\"name\":\"x\"}]", Now.AddHours(1));

        tpl.DocType.Should().Be("brochure");
        tpl.TemplateHtml.Should().Be("<new/>");
        tpl.FieldsJson.Should().Be("[{\"name\":\"x\"}]");
        tpl.UpdatedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Update_BlankDocType_KeepsOriginal()
    {
        var tpl = DocumentTemplate.Create(TenantId, "c", "quote", "<h/>", Now);

        tpl.Update("", "<new/>", null, Now.AddMinutes(1));

        tpl.DocType.Should().Be("quote");
        tpl.TemplateHtml.Should().Be("<new/>");
    }

    [Fact]
    public void Update_BlankFieldsJson_ResetsToEmptyArray()
    {
        var tpl = DocumentTemplate.Create(TenantId, "c", "q", "<h/>", Now, "[{\"a\":1}]");

        tpl.Update("q", "<h/>", "   ", Now.AddMinutes(1));

        tpl.FieldsJson.Should().Be("[]");
    }
}
