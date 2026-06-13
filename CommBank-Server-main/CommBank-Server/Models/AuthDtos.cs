using System.ComponentModel.DataAnnotations;

namespace CommBank.Models;

/// <summary>Request to exchange a refresh token for a fresh access + refresh token pair.</summary>
public sealed class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = "";
}

/// <summary>Access + refresh token pair returned by login and refresh.</summary>
public sealed record TokenResponse(
    string Token,
    DateTime ExpiresAtUtc,
    string TokenType,
    string RefreshToken,
    DateTime RefreshExpiresAtUtc);

/// <summary>A 6-digit TOTP code (MFA verification and step-up).</summary>
public sealed class MfaCodeInput
{
    [Required]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Code must be six digits.")]
    public string Code { get; set; } = "";
}

/// <summary>Returned on MFA enrollment: the secret plus the otpauth URI for an authenticator app.</summary>
public sealed record MfaEnrollResponse(string Secret, string OtpAuthUri);

/// <summary>An elevated step-up token (carries acr=mfa) for sensitive operations.</summary>
public sealed record StepUpResponse(string Token, DateTime ExpiresAtUtc, string TokenType);
