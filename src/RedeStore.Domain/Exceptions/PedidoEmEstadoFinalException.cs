namespace RedeStore.Domain.Exceptions;

public sealed class PedidoEmEstadoFinalException : DomainException
{
    public override string Codigo => "PEDIDO_EM_ESTADO_FINAL";
    public override int StatusCode => 409;

    public PedidoEmEstadoFinalException(string message) : base(message)
    {
    }
}
