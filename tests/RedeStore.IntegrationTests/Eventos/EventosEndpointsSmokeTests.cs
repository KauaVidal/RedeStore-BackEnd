using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Application.Eventos.Dtos;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Eventos;

[Collection(IntegrationTestCollection.Name)]
public class EventosEndpointsSmokeTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public EventosEndpointsSmokeTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> ObterTokenAdminAsync()
    {
        var email = $"{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Admin", email, "senha12345"));

        using (var scope = _factory.Services.CreateScope())
        {
            var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
            var usuario = await usuarioRepositorio.BuscarPorEmailAsync(email, CancellationToken.None);
            usuario!.Papel = Papel.Admin;
            await usuarioRepositorio.AtualizarAsync(usuario, CancellationToken.None);
        }

        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "senha12345"));
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        return login!.Token;
    }

    private static CriarEventoRequest RequestValido() => new(
        Titulo: $"Culto {Guid.NewGuid()}",
        Descricao: "Encontro semanal",
        DataHora: DateTime.UtcNow.AddDays(7),
        Local: "Templo Sede",
        Preco: 0m,
        VagasTotais: 50,
        Foto: "https://exemplo.com/evento.jpg");

    [Fact]
    public async Task PostEventos_SemToken_Retorna401()
    {
        var response = await _client.PostAsJsonAsync("/eventos", RequestValido());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostEventos_ComTokenDeAdmin_Retorna200EDepoisApareceNoDetalhe()
    {
        var token = await ObterTokenAdminAsync();
        using var clienteAdmin = _factory.CreateClient();
        clienteAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var criarResponse = await clienteAdmin.PostAsJsonAsync("/eventos", RequestValido());
        Assert.Equal(HttpStatusCode.OK, criarResponse.StatusCode);
        var criado = await criarResponse.Content.ReadFromJsonAsync<EventoDto>();
        Assert.Equal(50, criado!.VagasRestantes);

        var detalheResponse = await _client.GetAsync($"/eventos/{criado.Id}");
        Assert.Equal(HttpStatusCode.OK, detalheResponse.StatusCode);
    }

    [Fact]
    public async Task PostEventos_ComVagasTotaisZero_Retorna400()
    {
        var token = await ObterTokenAdminAsync();
        using var clienteAdmin = _factory.CreateClient();
        clienteAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var request = RequestValido() with { VagasTotais = 0 };

        var response = await clienteAdmin.PostAsJsonAsync("/eventos", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetEventoPorId_ComIdInexistente_Retorna404()
    {
        var response = await _client.GetAsync($"/eventos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetVagasRestantes_EPublico_RetornaVagasTotaisParaEventoSemInscricoes()
    {
        var token = await ObterTokenAdminAsync();
        using var clienteAdmin = _factory.CreateClient();
        clienteAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var criarResponse = await clienteAdmin.PostAsJsonAsync("/eventos", RequestValido());
        var criado = await criarResponse.Content.ReadFromJsonAsync<EventoDto>();

        var response = await _client.GetAsync($"/eventos/{criado!.Id}/vagas-restantes");

        response.EnsureSuccessStatusCode();
        var vagas = await response.Content.ReadFromJsonAsync<VagasRestantesDto>();
        Assert.Equal(50, vagas!.VagasRestantes);
    }
}
