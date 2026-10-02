using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using RedeStore.Application.Auth.Dtos;
using Xunit;

namespace RedeStore.IntegrationTests.Auth;

[Collection(IntegrationTestCollection.Name)]
public class AuthEndpointsSmokeTests
{
    private readonly HttpClient _client;

    public AuthEndpointsSmokeTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostCadastro_ComDadosValidos_Retorna200ComToken()
    {
        var request = new CadastroRequest("Fulano", $"{Guid.NewGuid()}@teste.com", "senha12345");

        var response = await _client.PostAsJsonAsync("/auth/cadastro", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var corpo = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotEmpty(corpo!.Token);
    }

    [Fact]
    public async Task GetMe_SemToken_Retorna401()
    {
        var response = await _client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostCadastro_ComEmailInvalido_Retorna400()
    {
        var request = new CadastroRequest("Fulano", "nao-e-um-email", "senha12345");

        var response = await _client.PostAsJsonAsync("/auth/cadastro", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_ComTokenExpirado_Retorna401()
    {
        // Mesma chave/issuer/audience configurados pelo ApiFactory para o ambiente de testes.
        var chaveAssinatura = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("chave-de-teste-para-integracao-com-pelo-menos-32-caracteres"));
        var credenciais = new SigningCredentials(chaveAssinatura, SecurityAlgorithms.HmacSha256);

        var tokenExpirado = new JwtSecurityToken(
            issuer: "RedeStore.IntegrationTests",
            audience: "RedeStore.IntegrationTests.Clients",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: credenciais);
        var tokenString = new JwtSecurityTokenHandler().WriteToken(tokenExpirado);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenString);

        var response = await _client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
