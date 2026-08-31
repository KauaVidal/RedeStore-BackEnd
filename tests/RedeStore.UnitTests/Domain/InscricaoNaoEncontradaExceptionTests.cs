using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class InscricaoNaoEncontradaExceptionTests
{
    [Fact]
    public void InscricaoNaoEncontradaException_TemCodigoEStatusCorretos()
    {
        var exception = new InscricaoNaoEncontradaException("inscrição não encontrada");

        Assert.Equal("INSCRICAO_NAO_ENCONTRADA", exception.Codigo);
        Assert.Equal(404, exception.StatusCode);
    }
}
