using FluentValidation;
using RedeStore.Application.Eventos.Dtos;

namespace RedeStore.Application.Eventos.Validators;

public sealed class AtualizarEventoRequestValidator : AbstractValidator<AtualizarEventoRequest>
{
    public AtualizarEventoRequestValidator()
    {
        RuleFor(r => r.Titulo).NotEmpty().When(r => r.Titulo is not null);
        RuleFor(r => r.Descricao).NotEmpty().When(r => r.Descricao is not null);
        RuleFor(r => r.Local).NotEmpty().When(r => r.Local is not null);
        RuleFor(r => r.Foto).NotEmpty().When(r => r.Foto is not null);
        RuleFor(r => r.Preco).GreaterThanOrEqualTo(0).When(r => r.Preco.HasValue);
        RuleFor(r => r.VagasTotais).GreaterThanOrEqualTo(1).When(r => r.VagasTotais.HasValue);
    }
}
