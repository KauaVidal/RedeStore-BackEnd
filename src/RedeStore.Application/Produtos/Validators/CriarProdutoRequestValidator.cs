using FluentValidation;
using RedeStore.Application.Produtos.Dtos;

namespace RedeStore.Application.Produtos.Validators;

public sealed class CriarProdutoRequestValidator : AbstractValidator<CriarProdutoRequest>
{
    public CriarProdutoRequestValidator()
    {
        RuleFor(r => r.Nome).NotEmpty();
        RuleFor(r => r.Categoria)
            .Must(CategoriasProduto.EhValida)
            .WithMessage(CategoriasProduto.MensagemInvalida);
        RuleFor(r => r.Preco).GreaterThan(0);
        RuleFor(r => r.Descricao).NotEmpty();
        RuleFor(r => r.Variacoes)
            .Must(v => v is not null && v.Count > 0)
            .WithMessage("O produto precisa de ao menos 1 variação.");
        RuleForEach(r => r.Variacoes).ChildRules(variacao =>
        {
            variacao.RuleFor(v => v.Tamanho).NotEmpty();
            variacao.RuleFor(v => v.Cor).NotEmpty();
            variacao.RuleFor(v => v.Estoque).GreaterThanOrEqualTo(0);
        });
    }
}
