namespace CommBank.Models;

/// <summary>The payload returned on a successful login. Never includes the password or its hash.</summary>
/// <param name="Token">Bearer access token (JWT), short-lived.</param>
/// <param name="ExpiresAtUtc">Access-token expiry (UTC).</param>
/// <param name="TokenType">Always "Bearer".</param>
/// <param name="RefreshToken">Opaque refresh token used to obtain new access tokens.</param>
/// <param name="RefreshExpiresAtUtc">Refresh-token expiry (UTC).</param>
/// <param name="UserId">Authenticated user's id.</param>
/// <param name="Name">Display name, if set.</param>
/// <param name="Roles">Roles granted to the user.</param>
/// <param name="MfaEnabled">Whether the user has MFA enabled (step-up will be required for sensitive ops).</param>
public sealed record LoginResponse(
    string Token,
    DateTime ExpiresAtUtc,
    string TokenType,
    string RefreshToken,
    DateTime RefreshExpiresAtUtc,
    string UserId,
    string? Name,
    IReadOnlyList<string> Roles,
    bool MfaEnabled);
