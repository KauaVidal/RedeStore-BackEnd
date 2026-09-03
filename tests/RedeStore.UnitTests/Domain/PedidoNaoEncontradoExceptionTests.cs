using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class PedidoNaoEncontradoExceptionTests
{
    [Fact]
    public void PedidoNaoEncontradoException_TemCodigoEStatusCorretos()
    {
        var exception = new PedidoNaoEncontradoException("pedido não encontrado");

        Assert.Equal("PEDIDO_NAO_ENCONTRADO", exception.Codigo);
        Assert.Equal(404, exception.StatusCode);
    }
}
