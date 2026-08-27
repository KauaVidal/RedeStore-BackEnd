using FluentValidation;
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Application.Auth.Validators;

public sealed class RedefinirSenhaRequestValidator : AbstractValidator<RedefinirSenhaRequest>
{
    public RedefinirSenhaRequestValidator()
    {
        RuleFor(r => r.Token).NotEmpty();
        RuleFor(r => r.NovaSenha).NotEmpty().MinimumLength(8);
    }
}
