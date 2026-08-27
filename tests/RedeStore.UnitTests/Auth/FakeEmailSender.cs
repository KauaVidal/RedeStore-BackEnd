using RedeStore.Application.Common;

namespace RedeStore.UnitTests.Auth;

public sealed class FakeEmailSender : IEmailSender
{
    public List<(string Destinatario, string Assunto, string CorpoHtml)> Enviados { get; } = new();

    public Task EnviarAsync(string destinatario, string assunto, string corpoHtml, CancellationToken ct)
    {
        Enviados.Add((destinatario, assunto, corpoHtml));
        return Task.CompletedTask;
    }
}
