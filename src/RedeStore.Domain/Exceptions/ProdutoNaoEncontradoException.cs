namespace RedeStore.Domain.Exceptions;

public sealed class ProdutoNaoEncontradoException : DomainException
{
    public override string Codigo => "PRODUTO_NAO_ENCONTRADO";
    public override int StatusCode => 404;

    public ProdutoNaoEncontradoException(string message) : base(message)
    {
    }
}
