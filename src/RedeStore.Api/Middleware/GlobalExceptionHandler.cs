using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RedeStore.Domain.Exceptions;

namespace RedeStore.Api.Middleware;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
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

        if (exception is not DomainException)
        {
            logger.LogError(exception, "Exceção não tratada em {Path}", httpContext.Request.Path);
        }

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = codigo,
            Detail = mensagem,
        };
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }
}
