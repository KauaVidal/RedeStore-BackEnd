using System.Collections.Concurrent;
using RedeStore.Application.Common;

namespace RedeStore.IntegrationTests;

public sealed class FakeEmailSender : IEmailSender
{
    public ConcurrentBag<(string Destinatario, string Assunto, string CorpoHtml)> Enviados { get; } = new();

    public Task EnviarAsync(string destinatario, string assunto, string corpoHtml, CancellationToken ct)
    {
        Enviados.Add((destinatario, assunto, corpoHtml));
        return Task.CompletedTask;
    }
}
