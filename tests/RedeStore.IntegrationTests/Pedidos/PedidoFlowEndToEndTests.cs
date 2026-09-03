using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Application.Pedidos.Dtos;
using RedeStore.Application.Produtos.Dtos;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Pedidos;

[Collection(IntegrationTestCollection.Name)]
public class PedidoFlowEndToEndTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public PedidoFlowEndToEndTests(ApiFactory factory)
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

    private async Task<Guid> CriarProdutoComEstoqueAsync(HttpClient clienteAdmin, int estoque)
    {
        var response = await clienteAdmin.PostAsJsonAsync("/produtos", new CriarProdutoRequest(
            Nome: $"Camiseta {Guid.NewGuid()}",
            Categoria: "camisetas",
            Preco: 79.90m,
            Descricao: "Camiseta de teste",
            Fotos: ["https://exemplo.com/foto.jpg"],
            Destaque: false,
            Variacoes: [new VariacaoRequest("M", "Preto", estoque)]));
        var produto = await response.Content.ReadFromJsonAsync<ProdutoDto>();
        return produto!.Id;
    }

    [Fact]
    public async Task FluxoCompleto_CriarListarAvancarStatusAteEstadoFinal_FuncionaPontaAPonta()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 10);
        using var clienteJovem = await CriarClienteJovemAsync();

        var criarResponse = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(produtoId, "M", "Preto", 2)], "retirada", null));
        criarResponse.EnsureSuccessStatusCode();
        var criado = await criarResponse.Content.ReadFromJsonAsync<PedidoDto>();
        Assert.Equal("pago", criado!.Status);
        Assert.Equal(159.80m, criado.ValorTotal);

        var meusPedidosResponse = await clienteJovem.GetAsync("/usuarios/me/pedidos");
        var meusPedidos = await meusPedidosResponse.Content.ReadFromJsonAsync<List<PedidoDto>>();
        Assert.Contains(meusPedidos!, p => p.Id == criado.Id);

        var todosOsPedidosResponse = await clienteAdmin.GetAsync("/pedidos");
        var todosOsPedidos = await todosOsPedidosResponse.Content.ReadFromJsonAsync<List<PedidoDto>>();
        Assert.Contains(todosOsPedidos!, p => p.Id == criado.Id);

        var avancarUmResponse = await clienteAdmin.PatchAsync($"/pedidos/{criado.Id}/avancar-status", content: null);
        avancarUmResponse.EnsureSuccessStatusCode();
        var apósPrimeiroAvanco = await avancarUmResponse.Content.ReadFromJsonAsync<PedidoDto>();
        Assert.Equal("em_preparo", apósPrimeiroAvanco!.Status);

        var avancarDoisResponse = await clienteAdmin.PatchAsync($"/pedidos/{criado.Id}/avancar-status", content: null);
        avancarDoisResponse.EnsureSuccessStatusCode();
        var apósSegundoAvanco = await avancarDoisResponse.Content.ReadFromJsonAsync<PedidoDto>();
        Assert.Equal("retirado", apósSegundoAvanco!.Status);

        var avancarTresResponse = await clienteAdmin.PatchAsync($"/pedidos/{criado.Id}/avancar-status", content: null);
        Assert.Equal(HttpStatusCode.Conflict, avancarTresResponse.StatusCode);
    }

    [Fact]
    public async Task PatchAvancarStatus_ComTokenDeUsuarioNaoAdmin_Retorna403()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 10);
        using var clienteJovem = await CriarClienteJovemAsync();
        var criarResponse = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(produtoId, "M", "Preto", 1)], "retirada", null));
        var criado = await criarResponse.Content.ReadFromJsonAsync<PedidoDto>();

        var response = await clienteJovem.PatchAsync($"/pedidos/{criado!.Id}/avancar-status", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostPedidos_ComEntregaSemEndereco_Retorna400()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 10);
        using var clienteJovem = await CriarClienteJovemAsync();

        var response = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(produtoId, "M", "Preto", 1)], "entrega", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CriarPedido_ComEstoqueInsuficienteEmUmDosItens_RejeitaENaoDecrementaNenhumItem()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoComEstoqueId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 10);
        var produtoSemEstoqueId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 1);
        using var clienteJovem = await CriarClienteJovemAsync();

        var response = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [
                new ItemPedidoRequest(produtoComEstoqueId, "M", "Preto", 2),
                new ItemPedidoRequest(produtoSemEstoqueId, "M", "Preto", 5),
            ],
            "retirada", null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var produtoResponse = await _client.GetAsync($"/produtos/{produtoComEstoqueId}");
        var produto = await produtoResponse.Content.ReadFromJsonAsync<ProdutoDto>();
        Assert.Equal(10, produto!.Variacoes[0].Estoque);
    }

    [Fact]
    public async Task DoisCheckoutsSimultaneosNaUltimaUnidadeDeEstoque_ApenasUmSucede()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 1);
        using var primeiroUsuario = await CriarClienteJovemAsync();
        using var segundoUsuario = await CriarClienteJovemAsync();
        var request = new CriarPedidoRequest([new ItemPedidoRequest(produtoId, "M", "Preto", 1)], "retirada", null);

        var tarefaUm = primeiroUsuario.PostAsJsonAsync("/pedidos", request);
        var tarefaDois = segundoUsuario.PostAsJsonAsync("/pedidos", request);
        await Task.WhenAll(tarefaUm, tarefaDois);

        var statusCodes = new[] { (await tarefaUm).StatusCode, (await tarefaDois).StatusCode };
        Assert.Single(statusCodes, s => s == HttpStatusCode.OK);
        Assert.Single(statusCodes, s => s == HttpStatusCode.Conflict);

        var produtoResponse = await _client.GetAsync($"/produtos/{produtoId}");
        var produto = await produtoResponse.Content.ReadFromJsonAsync<ProdutoDto>();
        Assert.Equal(0, produto!.Variacoes[0].Estoque);
    }
}
