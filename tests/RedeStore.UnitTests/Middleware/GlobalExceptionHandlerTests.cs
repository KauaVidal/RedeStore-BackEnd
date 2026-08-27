using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RedeStore.Api.Middleware;
using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Middleware;

public class GlobalExceptionHandlerTests
{
    private sealed class ExemploDomainException() : DomainException("mensagem de teste")
    {
        public override string Codigo => "ERRO_EXEMPLO";
        public override int StatusCode => StatusCodes.Status409Conflict;
    }

    private sealed class ErrorTrackingLogger : ILogger<GlobalExceptionHandler>
    {
        public bool ErrorLogged { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => throw new NotImplementedException();

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                ErrorLogged = true;
            }
        }
    }

    private readonly GlobalExceptionHandler _sut = new(NullLogger<GlobalExceptionHandler>.Instance);

    [Fact]
    public async Task TryHandleAsync_WithDomainException_WritesMappedProblemDetails()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var handled = await _sut.TryHandleAsync(httpContext, new ExemploDomainException(), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.Equal("application/problem+json", httpContext.Response.ContentType);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(httpContext.Response.Body);
        Assert.Equal("ERRO_EXEMPLO", problemDetails!.Title);
        Assert.Equal("mensagem de teste", problemDetails.Detail);
        Assert.Equal(httpContext.TraceIdentifier, problemDetails.Extensions["traceId"]?.ToString());
    }

    [Fact]
    public async Task TryHandleAsync_WithUnmappedException_Returns500WithGenericMessage()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var handled = await _sut.TryHandleAsync(httpContext, new InvalidOperationException("detalhe interno sensivel"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        Assert.Equal("application/problem+json", httpContext.Response.ContentType);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(httpContext.Response.Body);
        Assert.Equal("ERRO_INTERNO", problemDetails!.Title);
        Assert.DoesNotContain("detalhe interno sensivel", problemDetails.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_WithUnmappedException_LogsError()
    {
        var logger = new ErrorTrackingLogger();
        var sut = new GlobalExceptionHandler(logger);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        await sut.TryHandleAsync(httpContext, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.True(logger.ErrorLogged);
    }

    [Fact]
    public async Task TryHandleAsync_WithDomainException_DoesNotLogError()
    {
        var logger = new ErrorTrackingLogger();
        var sut = new GlobalExceptionHandler(logger);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        await sut.TryHandleAsync(httpContext, new ExemploDomainException(), CancellationToken.None);

        Assert.False(logger.ErrorLogged);
    }
}
