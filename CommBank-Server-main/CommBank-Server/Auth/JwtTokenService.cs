using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CommBank.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CommBank.Auth;

/// <summary>
/// HS256 JWT issuer. Stamps standard registered claims plus role claims so that
/// <c>[Authorize(Roles = "...")]</c> works out of the box, and an <c>acr=mfa</c> claim on step-up tokens.
/// Stateless: validation needs only the shared signing key, issuer and audience.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public AccessToken CreateAccessToken(User user) =>
        Build(user, TimeSpan.FromMinutes(_options.AccessTokenMinutes), stepUp: false);

    public AccessToken CreateStepUpToken(User user) =>
        Build(user, TimeSpan.FromMinutes(_options.StepUpTokenMinutes), stepUp: true);

    private AccessToken Build(User user, TimeSpan lifetime, bool stepUp)
    {
        if (string.IsNullOrWhiteSpace(user.Id))
        {
            throw new InvalidOperationException("Cannot issue a token for a user without an id.");
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        DateTime now = DateTime.UtcNow;
        DateTime expires = now.Add(lifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (!string.IsNullOrWhiteSpace(user.Name))
        {
            claims.Add(new Claim(ClaimTypes.Name, user.Name));
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));
        }

        foreach (string role in user.Roles ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        if (stepUp)
        {
            // Authentication Context Class Reference: proves a fresh MFA challenge was satisfied.
            claims.Add(new Claim("acr", "mfa"));
            claims.Add(new Claim("amr", "otp"));
        }

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires, "Bearer");
    }
}
