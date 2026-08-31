namespace RedeStore.Domain.Exceptions;

public sealed class EventoVagasTotaisInsuficientesException : DomainException
{
    public override string Codigo => "EVENTO_VAGAS_TOTAIS_INSUFICIENTES";
    public override int StatusCode => 409;

    public EventoVagasTotaisInsuficientesException(string message) : base(message)
    {
    }
}
