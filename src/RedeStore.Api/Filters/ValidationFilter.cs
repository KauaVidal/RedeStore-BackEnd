using FluentValidation;

namespace RedeStore.Api.Filters;

public sealed class ValidationFilter<T> : IEndpointFilter
{
    private readonly IValidator<T> _validator;

    public ValidationFilter(IValidator<T> validator)
    {
        _validator = validator;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argumento = context.Arguments.OfType<T>().First();
        var resultado = await _validator.ValidateAsync(argumento);

        if (!resultado.IsValid)
        {
            return Results.ValidationProblem(resultado.ToDictionary());
        }

        return await next(context);
    }
}
