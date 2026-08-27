using FluentValidation;
using RedeStore.Application.Produtos.Dtos;

namespace RedeStore.Application.Produtos.Validators;

public sealed class AtualizarProdutoRequestValidator : AbstractValidator<AtualizarProdutoRequest>
{
    private static readonly string[] CategoriasValidas = ["camisetas", "moletons", "acessorios"];

    public AtualizarProdutoRequestValidator()
    {
        RuleFor(r => r.Nome).NotEmpty().When(r => r.Nome is not null);
        RuleFor(r => r.Categoria)
            .Must(c => CategoriasValidas.Contains(c))
            .When(r => r.Categoria is not null)
            .WithMessage("Categoria deve ser 'camisetas', 'moletons' ou 'acessorios'.");
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
