using FluentValidation;
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Application.Auth.Validators;

public sealed class AtualizarPerfilRequestValidator : AbstractValidator<AtualizarPerfilRequest>
{
    public AtualizarPerfilRequestValidator()
    {
        RuleFor(r => r.Nome).MinimumLength(2).When(r => r.Nome is not null);
        RuleFor(r => r.Email).EmailAddress().When(r => r.Email is not null);
    }
}
