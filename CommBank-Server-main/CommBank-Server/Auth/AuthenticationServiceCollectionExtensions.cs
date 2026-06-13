using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace CommBank.Auth;

/// <summary>
/// Registers JWT bearer authentication for the API. Binds <see cref="JwtOptions"/>, validates the
/// signing key fail-fast, and configures strict token validation (issuer, audience, lifetime, signature).
/// The token issuer is always registered so it can be unit-tested even when the middleware is disabled.
/// </summary>
public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddCommBankJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(JwtOptions.SectionName);
        services.Configure<JwtOptions>(section);

        JwtOptions options = section.Get<JwtOptions>() ?? new JwtOptions();

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IRefreshTokenService, MongoRefreshTokenService>();
        services.AddSingleton<ITotpService, TotpService>();

        if (!options.Enabled)
        {
            return services;
        }

        // Fail fast: never start an auth-enabled banking API with a missing or weak signing key.
        if (string.IsNullOrWhiteSpace(options.SigningKey) || Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "JWT signing key is missing or too short (need >= 32 bytes for HS256). Provide it via the " +
                "environment variable 'Jwt__SigningKey' or .NET user-secrets " +
                "(dotnet user-secrets set \"Jwt:SigningKey\" \"<64+ random characters>\"). Never commit it to source control.");
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.SaveToken = true;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role
                };
            });

        services.AddAuthorization();

        return services;
    }
}
