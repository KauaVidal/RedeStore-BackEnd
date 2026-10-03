using FluentValidation;
using RedeStore.Application.Produtos.Dtos;

namespace RedeStore.Application.Produtos.Validators;

public sealed class AtualizarProdutoRequestValidator : AbstractValidator<AtualizarProdutoRequest>
{
    public AtualizarProdutoRequestValidator()
    {
        RuleFor(r => r.Nome).NotEmpty().When(r => r.Nome is not null);
        RuleFor(r => r.Categoria)
            .Must(CategoriasProduto.EhValida)
            .When(r => r.Categoria is not null)
            .WithMessage(CategoriasProduto.MensagemInvalida);
        RuleFor(r => r.Preco).GreaterThan(0).When(r => r.Preco.HasValue);
        RuleFor(r => r.Descricao).NotEmpty().When(r => r.Descricao is not null);
        RuleFor(r => r.Variacoes)
            .Must(v => v!.Count > 0)
            .When(r => r.Variacoes is not null)
            .WithMessage("O produto precisa de ao menos 1 variação.");
        RuleForEach(r => r.Variacoes).ChildRules(variacao =>
        {
            variacao.RuleFor(v => v.Tamanho).NotEmpty();
            variacao.RuleFor(v => v.Cor).NotEmpty();
            variacao.RuleFor(v => v.Estoque).GreaterThanOrEqualTo(0);
        }).When(r => r.Variacoes is not null);
    }
}
