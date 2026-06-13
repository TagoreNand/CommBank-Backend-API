using System.Security.Claims;
using CommBank.Auth;
using CommBank.Models;
using CommBank.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CommBank.Controllers;

[ApiController]
[Route("api/Auth")]
public class AuthController : ControllerBase
{
    private const string Issuer = "CommBank";

    private readonly IAuthService _authService;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly ITotpService _totp;
    private readonly IUsersService _users;

    public AuthController(
        IAuthService authService,
        ITokenService tokenService,
        IRefreshTokenService refreshTokens,
        ITotpService totp,
        IUsersService users)
    {
        _authService = authService;
        _tokenService = tokenService;
        _refreshTokens = refreshTokens;
        _totp = totp;
        _users = users;
    }

    /// <summary>Authenticate and receive a short-lived access token + a refresh token.</summary>
    [AllowAnonymous]
    [HttpPost("Login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginInput input)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.Email) || string.IsNullOrWhiteSpace(input.Password))
        {
            return BadRequest("Email and password are required.");
        }

        User? user = await _authService.Login(input.Email, input.Password);

        // Uniform 401 for both unknown email and wrong password — no account enumeration oracle.
        if (user?.Id is null)
        {
            return Unauthorized("Invalid credentials.");
        }

        AccessToken access = _tokenService.CreateAccessToken(user);
        (string refreshRaw, DateTime refreshExp) = await _refreshTokens.IssueAsync(user.Id, ClientIp(), HttpContext.RequestAborted);

        return Ok(new LoginResponse(
            access.Token,
            access.ExpiresAtUtc,
            access.TokenType,
            refreshRaw,
            refreshExp,
            user.Id,
            user.Name,
            user.Roles ?? new List<string> { "Customer" },
            user.MfaEnabled));
    }

    /// <summary>Exchange a refresh token for a new access + refresh pair (rotation; reuse is detected).</summary>
    [AllowAnonymous]
    [HttpPost("Refresh")]
    public async Task<ActionResult<TokenResponse>> Refresh(RefreshRequest request)
    {
        RefreshRotationResult? rotation = await _refreshTokens.RotateAsync(request.RefreshToken, ClientIp(), HttpContext.RequestAborted);
        if (rotation is null)
        {
            return Unauthorized("Invalid or expired refresh token.");
        }

        User? user = await _users.GetAsync(rotation.UserId);
        if (user?.Id is null)
        {
            return Unauthorized("Invalid refresh token.");
        }

        AccessToken access = _tokenService.CreateAccessToken(user);
        return Ok(new TokenResponse(access.Token, access.ExpiresAtUtc, access.TokenType, rotation.RawToken, rotation.ExpiresAtUtc));
    }

    /// <summary>Revoke a refresh token.</summary>
    [AllowAnonymous]
    [HttpPost("Logout")]
    public async Task<IActionResult> Logout(RefreshRequest request)
    {
        await _refreshTokens.RevokeAsync(request.RefreshToken, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>Begin MFA enrollment: returns a TOTP secret and an otpauth URI to scan. Not active until verified.</summary>
    [Authorize]
    [HttpPost("mfa/enroll")]
    public async Task<ActionResult<MfaEnrollResponse>> EnrollMfa()
    {
        User? user = await CurrentUserAsync();
        if (user?.Id is null)
        {
            return Unauthorized();
        }

        string secret = _totp.GenerateSecret();
        user.MfaSecret = secret;
        user.MfaEnabled = false;
        await _users.UpdateAsync(user.Id, user);

        string uri = _totp.BuildOtpAuthUri(secret, user.Email ?? user.Id, Issuer);
        return Ok(new MfaEnrollResponse(secret, uri));
    }

    /// <summary>Confirm MFA enrollment by submitting a current code.</summary>
    [Authorize]
    [HttpPost("mfa/verify")]
    public async Task<IActionResult> VerifyMfa(MfaCodeInput input)
    {
        User? user = await CurrentUserAsync();
        if (user?.Id is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(user.MfaSecret))
        {
            return BadRequest("MFA is not enrolled. Call mfa/enroll first.");
        }

        if (!_totp.VerifyCode(user.MfaSecret, input.Code))
        {
            return BadRequest("Invalid code.");
        }

        user.MfaEnabled = true;
        await _users.UpdateAsync(user.Id, user);
        return NoContent();
    }

    /// <summary>Elevate the session: verify a current code and receive a short-lived step-up token (acr=mfa).</summary>
    [Authorize]
    [HttpPost("step-up")]
    public async Task<ActionResult<StepUpResponse>> StepUp(MfaCodeInput input)
    {
        User? user = await CurrentUserAsync();
        if (user?.Id is null)
        {
            return Unauthorized();
        }

        if (!user.MfaEnabled || string.IsNullOrWhiteSpace(user.MfaSecret))
        {
            return BadRequest("MFA is not enabled for this account.");
        }

        if (!_totp.VerifyCode(user.MfaSecret, input.Code))
        {
            return Unauthorized("Invalid code.");
        }

        AccessToken stepUp = _tokenService.CreateStepUpToken(user);
        return Ok(new StepUpResponse(stepUp.Token, stepUp.ExpiresAtUtc, stepUp.TokenType));
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private async Task<User?> CurrentUserAsync()
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId) ? null : await _users.GetAsync(userId);
    }
}
