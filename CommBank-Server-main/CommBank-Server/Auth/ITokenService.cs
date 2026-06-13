using CommBank.Models;

namespace CommBank.Auth;

/// <summary>Issues signed access tokens (and elevated step-up tokens) for authenticated users.</summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(User user);

    /// <summary>An elevated token carrying <c>acr=mfa</c>, required for step-up-protected operations.</summary>
    AccessToken CreateStepUpToken(User user);
}

/// <summary>A minted access token and its metadata.</summary>
/// <param name="Token">The compact-serialized JWT.</param>
/// <param name="ExpiresAtUtc">Absolute expiry (UTC).</param>
/// <param name="TokenType">Always "Bearer".</param>
public sealed record AccessToken(string Token, DateTime ExpiresAtUtc, string TokenType);
