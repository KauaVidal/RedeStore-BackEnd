using FluentValidation;
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Application.Auth.Validators;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress();
        RuleFor(r => r.Senha).NotEmpty();
    }
}
