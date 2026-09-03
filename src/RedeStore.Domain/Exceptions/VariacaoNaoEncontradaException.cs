namespace RedeStore.Domain.Exceptions;

public sealed class VariacaoNaoEncontradaException : DomainException
{
    public override string Codigo => "VARIACAO_NAO_ENCONTRADA";
    public override int StatusCode => 404;

    public VariacaoNaoEncontradaException(string message) : base(message)
    {
    }
}
