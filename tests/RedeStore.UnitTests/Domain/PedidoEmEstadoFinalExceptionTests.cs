using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class PedidoEmEstadoFinalExceptionTests
{
    [Fact]
    public void PedidoEmEstadoFinalException_TemCodigoEStatusCorretos()
    {
        var exception = new PedidoEmEstadoFinalException("pedido em estado final");

        Assert.Equal("PEDIDO_EM_ESTADO_FINAL", exception.Codigo);
        Assert.Equal(409, exception.StatusCode);
    }
}
