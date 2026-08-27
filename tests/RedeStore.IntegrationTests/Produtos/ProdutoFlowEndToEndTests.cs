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
public class ProdutoFlowEndToEndTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ProdutoFlowEndToEndTests(ApiFactory factory)
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

    [Fact]
    public async Task FluxoCompleto_CriarListarDetalharAtualizarDeletar_FuncionaPontaAPonta()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var nomeUnico = $"Camiseta {Guid.NewGuid()}";

        var criarResponse = await clienteAdmin.PostAsJsonAsync("/produtos", new CriarProdutoRequest(
            Nome: nomeUnico,
            Categoria: "camisetas",
            Preco: 79.90m,
            Descricao: "Camiseta oficial da Rede",
            Fotos: ["https://exemplo.com/foto.jpg"],
            Destaque: true,
            Variacoes: [new VariacaoRequest("M", "Preto", 10), new VariacaoRequest("G", "Branco", 5)]));
        criarResponse.EnsureSuccessStatusCode();
        var criado = await criarResponse.Content.ReadFromJsonAsync<ProdutoDto>();
        Assert.Equal(["M", "G"], criado!.Tamanhos);
        Assert.Equal(["Preto", "Branco"], criado.Cores);

        var listaResponse = await _client.GetAsync($"/produtos?busca={Uri.EscapeDataString(nomeUnico)}");
        listaResponse.EnsureSuccessStatusCode();
        var lista = await listaResponse.Content.ReadFromJsonAsync<List<ProdutoDto>>();
        Assert.Contains(lista!, p => p.Id == criado.Id);

        var destaquesResponse = await _client.GetAsync("/produtos/destaques");
        destaquesResponse.EnsureSuccessStatusCode();
        var destaques = await destaquesResponse.Content.ReadFromJsonAsync<List<ProdutoDto>>();
        Assert.Contains(destaques!, p => p.Id == criado.Id);

        var detalheResponse = await _client.GetAsync($"/produtos/{criado.Id}");
        detalheResponse.EnsureSuccessStatusCode();
        var detalhe = await detalheResponse.Content.ReadFromJsonAsync<ProdutoDto>();
        Assert.Equal(2, detalhe!.Variacoes.Count);

        var atualizarResponse = await clienteAdmin.PatchAsJsonAsync($"/produtos/{criado.Id}",
            new AtualizarProdutoRequest(null, null, null, null, null, null, [new VariacaoRequest("U", "Azul", 3)]));
        atualizarResponse.EnsureSuccessStatusCode();
        var atualizado = await atualizarResponse.Content.ReadFromJsonAsync<ProdutoDto>();
        Assert.Equal(["U"], atualizado!.Tamanhos);
        Assert.Equal(["Azul"], atualizado.Cores);
        Assert.Single(atualizado.Variacoes);

        var detalheAposAtualizarResponse = await _client.GetAsync($"/produtos/{criado.Id}");
        detalheAposAtualizarResponse.EnsureSuccessStatusCode();
        var detalheAposAtualizar = await detalheAposAtualizarResponse.Content.ReadFromJsonAsync<ProdutoDto>();
        Assert.Equal(["U"], detalheAposAtualizar!.Tamanhos);
        Assert.Equal(["Azul"], detalheAposAtualizar.Cores);
        Assert.Single(detalheAposAtualizar.Variacoes);

        var deletarResponse = await clienteAdmin.DeleteAsync($"/produtos/{criado.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deletarResponse.StatusCode);

        var depoisDeDeletarResponse = await _client.GetAsync($"/produtos/{criado.Id}");
        Assert.Equal(HttpStatusCode.NotFound, depoisDeDeletarResponse.StatusCode);
    }

    [Fact]
    public async Task PatchProdutos_SemTokenDeAdmin_Retorna401()
    {
        var response = await _client.PatchAsJsonAsync($"/produtos/{Guid.NewGuid()}",
            new AtualizarProdutoRequest("Novo Nome", null, null, null, null, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteProdutos_SemTokenDeAdmin_Retorna401()
    {
        var response = await _client.DeleteAsync($"/produtos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostProdutos_ComTokenDeUsuarioNaoAdmin_Retorna403()
    {
        var email = $"{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Jovem", email, "senha12345"));

        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "senha12345"));
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        using var clienteNaoAdmin = _factory.CreateClient();
        clienteNaoAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);

        var response = await clienteNaoAdmin.PostAsJsonAsync("/produtos", new CriarProdutoRequest(
            Nome: $"Camiseta {Guid.NewGuid()}",
            Categoria: "camisetas",
            Preco: 79.90m,
            Descricao: "Camiseta oficial da Rede",
            Fotos: ["https://exemplo.com/foto.jpg"],
            Destaque: true,
            Variacoes: [new VariacaoRequest("M", "Preto", 10)]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetProdutos_ComFiltroDeCategoria_RetornaSoDaCategoria()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var nomeMoletom = $"Moletom {Guid.NewGuid()}";
        await clienteAdmin.PostAsJsonAsync("/produtos", new CriarProdutoRequest(
            Nome: nomeMoletom,
            Categoria: "moletons",
            Preco: 149.90m,
            Descricao: "Moletom oficial",
            Fotos: [],
            Destaque: false,
            Variacoes: [new VariacaoRequest("G", "Cinza", 5)]));

        var response = await _client.GetAsync("/produtos?categoria=moletons");
        response.EnsureSuccessStatusCode();
        var lista = await response.Content.ReadFromJsonAsync<List<ProdutoDto>>();

        Assert.Contains(lista!, p => p.Nome == nomeMoletom);
        Assert.All(lista!, p => Assert.Equal("moletons", p.Categoria));
    }
}
