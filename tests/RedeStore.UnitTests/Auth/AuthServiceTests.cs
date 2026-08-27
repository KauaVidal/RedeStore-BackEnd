using RedeStore.Application.Auth;
using RedeStore.Application.Auth.Dtos;
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
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _sut = new AuthService(
            _repositorio,
            new PasswordHasher(),
            new JwtTokenGenerator(Microsoft.Extensions.Options.Options.Create(Options)));
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
}
