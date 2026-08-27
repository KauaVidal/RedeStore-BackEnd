using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using RedeStore.Application.Common;

namespace RedeStore.Infrastructure.Email;

public sealed class ResendEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly ResendOptions _options;

    public ResendEmailSender(HttpClient httpClient, IOptions<ResendOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task EnviarAsync(string destinatario, string assunto, string corpoHtml, CancellationToken ct)
    {
        var payload = new
        {
            from = _options.FromEmail,
            to = new[] { destinatario },
            subject = assunto,
            html = corpoHtml,
        };

        var response = await _httpClient.PostAsJsonAsync("emails", payload, ct);
        response.EnsureSuccessStatusCode();
    }
}
