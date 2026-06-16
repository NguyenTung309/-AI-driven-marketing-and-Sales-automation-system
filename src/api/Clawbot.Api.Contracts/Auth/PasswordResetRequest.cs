namespace Clawbot.Api.Contracts.Auth;

public sealed record PasswordResetRequest(string Email);

public sealed record PasswordResetConfirm(string Email, string Token, string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
