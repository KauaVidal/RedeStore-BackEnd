namespace RedeStore.Domain.Exceptions;

public abstract class DomainException : Exception
{
    public abstract string Codigo { get; }
    public abstract int StatusCode { get; }

    protected DomainException(string message) : base(message)
    {
    }
}
