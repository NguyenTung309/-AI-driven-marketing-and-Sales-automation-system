namespace Clawbot.Api.Contracts.Auth;

public sealed record TwoFactorEnableResponse(string SharedKey, string AuthenticatorUri);

public sealed record TwoFactorVerifyRequest(string Code);

public sealed record TwoFactorLoginRequest(string Email, string Password, string Code);
