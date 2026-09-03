namespace RedeStore.Domain.Exceptions;

public sealed class PedidoNaoEncontradoException : DomainException
{
    public override string Codigo => "PEDIDO_NAO_ENCONTRADO";
    public override int StatusCode => 404;

    public PedidoNaoEncontradoException(string message) : base(message)
    {
    }
}
