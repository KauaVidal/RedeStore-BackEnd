namespace RedeStore.Domain.Exceptions;

public sealed class EventoComInscricoesConfirmadasException : DomainException
{
    public override string Codigo => "EVENTO_COM_INSCRICOES_CONFIRMADAS";
    public override int StatusCode => 409;

    public EventoComInscricoesConfirmadasException(string message) : base(message)
    {
    }
}
