using RedeStore.Application.Auth;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Domain.Exceptions;
using RedeStore.Infrastructure.Auth;
using Xunit;

namespace RedeStore.UnitTests.Auth;

public class AuthServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        SigningKey = "chave-de-teste-com-pelo-menos-32-caracteres",
        Issuer = "RedeStore.Tests",
        Audience = "RedeStore.Tests.Clients",
    };

    private readonly FakeUsuarioRepository _repositorio = new();
    private readonly FakePasswordResetTokenRepository _tokenRepositorio = new();
    private readonly FakeEmailSender _emailSender = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _sut = new AuthService(
            _repositorio,
            _tokenRepositorio,
            new PasswordHasher(),
            new JwtTokenGenerator(Microsoft.Extensions.Options.Options.Create(Options)),
            _emailSender,
            Microsoft.Extensions.Options.Options.Create(new FrontendOptions { ResetPasswordUrl = "http://localhost:4200/redefinir-senha" }));
    }

    [Fact]
    public async Task CadastrarAsync_ComEmailNovo_CriaUsuarioComPapelJovem()
    {
        var request = new CadastroRequest("Fulano", "fulano@teste.com", "senha12345");

        var resposta = await _sut.CadastrarAsync(request, CancellationToken.None);

        Assert.Equal("jovem", resposta.Usuario.Papel);
        Assert.Equal("fulano@teste.com", resposta.Usuario.Email);
        Assert.NotEmpty(resposta.Token);
    }

    [Fact]
    public async Task CadastrarAsync_ComEmailJaExistente_LancaEmailEmUsoException()
    {
        var request = new CadastroRequest("Fulano", "fulano@teste.com", "senha12345");
        await _sut.CadastrarAsync(request, CancellationToken.None);

        await Assert.ThrowsAsync<EmailEmUsoException>(() => _sut.CadastrarAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_ComCredenciaisCorretas_RetornaToken()
    {
        await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano2@teste.com", "senha12345"), CancellationToken.None);

        var resposta = await _sut.LoginAsync(new LoginRequest("fulano2@teste.com", "senha12345"), CancellationToken.None);

        Assert.NotEmpty(resposta.Token);
    }

    [Fact]
    public async Task LoginAsync_ComSenhaErrada_LancaCredenciaisInvalidasException()
    {
        await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano3@teste.com", "senha12345"), CancellationToken.None);

        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() =>
            _sut.LoginAsync(new LoginRequest("fulano3@teste.com", "senhaErrada"), CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_ComEmailInexistente_LancaCredenciaisInvalidasException()
    {
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() =>
            _sut.LoginAsync(new LoginRequest("naoexiste@teste.com", "qualquercoisa"), CancellationToken.None));
    }

    [Fact]
    public async Task ObterComAutorizacaoAsync_ComIdDiferenteENaoAdmin_LancaAcessoNegadoException()
    {
        var cadastro = await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano4@teste.com", "senha12345"), CancellationToken.None);
        var outroId = Guid.NewGuid();

        await Assert.ThrowsAsync<AcessoNegadoException>(() =>
            _sut.ObterComAutorizacaoAsync(cadastro.Usuario.Id, outroId, ehAdmin: false, CancellationToken.None));
    }

    [Fact]
    public async Task ObterComAutorizacaoAsync_ComAdmin_RetornaUsuarioMesmoSemSerODono()
    {
        var cadastro = await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano5@teste.com", "senha12345"), CancellationToken.None);
        var idAdmin = Guid.NewGuid();

        var resultado = await _sut.ObterComAutorizacaoAsync(cadastro.Usuario.Id, idAdmin, ehAdmin: true, CancellationToken.None);

        Assert.Equal(cadastro.Usuario.Id, resultado.Id);
    }

    [Fact]
    public async Task AtualizarPerfilAsync_ComNovoEmailJaEmUso_LancaEmailEmUsoException()
    {
        await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano6@teste.com", "senha12345"), CancellationToken.None);
        var cadastro2 = await _sut.CadastrarAsync(new CadastroRequest("Ciclano", "ciclano6@teste.com", "senha12345"), CancellationToken.None);

        await Assert.ThrowsAsync<EmailEmUsoException>(() =>
            _sut.AtualizarPerfilAsync(cadastro2.Usuario.Id, new AtualizarPerfilRequest(null, "fulano6@teste.com", null), CancellationToken.None));
    }

    [Fact]
    public async Task AtualizarPerfilAsync_ComNomeNovo_AtualizaEPersisteNoRepositorio()
    {
        var cadastro = await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano7@teste.com", "senha12345"), CancellationToken.None);

        var atualizado = await _sut.AtualizarPerfilAsync(cadastro.Usuario.Id, new AtualizarPerfilRequest("Novo Nome", null, null), CancellationToken.None);

        Assert.Equal("Novo Nome", atualizado.Nome);
    }

    [Fact]
    public async Task RecuperarSenhaAsync_ComEmailExistente_EnviaEmailComLink()
    {
        await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano8@teste.com", "senha12345"), CancellationToken.None);

        await _sut.RecuperarSenhaAsync("fulano8@teste.com", CancellationToken.None);

        Assert.Single(_emailSender.Enviados);
        Assert.Equal("fulano8@teste.com", _emailSender.Enviados[0].Destinatario);
        Assert.Contains("token=", _emailSender.Enviados[0].CorpoHtml);
    }

    [Fact]
    public async Task RecuperarSenhaAsync_ComEmailInexistente_NaoEnviaEmailENaoLancaExcecao()
    {
        await _sut.RecuperarSenhaAsync("naoexiste@teste.com", CancellationToken.None);

        Assert.Empty(_emailSender.Enviados);
    }

    [Fact]
    public async Task RedefinirSenhaAsync_ComTokenValido_AtualizaSenhaEPermiteLoginComNovaSenha()
    {
        await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano9@teste.com", "senha12345"), CancellationToken.None);
        await _sut.RecuperarSenhaAsync("fulano9@teste.com", CancellationToken.None);
        var token = ExtrairTokenDoLink(_emailSender.Enviados[0].CorpoHtml);

        await _sut.RedefinirSenhaAsync(token, "senhaNova123", CancellationToken.None);
        var resposta = await _sut.LoginAsync(new LoginRequest("fulano9@teste.com", "senhaNova123"), CancellationToken.None);

        Assert.NotEmpty(resposta.Token);
    }

    [Fact]
    public async Task RedefinirSenhaAsync_ComTokenUsadoDuasVezes_LancaTokenInvalidoNaSegundaVez()
    {
        await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano10@teste.com", "senha12345"), CancellationToken.None);
        await _sut.RecuperarSenhaAsync("fulano10@teste.com", CancellationToken.None);
        var token = ExtrairTokenDoLink(_emailSender.Enviados[0].CorpoHtml);
        await _sut.RedefinirSenhaAsync(token, "senhaNova123", CancellationToken.None);

        await Assert.ThrowsAsync<TokenInvalidoException>(() =>
            _sut.RedefinirSenhaAsync(token, "outraSenha123", CancellationToken.None));
    }

    [Fact]
    public async Task RedefinirSenhaAsync_ComTokenInexistente_LancaTokenInvalidoException()
    {
        await Assert.ThrowsAsync<TokenInvalidoException>(() =>
            _sut.RedefinirSenhaAsync("token-que-nao-existe", "outraSenha123", CancellationToken.None));
    }

    private static string ExtrairTokenDoLink(string corpoHtml)
    {
        var inicio = corpoHtml.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var fim = corpoHtml.IndexOf('"', inicio);
        return corpoHtml[inicio..fim];
    }
}
