using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Application.Produtos.Dtos;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Produtos;

[Collection(IntegrationTestCollection.Name)]
public class ProdutosEndpointsSmokeTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ProdutosEndpointsSmokeTests(ApiFactory factory)
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

    private static CriarProdutoRequest RequestValido() => new(
        Nome: $"Camiseta {Guid.NewGuid()}",
        Categoria: "camisetas",
        Preco: 79.90m,
        Descricao: "Camiseta oficial",
        Fotos: ["https://exemplo.com/foto.jpg"],
        Destaque: false,
        Variacoes: [new VariacaoRequest("M", "Preto", 10)]);

    [Fact]
    public async Task PostProdutos_SemToken_Retorna401()
    {
        var response = await _client.PostAsJsonAsync("/produtos", RequestValido());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostProdutos_ComTokenDeAdmin_Retorna200EDepoisApareceNoDetalhe()
    {
        var token = await ObterTokenAdminAsync();
        using var clienteAdmin = _factory.CreateClient();
        clienteAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var criarResponse = await clienteAdmin.PostAsJsonAsync("/produtos", RequestValido());
        Assert.Equal(HttpStatusCode.OK, criarResponse.StatusCode);
        var criado = await criarResponse.Content.ReadFromJsonAsync<ProdutoDto>();

        var detalheResponse = await _client.GetAsync($"/produtos/{criado!.Id}");
        Assert.Equal(HttpStatusCode.OK, detalheResponse.StatusCode);
    }

    [Fact]
    public async Task PostProdutos_SemVariacoes_Retorna400()
    {
        var token = await ObterTokenAdminAsync();
        using var clienteAdmin = _factory.CreateClient();
        clienteAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var request = RequestValido() with { Variacoes = [] };

        var response = await clienteAdmin.PostAsJsonAsync("/produtos", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetProdutoPorId_ComIdInexistente_Retorna404()
    {
        var response = await _client.GetAsync($"/produtos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
