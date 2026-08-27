using System.Net;
using System.Net.Http.Json;
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
}
