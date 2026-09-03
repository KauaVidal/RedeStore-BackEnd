namespace RedeStore.Domain.Exceptions;

public sealed class EstoqueInsuficienteException : DomainException
{
    public override string Codigo => "ESTOQUE_INSUFICIENTE";
    public override int StatusCode => 409;

    public EstoqueInsuficienteException(string message) : base(message)
    {
    }
}
