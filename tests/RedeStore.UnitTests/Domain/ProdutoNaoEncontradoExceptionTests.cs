using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class ProdutoNaoEncontradoExceptionTests
{
    [Fact]
    public void ProdutoNaoEncontradoException_TemCodigoEStatusCorretos()
    {
        var exception = new ProdutoNaoEncontradoException("produto não encontrado");

        Assert.Equal("PRODUTO_NAO_ENCONTRADO", exception.Codigo);
        Assert.Equal(404, exception.StatusCode);
    }
}
