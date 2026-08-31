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

namespace RedeStore.IntegrationTests.Inscricoes;

[Collection(IntegrationTestCollection.Name)]
public class InscricaoFlowEndToEndTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public InscricaoFlowEndToEndTests(ApiFactory factory)
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

    private async Task<HttpClient> CriarClienteJovemAsync()
    {
        var email = $"{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Jovem", email, "senha12345"));
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "senha12345"));
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return cliente;
    }

    private async Task<Guid> CriarEventoAsync(HttpClient clienteAdmin, int vagasTotais)
    {
        var criarResponse = await clienteAdmin.PostAsJsonAsync("/eventos", new CriarEventoRequest(
            Titulo: $"Retiro {Guid.NewGuid()}",
            Descricao: "Retiro anual",
            DataHora: DateTime.UtcNow.AddDays(30),
            Local: "Sítio da Rede",
            Preco: 50m,
            VagasTotais: vagasTotais,
            Foto: "https://exemplo.com/retiro.jpg"));
        var evento = await criarResponse.Content.ReadFromJsonAsync<EventoDto>();
        return evento!.Id;
    }

    [Fact]
    public async Task FluxoCompleto_InscreverListarCancelar_FuncionaPontaAPonta()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var eventoId = await CriarEventoAsync(clienteAdmin, vagasTotais: 10);
        using var clienteJovem = await CriarClienteJovemAsync();

        var inscreverResponse = await clienteJovem.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);
        inscreverResponse.EnsureSuccessStatusCode();
        var resultado = await inscreverResponse.Content.ReadFromJsonAsync<ResultadoInscricaoDto>();
        Assert.Equal("criada", resultado!.Resultado);
        Assert.Equal(50m, resultado.Inscricao!.ValorPago);

        var vagasResponse = await _client.GetAsync($"/eventos/{eventoId}/vagas-restantes");
        var vagas = await vagasResponse.Content.ReadFromJsonAsync<VagasRestantesDto>();
        Assert.Equal(9, vagas!.VagasRestantes);

        var minhasInscricoesResponse = await clienteJovem.GetAsync("/usuarios/me/inscricoes");
        minhasInscricoesResponse.EnsureSuccessStatusCode();
        var minhasInscricoes = await minhasInscricoesResponse.Content.ReadFromJsonAsync<List<InscricaoDto>>();
        Assert.Single(minhasInscricoes!);

        var inscricoesDoEventoResponse = await clienteAdmin.GetAsync($"/eventos/{eventoId}/inscricoes");
        inscricoesDoEventoResponse.EnsureSuccessStatusCode();
        var inscricoesDoEvento = await inscricoesDoEventoResponse.Content.ReadFromJsonAsync<List<InscricaoDto>>();
        Assert.Single(inscricoesDoEvento!);

        var cancelarResponse = await clienteJovem.PatchAsync($"/inscricoes/{resultado.Inscricao.Id}/cancelar", content: null);
        cancelarResponse.EnsureSuccessStatusCode();
        var cancelada = await cancelarResponse.Content.ReadFromJsonAsync<InscricaoDto>();
        Assert.Equal("cancelada", cancelada!.Status);

        var vagasAposCancelarResponse = await _client.GetAsync($"/eventos/{eventoId}/vagas-restantes");
        var vagasAposCancelar = await vagasAposCancelarResponse.Content.ReadFromJsonAsync<VagasRestantesDto>();
        Assert.Equal(10, vagasAposCancelar!.VagasRestantes);
    }

    [Fact]
    public async Task Inscrever_MesmoUsuarioDuasVezes_SegundaChamadaRetornaJaInscritoSemDuplicar()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var eventoId = await CriarEventoAsync(clienteAdmin, vagasTotais: 10);
        using var clienteJovem = await CriarClienteJovemAsync();

        await clienteJovem.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);
        var segundaResponse = await clienteJovem.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);

        segundaResponse.EnsureSuccessStatusCode();
        var resultado = await segundaResponse.Content.ReadFromJsonAsync<ResultadoInscricaoDto>();
        Assert.Equal("ja_inscrito", resultado!.Resultado);

        var minhasInscricoesResponse = await clienteJovem.GetAsync("/usuarios/me/inscricoes");
        var minhasInscricoes = await minhasInscricoesResponse.Content.ReadFromJsonAsync<List<InscricaoDto>>();
        Assert.Single(minhasInscricoes!);
    }

    [Fact]
    public async Task CancelarInscricao_PorUsuarioNaoDonoNaoAdmin_Retorna403()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var eventoId = await CriarEventoAsync(clienteAdmin, vagasTotais: 10);
        using var dono = await CriarClienteJovemAsync();
        var inscreverResponse = await dono.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);
        var resultado = await inscreverResponse.Content.ReadFromJsonAsync<ResultadoInscricaoDto>();

        using var outroUsuario = await CriarClienteJovemAsync();
        var cancelarResponse = await outroUsuario.PatchAsync($"/inscricoes/{resultado!.Inscricao!.Id}/cancelar", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, cancelarResponse.StatusCode);
    }

    [Fact]
    public async Task CancelarInscricao_PeloAdmin_Retorna200MesmoNaoSendoODono()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var eventoId = await CriarEventoAsync(clienteAdmin, vagasTotais: 10);
        using var dono = await CriarClienteJovemAsync();
        var inscreverResponse = await dono.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);
        var resultado = await inscreverResponse.Content.ReadFromJsonAsync<ResultadoInscricaoDto>();

        var cancelarResponse = await clienteAdmin.PatchAsync($"/inscricoes/{resultado!.Inscricao!.Id}/cancelar", content: null);

        Assert.Equal(HttpStatusCode.OK, cancelarResponse.StatusCode);
    }

    [Fact]
    public async Task GetInscricoesDoEvento_ComTokenDeUsuarioNaoAdmin_Retorna403()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var eventoId = await CriarEventoAsync(clienteAdmin, vagasTotais: 10);
        using var clienteJovem = await CriarClienteJovemAsync();

        var response = await clienteJovem.GetAsync($"/eventos/{eventoId}/inscricoes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DuasInscricoesSimultaneasNaUltimaVaga_ApenasUmaVemCriadaAOutraVemEsgotada()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var eventoId = await CriarEventoAsync(clienteAdmin, vagasTotais: 1);
        using var primeiroUsuario = await CriarClienteJovemAsync();
        using var segundoUsuario = await CriarClienteJovemAsync();

        var tarefaUm = primeiroUsuario.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);
        var tarefaDois = segundoUsuario.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);
        await Task.WhenAll(tarefaUm, tarefaDois);

        var resultadoUm = await (await tarefaUm).Content.ReadFromJsonAsync<ResultadoInscricaoDto>();
        var resultadoDois = await (await tarefaDois).Content.ReadFromJsonAsync<ResultadoInscricaoDto>();
        var resultados = new[] { resultadoUm!.Resultado, resultadoDois!.Resultado };

        Assert.Single(resultados, r => r == "criada");
        Assert.Single(resultados, r => r == "esgotado");

        var vagasResponse = await _client.GetAsync($"/eventos/{eventoId}/vagas-restantes");
        var vagas = await vagasResponse.Content.ReadFromJsonAsync<VagasRestantesDto>();
        Assert.Equal(0, vagas!.VagasRestantes);
    }
}
