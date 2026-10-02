using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using RedeStore.Application.Auth.Dtos;
using Xunit;

namespace RedeStore.IntegrationTests.Auth;

[Collection(IntegrationTestCollection.Name)]
public class AuthRateLimitingTests
{
    private const int Limite = 3;
    private readonly HttpClient _client;

    public AuthRateLimitingTests(ApiFactory factory)
    {
        _client = factory
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:AuthPorMinuto"] = Limite.ToString(),
                })))
            .CreateClient();
    }

    [Fact]
    public async Task PostLogin_AcimaDoLimitePorMinuto_Retorna429()
    {
        var request = new LoginRequest($"{Guid.NewGuid()}@teste.com", "senha-errada");

        for (var i = 0; i < Limite; i++)
        {
            var permitida = await _client.PostAsJsonAsync("/auth/login", request);
            Assert.Equal(HttpStatusCode.Unauthorized, permitida.StatusCode);
        }

        var bloqueada = await _client.PostAsJsonAsync("/auth/login", request);

        Assert.Equal(HttpStatusCode.TooManyRequests, bloqueada.StatusCode);
    }
}
