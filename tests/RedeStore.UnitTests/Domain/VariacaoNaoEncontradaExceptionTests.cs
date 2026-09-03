using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class VariacaoNaoEncontradaExceptionTests
{
    [Fact]
    public void VariacaoNaoEncontradaException_TemCodigoEStatusCorretos()
    {
        var exception = new VariacaoNaoEncontradaException("variação não encontrada");

        Assert.Equal("VARIACAO_NAO_ENCONTRADA", exception.Codigo);
        Assert.Equal(404, exception.StatusCode);
    }
}
