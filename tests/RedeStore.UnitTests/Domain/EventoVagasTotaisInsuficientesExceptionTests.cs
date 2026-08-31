using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class EventoVagasTotaisInsuficientesExceptionTests
{
    [Fact]
    public void EventoVagasTotaisInsuficientesException_TemCodigoEStatusCorretos()
    {
        var exception = new EventoVagasTotaisInsuficientesException("vagasTotais insuficiente para as inscrições confirmadas");

        Assert.Equal("EVENTO_VAGAS_TOTAIS_INSUFICIENTES", exception.Codigo);
        Assert.Equal(409, exception.StatusCode);
    }
}
