using FluentValidation;
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Application.Auth.Validators;

public sealed class CadastroRequestValidator : AbstractValidator<CadastroRequest>
{
    public CadastroRequestValidator()
    {
        RuleFor(r => r.Nome).NotEmpty().MinimumLength(2);
        RuleFor(r => r.Email).NotEmpty().EmailAddress();
        RuleFor(r => r.Senha).NotEmpty().MinimumLength(8);
    }
}
