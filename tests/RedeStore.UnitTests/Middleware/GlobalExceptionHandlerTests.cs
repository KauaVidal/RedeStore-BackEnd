using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

    private readonly GlobalExceptionHandler _sut = new();

    [Fact]
    public async Task TryHandleAsync_WithDomainException_WritesMappedProblemDetails()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var handled = await _sut.TryHandleAsync(httpContext, new ExemploDomainException(), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(httpContext.Response.Body);
        Assert.Equal("ERRO_EXEMPLO", problemDetails!.Title);
        Assert.Equal("mensagem de teste", problemDetails.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_WithUnmappedException_Returns500WithGenericMessage()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var handled = await _sut.TryHandleAsync(httpContext, new InvalidOperationException("detalhe interno sensivel"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(httpContext.Response.Body);
        Assert.Equal("ERRO_INTERNO", problemDetails!.Title);
        Assert.DoesNotContain("detalhe interno sensivel", problemDetails.Detail);
    }
}
