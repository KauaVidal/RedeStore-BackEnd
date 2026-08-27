using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// Reproduces the exact validation path used by the real running API (Program.cs wires
    /// AddAuthentication().AddJwtBearer(options => { options.MapInboundClaims = false; options.TokenValidationParameters = ...; }))
    /// instead of a raw JwtSecurityTokenHandler, to prove sub/email/role survive through the actual
    /// JwtBearerHandler token handler pipeline (JwtBearerOptions.TokenHandlers), not just an approximation.
    /// </summary>
    [Fact]
    public async Task GenerateToken_ValidatesThroughRealJwtBearerOptionsPipeline()
    {
        var usuarioId = Guid.NewGuid();
        var token = _sut.GenerateToken(usuarioId, "pipeline@rede.com", "admin");

        var services = new ServiceCollection();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = JwtTokenValidationParametersFactory.Create(Options);
            });

        await using var provider = services.BuildServiceProvider();
        var jwtBearerOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.NotEmpty(jwtBearerOptions.TokenHandlers);
        var tokenHandler = jwtBearerOptions.TokenHandlers[0];

        var result = await tokenHandler.ValidateTokenAsync(token, jwtBearerOptions.TokenValidationParameters);

        Assert.True(result.IsValid, result.Exception?.ToString());
        var principal = new ClaimsPrincipal(result.ClaimsIdentity);

        Assert.Equal(usuarioId.ToString(), principal.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        Assert.Equal("pipeline@rede.com", principal.FindFirst(JwtRegisteredClaimNames.Email)!.Value);
        Assert.Equal("admin", principal.FindFirst(ClaimTypes.Role)!.Value);
    }
}
