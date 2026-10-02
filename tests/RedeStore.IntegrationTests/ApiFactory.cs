using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RedeStore.Application.Common;
using RedeStore.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace RedeStore.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public FakeEmailSender EmailSender =>
        (FakeEmailSender)Services.GetRequiredService<IEmailSender>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _container.GetConnectionString(),
                ["Jwt:SigningKey"] = "chave-de-teste-para-integracao-com-pelo-menos-32-caracteres",
                ["Jwt:Issuer"] = "RedeStore.IntegrationTests",
                ["Jwt:Audience"] = "RedeStore.IntegrationTests.Clients",
                ["Resend:ApiKey"] = "chave-fake-para-testes",
                ["Resend:FromEmail"] = "nao-responda@teste.com",
                ["Frontend:ResetPasswordUrl"] = "http://localhost:4200/redefinir-senha",
                // Os testes fazem muitos logins/cadastros a partir do mesmo "IP"; o limite real é testado à parte.
                ["RateLimiting:AuthPorMinuto"] = "100000",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, FakeEmailSender>();
        });
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RedeStoreDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }
}
