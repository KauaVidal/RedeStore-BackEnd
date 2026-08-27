namespace RedeStore.Application.Common;

public interface IEmailSender
{
    Task EnviarAsync(string destinatario, string assunto, string corpoHtml, CancellationToken ct);
}
