namespace RedeStore.Domain.Exceptions;

public sealed class EventoNaoEncontradoException : DomainException
{
    public override string Codigo => "EVENTO_NAO_ENCONTRADO";
    public override int StatusCode => 404;

    public EventoNaoEncontradoException(string message) : base(message)
    {
    }
}
