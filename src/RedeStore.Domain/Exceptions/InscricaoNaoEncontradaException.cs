namespace RedeStore.Domain.Exceptions;

public sealed class InscricaoNaoEncontradaException : DomainException
{
    public override string Codigo => "INSCRICAO_NAO_ENCONTRADA";
    public override int StatusCode => 404;

    public InscricaoNaoEncontradaException(string message) : base(message)
    {
    }
}
