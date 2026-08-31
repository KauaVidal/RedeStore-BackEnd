using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class EventoComInscricoesConfirmadasExceptionTests
{
    [Fact]
    public void EventoComInscricoesConfirmadasException_TemCodigoEStatusCorretos()
    {
        var exception = new EventoComInscricoesConfirmadasException("evento tem inscrições confirmadas");

        Assert.Equal("EVENTO_COM_INSCRICOES_CONFIRMADAS", exception.Codigo);
        Assert.Equal(409, exception.StatusCode);
    }
}
