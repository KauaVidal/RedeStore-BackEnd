using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Persistence;

[Collection(IntegrationTestCollection.Name)]
public class UsuarioRepositoryTests
{
    private readonly ApiFactory _factory;

    public UsuarioRepositoryTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AdicionarAsync_ThenBuscarPorEmailAsync_RetornaOMesmoUsuario()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = "Fulano de Tal",
            Email = $"{Guid.NewGuid()}@teste.com",
            Papel = Papel.Jovem,
            SenhaHash = "hash-fake",
        };

        await repositorio.AdicionarAsync(usuario, CancellationToken.None);
        var encontrado = await repositorio.BuscarPorEmailAsync(usuario.Email, CancellationToken.None);

        Assert.NotNull(encontrado);
        Assert.Equal(usuario.Nome, encontrado!.Nome);
        Assert.Equal(Papel.Jovem, encontrado.Papel);
    }

    [Fact]
    public async Task BuscarPorEmailAsync_ComEmailInexistente_RetornaNull()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();

        var encontrado = await repositorio.BuscarPorEmailAsync($"{Guid.NewGuid()}@naoexiste.com", CancellationToken.None);

        Assert.Null(encontrado);
    }

    [Fact]
    public async Task AtualizarAsync_PersisteMudancaDeNome()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = "Nome Original",
            Email = $"{Guid.NewGuid()}@teste.com",
            Papel = Papel.Jovem,
            SenhaHash = "hash-fake",
        };
        await repositorio.AdicionarAsync(usuario, CancellationToken.None);

        usuario.Nome = "Nome Atualizado";
        await repositorio.AtualizarAsync(usuario, CancellationToken.None);
        var encontrado = await repositorio.BuscarPorIdAsync(usuario.Id, CancellationToken.None);

        Assert.Equal("Nome Atualizado", encontrado!.Nome);
    }
}
