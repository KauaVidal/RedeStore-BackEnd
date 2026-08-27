using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class DomainExceptionsTests
{
    [Fact]
    public void EmailEmUsoException_TemCodigoEStatusCorretos()
    {
        var exception = new EmailEmUsoException("e-mail já cadastrado");
        Assert.Equal("EMAIL_EM_USO", exception.Codigo);
        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public void CredenciaisInvalidasException_TemCodigoEStatusCorretos()
    {
        var exception = new CredenciaisInvalidasException("credenciais inválidas");
        Assert.Equal("CREDENCIAIS_INVALIDAS", exception.Codigo);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public void TokenInvalidoException_TemCodigoEStatusCorretos()
    {
        var exception = new TokenInvalidoException("token inválido");
        Assert.Equal("TOKEN_INVALIDO", exception.Codigo);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public void AcessoNegadoException_TemCodigoEStatusCorretos()
    {
        var exception = new AcessoNegadoException("acesso negado");
        Assert.Equal("ACESSO_NEGADO", exception.Codigo);
        Assert.Equal(403, exception.StatusCode);
    }
}
