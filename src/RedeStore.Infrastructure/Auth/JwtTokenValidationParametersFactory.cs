using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace RedeStore.Infrastructure.Auth;

public static class JwtTokenValidationParametersFactory
{
    public static TokenValidationParameters Create(JwtOptions options) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
    };
}
