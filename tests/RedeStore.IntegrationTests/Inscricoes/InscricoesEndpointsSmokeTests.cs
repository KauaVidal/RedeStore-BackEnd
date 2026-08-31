using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Inscricoes.Dtos;
using Xunit;

namespace RedeStore.IntegrationTests.Inscricoes;

[Collection(IntegrationTestCollection.Name)]
public class InscricoesEndpointsSmokeTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public InscricoesEndpointsSmokeTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostInscricoes_SemToken_Retorna401()
    {
        var response = await _client.PostAsync($"/eventos/{Guid.NewGuid()}/inscricoes", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMinhasInscricoes_SemToken_Retorna401()
    {
        var response = await _client.GetAsync("/usuarios/me/inscricoes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostInscricoes_ComEventoInexistente_Retorna404()
    {
        var email = $"{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Jovem", email, "senha12345"));
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "senha12345"));
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        using var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);

        var response = await cliente.PostAsync($"/eventos/{Guid.NewGuid()}/inscricoes", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
