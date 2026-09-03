using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Application.Eventos.Dtos;
using RedeStore.Application.Inscricoes.Dtos;
using RedeStore.Application.Pedidos.Dtos;
using RedeStore.Application.Produtos.Dtos;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.CrossFeature;

[Collection(IntegrationTestCollection.Name)]
public class CrossFeatureFlowEndToEndTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public CrossFeatureFlowEndToEndTests(ApiFactory factory)
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

    private async Task<Guid> CriarProdutoComEstoqueAsync(HttpClient clienteAdmin, int estoque, decimal preco = 79.90m)
    {
        var response = await clienteAdmin.PostAsJsonAsync("/produtos", new CriarProdutoRequest(
            Nome: $"Camiseta {Guid.NewGuid()}",
            Categoria: "camisetas",
            Preco: preco,
            Descricao: "Camiseta de teste",
            Fotos: ["https://exemplo.com/foto.jpg"],
            Destaque: false,
            Variacoes: [new VariacaoRequest("M", "Preto", estoque)]));
        var produto = await response.Content.ReadFromJsonAsync<ProdutoDto>();
        return produto!.Id;
    }

    private async Task<Guid> CriarEventoAsync(HttpClient clienteAdmin, int vagasTotais)
    {
        var response = await clienteAdmin.PostAsJsonAsync("/eventos", new CriarEventoRequest(
            Titulo: $"Retiro {Guid.NewGuid()}",
            Descricao: "Retiro anual",
            DataHora: DateTime.UtcNow.AddDays(30),
            Local: "Sítio da Rede",
            Preco: 50m,
            VagasTotais: vagasTotais,
            Foto: "https://exemplo.com/retiro.jpg"));
        var evento = await response.Content.ReadFromJsonAsync<EventoDto>();
        return evento!.Id;
    }

    [Fact]
    public async Task FluxoDeUsuarioAtravesDeTodasAsFeatures_ComprarProdutoEInscreverEmEvento_ApareceCorretamenteParaOUsuarioEParaOAdmin()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 10);
        var eventoId = await CriarEventoAsync(clienteAdmin, vagasTotais: 5);
        using var clienteJovem = await CriarClienteJovemAsync();

        var criarPedidoResponse = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(produtoId, "M", "Preto", 1)], "retirada", null));
        criarPedidoResponse.EnsureSuccessStatusCode();
        var pedidoCriado = await criarPedidoResponse.Content.ReadFromJsonAsync<PedidoDto>();

        var inscreverResponse = await clienteJovem.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);
        inscreverResponse.EnsureSuccessStatusCode();
        var resultadoInscricao = await inscreverResponse.Content.ReadFromJsonAsync<ResultadoInscricaoDto>();
        Assert.Equal("criada", resultadoInscricao!.Resultado);

        var meusPedidosResponse = await clienteJovem.GetAsync("/usuarios/me/pedidos");
        var meusPedidos = await meusPedidosResponse.Content.ReadFromJsonAsync<List<PedidoDto>>();
        Assert.Contains(meusPedidos!, p => p.Id == pedidoCriado!.Id);

        var minhasInscricoesResponse = await clienteJovem.GetAsync("/usuarios/me/inscricoes");
        var minhasInscricoes = await minhasInscricoesResponse.Content.ReadFromJsonAsync<List<InscricaoDto>>();
        Assert.Contains(minhasInscricoes!, i => i.EventoId == eventoId);

        var todosOsPedidosResponse = await clienteAdmin.GetAsync("/pedidos");
        var todosOsPedidos = await todosOsPedidosResponse.Content.ReadFromJsonAsync<List<PedidoDto>>();
        Assert.Contains(todosOsPedidos!, p => p.Id == pedidoCriado!.Id);

        var inscricoesDoEventoResponse = await clienteAdmin.GetAsync($"/eventos/{eventoId}/inscricoes");
        var inscricoesDoEvento = await inscricoesDoEventoResponse.Content.ReadFromJsonAsync<List<InscricaoDto>>();
        Assert.Contains(inscricoesDoEvento!, i => i.EventoId == eventoId);
    }

    [Fact]
    public async Task DeleteProduto_ComPedidoExistenteReferenciandoOProduto_PermiteDeleteEPreservaOSnapshotDoPedido()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 10, preco: 79.90m);
        using var clienteJovem = await CriarClienteJovemAsync();

        var criarPedidoResponse = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(produtoId, "M", "Preto", 2)], "retirada", null));
        criarPedidoResponse.EnsureSuccessStatusCode();
        var pedidoCriado = await criarPedidoResponse.Content.ReadFromJsonAsync<PedidoDto>();
        var nomeOriginal = pedidoCriado!.Itens[0].Nome;
        var precoOriginal = pedidoCriado.Itens[0].PrecoUnitario;
        var fotoOriginal = pedidoCriado.Itens[0].FotoUrl;
        var valorTotalOriginal = pedidoCriado.ValorTotal;

        var deletarResponse = await clienteAdmin.DeleteAsync($"/produtos/{produtoId}");
        Assert.Equal(HttpStatusCode.NoContent, deletarResponse.StatusCode);

        var produtoDepoisResponse = await _client.GetAsync($"/produtos/{produtoId}");
        Assert.Equal(HttpStatusCode.NotFound, produtoDepoisResponse.StatusCode);

        var meusPedidosResponse = await clienteJovem.GetAsync("/usuarios/me/pedidos");
        meusPedidosResponse.EnsureSuccessStatusCode();
        var meusPedidos = await meusPedidosResponse.Content.ReadFromJsonAsync<List<PedidoDto>>();
        var pedidoAposDelete = meusPedidos!.Single(p => p.Id == pedidoCriado.Id);
        Assert.Equal(nomeOriginal, pedidoAposDelete.Itens[0].Nome);
        Assert.Equal(precoOriginal, pedidoAposDelete.Itens[0].PrecoUnitario);
        Assert.Equal(fotoOriginal, pedidoAposDelete.Itens[0].FotoUrl);
        Assert.Equal(valorTotalOriginal, pedidoAposDelete.ValorTotal);
    }
}
