namespace RedeStore.Domain.Exceptions;

public sealed class CredenciaisInvalidasException : DomainException
{
    public override string Codigo => "CREDENCIAIS_INVALIDAS";
    public override int StatusCode => 401;

    public CredenciaisInvalidasException(string message) : base(message)
    {
    }
}
