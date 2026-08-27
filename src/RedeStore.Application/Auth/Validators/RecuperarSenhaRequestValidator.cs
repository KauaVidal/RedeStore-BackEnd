using FluentValidation;
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Application.Auth.Validators;

public sealed class RecuperarSenhaRequestValidator : AbstractValidator<RecuperarSenhaRequest>
{
    public RecuperarSenhaRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress();
    }
}
