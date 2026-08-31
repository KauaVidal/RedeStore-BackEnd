using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Application.Eventos.Dtos;
using RedeStore.Application.Inscricoes.Dtos;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Eventos;

[Collection(IntegrationTestCollection.Name)]
public class EventoFlowEndToEndTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public EventoFlowEndToEndTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<HttpClient> CriarClienteAdminAsync()
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

        var clienteAdmin = _factory.CreateClient();
        clienteAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return clienteAdmin;
    }

    private static CriarEventoRequest RequestValido(int vagasTotais = 50) => new(
        Titulo: $"Culto {Guid.NewGuid()}",
        Descricao: "Encontro semanal",
        DataHora: DateTime.UtcNow.AddDays(7),
        Local: "Templo Sede",
        Preco: 0m,
        VagasTotais: vagasTotais,
        Foto: "https://exemplo.com/evento.jpg");

    [Fact]
    public async Task FluxoCompleto_CriarListarDetalharAtualizarDeletar_FuncionaPontaAPonta()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var tituloUnico = $"Culto {Guid.NewGuid()}";

        var criarResponse = await clienteAdmin.PostAsJsonAsync("/eventos", RequestValido() with { Titulo = tituloUnico });
        criarResponse.EnsureSuccessStatusCode();
        var criado = await criarResponse.Content.ReadFromJsonAsync<EventoDto>();
        Assert.Equal(50, criado!.VagasRestantes);

        var listaResponse = await _client.GetAsync("/eventos");
        listaResponse.EnsureSuccessStatusCode();
        var lista = await listaResponse.Content.ReadFromJsonAsync<List<EventoDto>>();
        Assert.Contains(lista!, e => e.Id == criado.Id);

        var detalheResponse = await _client.GetAsync($"/eventos/{criado.Id}");
        detalheResponse.EnsureSuccessStatusCode();

        var atualizarResponse = await clienteAdmin.PatchAsJsonAsync($"/eventos/{criado.Id}",
            new AtualizarEventoRequest("Novo Título", null, null, null, null, null, null));
        atualizarResponse.EnsureSuccessStatusCode();
        var atualizado = await atualizarResponse.Content.ReadFromJsonAsync<EventoDto>();
        Assert.Equal("Novo Título", atualizado!.Titulo);

        var detalheAposAtualizarResponse = await _client.GetAsync($"/eventos/{criado.Id}");
        var detalheAposAtualizar = await detalheAposAtualizarResponse.Content.ReadFromJsonAsync<EventoDto>();
        Assert.Equal("Novo Título", detalheAposAtualizar!.Titulo);

        var deletarResponse = await clienteAdmin.DeleteAsync($"/eventos/{criado.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deletarResponse.StatusCode);

        var depoisDeDeletarResponse = await _client.GetAsync($"/eventos/{criado.Id}");
        Assert.Equal(HttpStatusCode.NotFound, depoisDeDeletarResponse.StatusCode);
    }

    [Fact]
    public async Task PatchEventos_ComTokenDeUsuarioNaoAdmin_Retorna403()
    {
        var email = $"{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Jovem", email, "senha12345"));
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "senha12345"));
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        using var clienteNaoAdmin = _factory.CreateClient();
        clienteNaoAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);

        var response = await clienteNaoAdmin.PatchAsJsonAsync($"/eventos/{Guid.NewGuid()}",
            new AtualizarEventoRequest("Novo Título", null, null, null, null, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEvento_ComInscricaoConfirmada_Retorna409EEventoContinuaAcessivel()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var criarResponse = await clienteAdmin.PostAsJsonAsync("/eventos", RequestValido());
        var criado = await criarResponse.Content.ReadFromJsonAsync<EventoDto>();

        var email = $"{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Jovem", email, "senha12345"));
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "senha12345"));
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        using var clienteJovem = _factory.CreateClient();
        clienteJovem.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        await clienteJovem.PostAsync($"/eventos/{criado!.Id}/inscricoes", content: null);

        var deletarResponse = await clienteAdmin.DeleteAsync($"/eventos/{criado.Id}");

        Assert.Equal(HttpStatusCode.Conflict, deletarResponse.StatusCode);
        var aindaExisteResponse = await _client.GetAsync($"/eventos/{criado.Id}");
        Assert.Equal(HttpStatusCode.OK, aindaExisteResponse.StatusCode);
    }
}
