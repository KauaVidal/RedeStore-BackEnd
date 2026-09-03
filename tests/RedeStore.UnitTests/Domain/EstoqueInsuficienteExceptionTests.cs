using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class EstoqueInsuficienteExceptionTests
{
    [Fact]
    public void EstoqueInsuficienteException_TemCodigoEStatusCorretos()
    {
        var exception = new EstoqueInsuficienteException("estoque insuficiente");

        Assert.Equal("ESTOQUE_INSUFICIENTE", exception.Codigo);
        Assert.Equal(409, exception.StatusCode);
    }
}
