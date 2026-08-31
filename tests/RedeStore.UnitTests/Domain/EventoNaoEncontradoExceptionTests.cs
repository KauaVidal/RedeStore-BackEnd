using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class EventoNaoEncontradoExceptionTests
{
    [Fact]
    public void EventoNaoEncontradoException_TemCodigoEStatusCorretos()
    {
        var exception = new EventoNaoEncontradoException("evento não encontrado");

        Assert.Equal("EVENTO_NAO_ENCONTRADO", exception.Codigo);
        Assert.Equal(404, exception.StatusCode);
    }
}
