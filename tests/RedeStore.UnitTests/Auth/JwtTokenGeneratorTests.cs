using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using RedeStore.Infrastructure.Auth;
using Xunit;

namespace RedeStore.UnitTests.Auth;

public class JwtTokenGeneratorTests
{
    private static readonly JwtOptions Options = new()
    {
        SigningKey = "chave-de-teste-com-pelo-menos-32-caracteres",
        Issuer = "RedeStore.Tests",
        Audience = "RedeStore.Tests.Clients",
        ExpirationHours = 8,
    };

    private readonly JwtTokenGenerator _sut = new(Microsoft.Extensions.Options.Options.Create(Options));

    [Fact]
    public void GenerateToken_ProducesTokenWithExpectedClaims()
    {
        var usuarioId = Guid.NewGuid();

        var token = _sut.GenerateToken(usuarioId, "jovem@rede.com", "jovem");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(usuarioId.ToString(), jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("jovem@rede.com", jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("jovem", jwt.Claims.Single(c => c.Type == ClaimTypes.Role).Value);
    }

    [Fact]
    public void GenerateToken_ProducesTokenThatPassesValidationWithSameOptions()
    {
        var usuarioId = Guid.NewGuid();
        var token = _sut.GenerateToken(usuarioId, "admin@rede.com", "admin");
        var validationParameters = JwtTokenValidationParametersFactory.Create(Options);

        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        var principal = handler.ValidateToken(token, validationParameters, out _);

        Assert.Equal("admin@rede.com", principal.FindFirst(JwtRegisteredClaimNames.Email)!.Value);
        Assert.Equal("admin", principal.FindFirst(ClaimTypes.Role)!.Value);
    }
}
