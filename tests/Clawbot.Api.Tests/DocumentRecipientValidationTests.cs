using System.Text.Json;
using Clawbot.Api.Contracts.Documents;
using Clawbot.Api.Jobs;
using Clawbot.Api.Services;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class DocumentRecipientValidationTests
{
    [Theory]
    [InlineData("bad-address")]
    [InlineData("Name <customer@example.com>")]
    [InlineData("first@example.com,second@example.com")]
    [InlineData("customer@example.com\r\nBcc: other@example.com")]
    public void Validate_RejectsInvalidDirectRecipient(string recipientEmail)
    {
        // Act
        var result = DocumentDeliveryTargetValidator.Validate(
            "email",
            contactId: null,
            recipientEmail);

        // Assert
        result.IsValid.Should().BeFalse();
        result.RecipientEmail.Should().BeNull();
        result.Error.Should().Be("recipientEmail invalid");
    }

    [Fact]
    public void Validate_NormalizesSingleMailboxForEmailDelivery()
    {
        // Act
        var result = DocumentDeliveryTargetValidator.Validate(
            "email",
            contactId: null,
            "  customer@example.com  ");

        // Assert
        result.IsValid.Should().BeTrue();
        result.RecipientEmail.Should().Be("customer@example.com");
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Validate_RequiresContactOrDirectRecipientForEmailDelivery()
    {
        // Act
        var result = DocumentDeliveryTargetValidator.Validate(
            "email",
            contactId: null,
            recipientEmail: null);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("recipientEmail or contactId required for email delivery");
    }

    [Fact]
    public void Validate_AllowsContactEmailFallback()
    {
        // Act
        var result = DocumentDeliveryTargetValidator.Validate(
            "email",
            Guid.NewGuid(),
            recipientEmail: null);

        // Assert
        result.IsValid.Should().BeTrue();
        result.RecipientEmail.Should().BeNull();
    }

    [Fact]
    public void Validate_DiscardsUnusedRecipientForCreateOnlyJob()
    {
        // Act
        var result = DocumentDeliveryTargetValidator.Validate(
            sentVia: null,
            contactId: null,
            "customer@example.com");

        // Assert
        result.IsValid.Should().BeTrue();
        result.RecipientEmail.Should().BeNull();
    }

    [Fact]
    public void GenerateRequest_RemainsCompatibleWhenRecipientEmailIsOmitted()
    {
        // Act
        var request = new GenerateDocumentRequest(
            "BAO-GIA",
            ContactId: null,
            Vars: null,
            SentVia: null);

        // Assert
        request.RecipientEmail.Should().BeNull();
    }

    [Fact]
    public void LegacyJobPayload_DeserializesWithNullRecipientEmail()
    {
        // Arrange
        const string json = """
            {
              "templateCode": "BAO-GIA",
              "contactId": null,
              "vars": null,
              "sentVia": "email"
            }
            """;

        // Act
        var payload = JsonSerializer.Deserialize<DocsGenerateJobPayload>(json);

        // Assert
        payload.Should().NotBeNull();
        payload!.RecipientEmail.Should().BeNull();
    }
}
