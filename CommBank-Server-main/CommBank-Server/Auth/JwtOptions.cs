namespace CommBank.Auth;

/// <summary>
/// Non-secret JWT configuration, bound from the "Jwt" section. The signing key is intentionally NOT
/// part of committed config — it is resolved from the secret providers (env var 'Jwt__SigningKey' or
/// user-secrets) and validated fail-fast at startup.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>When false, authentication middleware is not registered (development/testing only).</summary>
    public bool Enabled { get; set; } = true;

    public string Issuer { get; set; } = "CommBank";

    public string Audience { get; set; } = "CommBank.Clients";

    /// <summary>Access-token lifetime in minutes. Short-lived; paired with refresh tokens.</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Refresh-token lifetime in days.</summary>
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>Lifetime of an elevated step-up (MFA) token in minutes.</summary>
    public int StepUpTokenMinutes { get; set; } = 10;

    /// <summary>HS256 signing key. Minimum 32 bytes (256-bit). Supplied via secrets, never committed.</summary>
    public string SigningKey { get; set; } = "";
}
