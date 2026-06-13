namespace CommBank.Auth;

/// <summary>Result of a successful refresh-token rotation.</summary>
public sealed record RefreshRotationResult(string RawToken, DateTime ExpiresAtUtc, string UserId);

/// <summary>Issues, rotates and revokes refresh tokens with reuse detection.</summary>
public interface IRefreshTokenService
{
    Task<(string RawToken, DateTime ExpiresAtUtc)> IssueAsync(string userId, string? ip, CancellationToken cancellationToken = default);

    /// <summary>Validates and rotates a refresh token, or returns null if it is invalid/expired/reused.</summary>
    Task<RefreshRotationResult?> RotateAsync(string rawToken, string? ip, CancellationToken cancellationToken = default);

    Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default);
}
