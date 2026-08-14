using System.Net.Mail;

namespace Clawbot.Api.Services;

internal sealed record DocumentDeliveryTargetValidation(
    bool IsValid,
    string? RecipientEmail,
    string? Error);

internal static class DocumentDeliveryTargetValidator
{
    private const int MaxEmailLength = 320;
    private const int MaxLocalPartLength = 64;
    private const int MaxDomainLength = 255;

    public static DocumentDeliveryTargetValidation Validate(
        string? sentVia,
        Guid? contactId,
        string? recipientEmail)
    {
        if (!string.Equals(sentVia, "email", StringComparison.OrdinalIgnoreCase))
            return new DocumentDeliveryTargetValidation(true, null, null);

        if (!TryNormalizeEmail(recipientEmail, out var normalizedRecipient))
        {
            return new DocumentDeliveryTargetValidation(
                false,
                null,
                "recipientEmail invalid");
        }

        if (contactId is null && normalizedRecipient is null)
        {
            return new DocumentDeliveryTargetValidation(
                false,
                null,
                "recipientEmail or contactId required for email delivery");
        }

        return new DocumentDeliveryTargetValidation(true, normalizedRecipient, null);
    }

    public static bool TryNormalizeEmail(
        string? recipientEmail,
        out string? normalizedRecipient)
    {
        normalizedRecipient = null;
        if (string.IsNullOrWhiteSpace(recipientEmail))
            return true;

        var trimmed = recipientEmail.Trim();
        if (trimmed.Length > MaxEmailLength ||
            trimmed.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)) ||
            trimmed.IndexOfAny([',', ';', '<', '>']) >= 0 ||
            !MailAddress.TryCreate(trimmed, out var address) ||
            !string.IsNullOrEmpty(address.DisplayName) ||
            !string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(address.User) ||
            address.User.Length > MaxLocalPartLength ||
            string.IsNullOrWhiteSpace(address.Host) ||
            address.Host.Length > MaxDomainLength)
        {
            return false;
        }

        normalizedRecipient = address.Address;
        return true;
    }
}
