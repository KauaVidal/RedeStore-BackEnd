namespace RedeStore.Domain.Exceptions;

public sealed class AcessoNegadoException : DomainException
{
    public override string Codigo => "ACESSO_NEGADO";
    public override int StatusCode => 403;

    public AcessoNegadoException(string message) : base(message)
    {
    }
}
