using Microsoft.EntityFrameworkCore;
using RedeStore.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace RedeStore.IntegrationTests.Persistence;

public class RedeStoreDbContextTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task CanConnectAsync_WithRealPostgresContainer_ReturnsTrue()
    {
        var options = new DbContextOptionsBuilder<RedeStoreDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        await using var dbContext = new RedeStoreDbContext(options);

        var canConnect = await dbContext.Database.CanConnectAsync();

        Assert.True(canConnect);
    }
}
