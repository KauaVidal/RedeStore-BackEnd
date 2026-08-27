using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Persistence;

[Collection(IntegrationTestCollection.Name)]
public class PasswordResetTokenRepositoryTests
{
    private readonly ApiFactory _factory;

    public PasswordResetTokenRepositoryTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<Usuario> CriarUsuarioAsync(IUsuarioRepository usuarioRepositorio)
    {
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = "Fulano",
            Email = $"{Guid.NewGuid()}@teste.com",
            Papel = Papel.Jovem,
            SenhaHash = "hash-fake",
        };
        await usuarioRepositorio.AdicionarAsync(usuario, CancellationToken.None);
        return usuario;
    }

    [Fact]
    public async Task AdicionarAsync_ThenBuscarPorTokenHashAsync_RetornaOMesmoToken()
    {
        using var scope = _factory.Services.CreateScope();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var tokenRepositorio = scope.ServiceProvider.GetRequiredService<IPasswordResetTokenRepository>();
        var usuario = await CriarUsuarioAsync(usuarioRepositorio);
        var token = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiraEm = DateTime.UtcNow.AddHours(1),
        };

        await tokenRepositorio.AdicionarAsync(token, CancellationToken.None);
        var encontrado = await tokenRepositorio.BuscarPorTokenHashAsync(token.TokenHash, CancellationToken.None);

        Assert.NotNull(encontrado);
        Assert.Equal(usuario.Id, encontrado!.UsuarioId);
        Assert.Null(encontrado.UsadoEm);
    }

    [Fact]
    public async Task AtualizarAsync_MarcaTokenComoUsado()
    {
        using var scope = _factory.Services.CreateScope();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var tokenRepositorio = scope.ServiceProvider.GetRequiredService<IPasswordResetTokenRepository>();
        var usuario = await CriarUsuarioAsync(usuarioRepositorio);
        var token = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiraEm = DateTime.UtcNow.AddHours(1),
        };
        await tokenRepositorio.AdicionarAsync(token, CancellationToken.None);

        token.UsadoEm = DateTime.UtcNow;
        await tokenRepositorio.AtualizarAsync(token, CancellationToken.None);
        var encontrado = await tokenRepositorio.BuscarPorTokenHashAsync(token.TokenHash, CancellationToken.None);

        Assert.NotNull(encontrado!.UsadoEm);
    }
}
