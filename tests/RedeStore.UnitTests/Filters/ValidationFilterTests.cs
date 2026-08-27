using FluentValidation;
using Microsoft.AspNetCore.Http;
using RedeStore.Api.Filters;
using Xunit;

namespace RedeStore.UnitTests.Filters;

public class ValidationFilterTests
{
    private sealed record TesteRequest(string Nome);

    private sealed class TesteRequestValidator : AbstractValidator<TesteRequest>
    {
        public TesteRequestValidator()
        {
            RuleFor(r => r.Nome).MinimumLength(2);
        }
    }

    [Fact]
    public async Task InvokeAsync_ComRequestInvalido_NaoChamaProximoDelegateERetornaValidationProblem()
    {
        var filter = new ValidationFilter<TesteRequest>(new TesteRequestValidator());
        var context = EndpointFilterInvocationContext.Create(new DefaultHttpContext(), new TesteRequest("a"));
        var proximoChamado = false;

        var resultado = await filter.InvokeAsync(context, _ =>
        {
            proximoChamado = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        Assert.False(proximoChamado);
        Assert.IsAssignableFrom<IResult>(resultado);
    }

    [Fact]
    public async Task InvokeAsync_ComRequestValido_ChamaProximoDelegate()
    {
        var filter = new ValidationFilter<TesteRequest>(new TesteRequestValidator());
        var context = EndpointFilterInvocationContext.Create(new DefaultHttpContext(), new TesteRequest("Nome Valido"));
        var proximoChamado = false;

        await filter.InvokeAsync(context, _ =>
        {
            proximoChamado = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        Assert.True(proximoChamado);
    }
}
