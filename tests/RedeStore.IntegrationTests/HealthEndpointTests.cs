using System.Net.Http.Json;
using Xunit;

namespace RedeStore.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public class HealthEndpointTests
{
    private readonly HttpClient _client;

    public HealthEndpointTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private sealed record HealthResponse(string Status, string Database);

    [Fact]
    public async Task GetHealth_ReturnsHealthyWithDatabaseConnected()
    {
        var response = await _client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("healthy", body!.Status);
        Assert.Equal("connected", body.Database);
    }
}
