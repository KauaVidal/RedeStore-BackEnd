using FluentValidation;
using RedeStore.Application.Eventos.Dtos;

namespace RedeStore.Application.Eventos.Validators;

public sealed class CriarEventoRequestValidator : AbstractValidator<CriarEventoRequest>
{
    public CriarEventoRequestValidator()
    {
        RuleFor(r => r.Titulo).NotEmpty();
        RuleFor(r => r.Descricao).NotEmpty();
        RuleFor(r => r.Local).NotEmpty();
        RuleFor(r => r.Foto).NotEmpty();
        RuleFor(r => r.Preco).GreaterThanOrEqualTo(0);
        RuleFor(r => r.VagasTotais).GreaterThanOrEqualTo(1);
    }
}
