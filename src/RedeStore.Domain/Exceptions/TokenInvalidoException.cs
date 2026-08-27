namespace RedeStore.Domain.Exceptions;

public sealed class TokenInvalidoException : DomainException
{
    public override string Codigo => "TOKEN_INVALIDO";
    public override int StatusCode => 400;

    public TokenInvalidoException(string message) : base(message)
    {
    }
}
