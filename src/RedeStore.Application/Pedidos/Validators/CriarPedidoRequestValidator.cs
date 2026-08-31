using FluentValidation;
using RedeStore.Application.Pedidos.Dtos;

namespace RedeStore.Application.Pedidos.Validators;

public sealed class CriarPedidoRequestValidator : AbstractValidator<CriarPedidoRequest>
{
    private static readonly string[] FormasEntregaValidas = ["retirada", "entrega"];

    public CriarPedidoRequestValidator()
    {
        RuleFor(r => r.Itens)
            .Must(itens => itens is not null && itens.Count > 0)
            .WithMessage("O pedido precisa de ao menos 1 item.");
        RuleForEach(r => r.Itens).ChildRules(item =>
        {
            item.RuleFor(i => i.ProdutoId).NotEmpty();
            item.RuleFor(i => i.Tamanho).NotEmpty();
            item.RuleFor(i => i.Cor).NotEmpty();
            item.RuleFor(i => i.Quantidade).GreaterThan(0);
        });
        RuleFor(r => r.FormaEntrega)
            .Must(f => FormasEntregaValidas.Contains(f))
            .WithMessage("FormaEntrega deve ser 'retirada' ou 'entrega'.");
        RuleFor(r => r.Endereco)
            .NotNull()
            .When(r => r.FormaEntrega == "entrega")
            .WithMessage("Endereco é obrigatório quando formaEntrega é 'entrega'.");
        When(r => r.Endereco is not null, () =>
        {
            RuleFor(r => r.Endereco!.Rua).NotEmpty();
            RuleFor(r => r.Endereco!.Numero).NotEmpty();
            RuleFor(r => r.Endereco!.Bairro).NotEmpty();
            RuleFor(r => r.Endereco!.Cidade).NotEmpty();
            RuleFor(r => r.Endereco!.Cep).NotEmpty();
        });
    }
}
