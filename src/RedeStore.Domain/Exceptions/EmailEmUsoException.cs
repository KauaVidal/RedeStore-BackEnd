namespace RedeStore.Domain.Exceptions;

public sealed class EmailEmUsoException : DomainException
{
    public override string Codigo => "EMAIL_EM_USO";
    public override int StatusCode => 409;

    public EmailEmUsoException(string message) : base(message)
    {
    }
}
