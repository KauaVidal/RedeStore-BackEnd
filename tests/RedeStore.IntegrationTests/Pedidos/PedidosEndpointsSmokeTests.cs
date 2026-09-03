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
public class PedidosEndpointsSmokeTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public PedidosEndpointsSmokeTests(ApiFactory factory)
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
    public async Task PostPedidos_SemToken_Retorna401()
    {
        var response = await _client.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(Guid.NewGuid(), "M", "Preto", 1)], "retirada", null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostPedidos_ComItemValido_Retorna200EDepoisApareceEmMeusPedidos()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 10);
        using var clienteJovem = await CriarClienteJovemAsync();

        var criarResponse = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(produtoId, "M", "Preto", 2)], "retirada", null));
        Assert.Equal(HttpStatusCode.OK, criarResponse.StatusCode);

        var meusPedidosResponse = await clienteJovem.GetAsync("/usuarios/me/pedidos");
        meusPedidosResponse.EnsureSuccessStatusCode();
        var meusPedidos = await meusPedidosResponse.Content.ReadFromJsonAsync<List<PedidoDto>>();
        Assert.Single(meusPedidos!);
    }

    [Fact]
    public async Task PostPedidos_ComProdutoInexistente_Retorna404()
    {
        using var clienteJovem = await CriarClienteJovemAsync();

        var response = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(Guid.NewGuid(), "M", "Preto", 1)], "retirada", null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPedidos_ComTokenDeUsuarioNaoAdmin_Retorna403()
    {
        using var clienteJovem = await CriarClienteJovemAsync();

        var response = await clienteJovem.GetAsync("/pedidos");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
