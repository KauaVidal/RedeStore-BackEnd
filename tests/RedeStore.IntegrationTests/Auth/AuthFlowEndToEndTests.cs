using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Auth;

[Collection(IntegrationTestCollection.Name)]
public class AuthFlowEndToEndTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public AuthFlowEndToEndTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task FluxoCompleto_CadastroLoginPerfilEsqueciSenhaRedefinir_FuncionaPontaAPonta()
    {
        var email = $"{Guid.NewGuid()}@teste.com";
        var senhaOriginal = "senhaOriginal123";

        var cadastroResponse = await _client.PostAsJsonAsync("/auth/cadastro",
            new CadastroRequest("Fulano de Tal", email, senhaOriginal));
        cadastroResponse.EnsureSuccessStatusCode();

        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, senhaOriginal));
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        using var clienteAutenticado = _factory.CreateClient();
        clienteAutenticado.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);

        var meResponse = await clienteAutenticado.GetAsync("/auth/me");
        meResponse.EnsureSuccessStatusCode();
        var me = await meResponse.Content.ReadFromJsonAsync<UsuarioDto>();
        Assert.Equal(email, me!.Email);

        var perfilResponse = await clienteAutenticado.PatchAsJsonAsync("/auth/perfil",
            new AtualizarPerfilRequest("Nome Atualizado", null, null));
        perfilResponse.EnsureSuccessStatusCode();
        var perfil = await perfilResponse.Content.ReadFromJsonAsync<UsuarioDto>();
        Assert.Equal("Nome Atualizado", perfil!.Nome);

        var usuarioProprioResponse = await clienteAutenticado.GetAsync($"/usuarios/{me.Id}");
        usuarioProprioResponse.EnsureSuccessStatusCode();

        var outroId = Guid.NewGuid();
        var usuarioOutroResponse = await clienteAutenticado.GetAsync($"/usuarios/{outroId}");
        Assert.Equal(HttpStatusCode.Forbidden, usuarioOutroResponse.StatusCode);

        var recuperarResponse = await _client.PostAsJsonAsync("/auth/recuperar-senha", new RecuperarSenhaRequest(email));
        Assert.Equal(HttpStatusCode.NoContent, recuperarResponse.StatusCode);

        var emailEnviado = Assert.Single(_factory.EmailSender.Enviados, e => e.Destinatario == email);
        var token = ExtrairTokenDoLink(emailEnviado.CorpoHtml);

        var redefinirResponse = await _client.PostAsJsonAsync("/auth/redefinir-senha",
            new RedefinirSenhaRequest(token, "senhaNova123"));
        Assert.Equal(HttpStatusCode.NoContent, redefinirResponse.StatusCode);

        var novoLoginResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "senhaNova123"));
        novoLoginResponse.EnsureSuccessStatusCode();

        var loginAntigoResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, senhaOriginal));
        Assert.Equal(HttpStatusCode.Unauthorized, loginAntigoResponse.StatusCode);

        var redefinirDeNovoResponse = await _client.PostAsJsonAsync("/auth/redefinir-senha",
            new RedefinirSenhaRequest(token, "outraSenha123"));
        Assert.Equal(HttpStatusCode.BadRequest, redefinirDeNovoResponse.StatusCode);
    }

    [Fact]
    public async Task PostCadastro_ComEmailJaCadastrado_Retorna409()
    {
        var email = $"{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Fulano", email, "senha12345"));

        var response = await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Fulano", email, "senha12345"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostCadastro_ComMesmoEmailEmOutraGrafia_Retorna409()
    {
        var id = Guid.NewGuid();
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Fulano", $"{id}@teste.com", "senha12345"));

        var response = await _client.PostAsJsonAsync(
            "/auth/cadastro", new CadastroRequest("Fulano", $"  {id.ToString().ToUpperInvariant()}@TESTE.com ", "senha12345"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostCadastro_SimultaneosComMesmoEmail_CriaUmaContaEORestoRecebe409()
    {
        var email = $"{Guid.NewGuid()}@teste.com";

        var respostas = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            _factory.CreateClient().PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Fulano", email, "senha12345"))));

        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(respostas.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task GetUsuarioPorId_ComoAdmin_PermiteVerOutroUsuario()
    {
        var emailAlvo = $"{Guid.NewGuid()}@teste.com";
        var cadastroAlvoResponse = await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Alvo", emailAlvo, "senha12345"));
        var alvo = await cadastroAlvoResponse.Content.ReadFromJsonAsync<AuthResponse>();

        var emailAdmin = $"{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Admin", emailAdmin, "senha12345"));

        using (var scope = _factory.Services.CreateScope())
        {
            var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
            var admin = await usuarioRepositorio.BuscarPorEmailAsync(emailAdmin, CancellationToken.None);
            admin!.Papel = Papel.Admin;
            await usuarioRepositorio.AtualizarAsync(admin, CancellationToken.None);
        }

        var loginAdminResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(emailAdmin, "senha12345"));
        var loginAdmin = await loginAdminResponse.Content.ReadFromJsonAsync<AuthResponse>();

        using var clienteAdmin = _factory.CreateClient();
        clienteAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginAdmin!.Token);

        var response = await clienteAdmin.GetAsync($"/usuarios/{alvo!.Usuario.Id}");

        response.EnsureSuccessStatusCode();
    }

    private static string ExtrairTokenDoLink(string corpoHtml)
    {
        var inicio = corpoHtml.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var fim = corpoHtml.IndexOf('"', inicio);
        return corpoHtml[inicio..fim];
    }
}
