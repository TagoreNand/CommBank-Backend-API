using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CommBank.Auth;

/// <summary>
/// MongoDB-backed refresh tokens. Stores only the token hash, rotates on every use (new token issued, old
/// one revoked and linked), and detects reuse: presenting an already-revoked token revokes the user's whole
/// active set, defeating a stolen-token replay.
/// </summary>
public sealed class MongoRefreshTokenService : IRefreshTokenService
{
    private readonly IMongoCollection<RefreshToken> _tokens;
    private readonly JwtOptions _options;

    public MongoRefreshTokenService(IMongoDatabase database, IOptions<JwtOptions> options)
    {
        _tokens = database.GetCollection<RefreshToken>("RefreshTokens");
        _options = options.Value;

        try
        {
            _tokens.Indexes.CreateOne(new CreateIndexModel<RefreshToken>(
                Builders<RefreshToken>.IndexKeys.Ascending(t => t.TokenHash),
                new CreateIndexOptions { Unique = true, Name = "ux_token_hash" }));
        }
        catch
        {
            // Best-effort index creation; never fatal.
        }
    }

    public async Task<(string RawToken, DateTime ExpiresAtUtc)> IssueAsync(string userId, string? ip, CancellationToken cancellationToken = default)
    {
        string raw = NewRawToken();
        DateTime expires = DateTime.UtcNow.AddDays(_options.RefreshTokenDays);

        await _tokens.InsertOneAsync(new RefreshToken
        {
            TokenHash = Hash(raw),
            UserId = userId,
            ExpiresAtUtc = expires,
            CreatedByIp = ip
        }, cancellationToken: cancellationToken);

        return (raw, expires);
    }

    public async Task<RefreshRotationResult?> RotateAsync(string rawToken, string? ip, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        string hash = Hash(rawToken);
        RefreshToken? existing = await _tokens.Find(t => t.TokenHash == hash).FirstOrDefaultAsync(cancellationToken);
        if (existing is null || existing.UserId is null)
        {
            return null;
        }

        // Reuse detection: an already-revoked token is being presented again -> assume compromise.
        if (existing.RevokedAtUtc is not null)
        {
            await RevokeAllForUserAsync(existing.UserId, cancellationToken);
            return null;
        }

        if (DateTime.UtcNow >= existing.ExpiresAtUtc)
        {
            return null;
        }

        string newRaw = NewRawToken();
        string newHash = Hash(newRaw);
        DateTime expires = DateTime.UtcNow.AddDays(_options.RefreshTokenDays);

        await _tokens.InsertOneAsync(new RefreshToken
        {
            TokenHash = newHash,
            UserId = existing.UserId,
            ExpiresAtUtc = expires,
            CreatedByIp = ip
        }, cancellationToken: cancellationToken);

        await _tokens.UpdateOneAsync(
            t => t.Id == existing.Id,
            Builders<RefreshToken>.Update
                .Set(t => t.RevokedAtUtc, DateTime.UtcNow)
                .Set(t => t.ReplacedByTokenHash, newHash),
            cancellationToken: cancellationToken);

        return new RefreshRotationResult(newRaw, expires, existing.UserId);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return;
        }

        string hash = Hash(rawToken);
        await _tokens.UpdateOneAsync(
            t => t.TokenHash == hash && t.RevokedAtUtc == null,
            Builders<RefreshToken>.Update.Set(t => t.RevokedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);
    }

    private Task RevokeAllForUserAsync(string userId, CancellationToken cancellationToken) =>
        _tokens.UpdateManyAsync(
            t => t.UserId == userId && t.RevokedAtUtc == null,
            Builders<RefreshToken>.Update.Set(t => t.RevokedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

    private static string NewRawToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
