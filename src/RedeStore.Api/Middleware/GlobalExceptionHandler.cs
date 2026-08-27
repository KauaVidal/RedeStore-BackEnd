using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RedeStore.Domain.Exceptions;

namespace RedeStore.Api.Middleware;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, codigo, mensagem) = exception switch
        {
            DomainException domainException => (domainException.StatusCode, domainException.Codigo, domainException.Message),
            _ => (StatusCodes.Status500InternalServerError, "ERRO_INTERNO", "Ocorreu um erro inesperado."),
        };

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = codigo,
            Detail = mensagem,
        };

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
