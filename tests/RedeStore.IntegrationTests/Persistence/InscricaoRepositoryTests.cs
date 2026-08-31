using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Persistence;

[Collection(IntegrationTestCollection.Name)]
public class InscricaoRepositoryTests
{
    private readonly ApiFactory _factory;

    public InscricaoRepositoryTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private static Evento CriarEventoDeTeste() => new()
    {
        Id = Guid.NewGuid(),
        Titulo = $"Retiro {Guid.NewGuid()}",
        Descricao = "Retiro anual",
        DataHora = DateTime.UtcNow.AddDays(30),
        Local = "Sítio da Rede",
        Preco = 50m,
        VagasTotais = 20,
        Foto = "https://exemplo.com/retiro.jpg",
    };

    private static Inscricao CriarInscricaoDeTeste(Guid eventoId, Guid usuarioId, StatusInscricao status = StatusInscricao.Confirmada) => new()
    {
        Id = Guid.NewGuid(),
        EventoId = eventoId,
        UsuarioId = usuarioId,
        Status = status,
        ValorPago = 50m,
        CriadoEm = DateTime.UtcNow,
    };

    private static async Task<Usuario> CriarUsuarioAsync(IUsuarioRepository usuarioRepositorio)
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
    public async Task AdicionarAsync_ThenBuscarPorIdAsync_RetornaInscricao()
    {
        using var scope = _factory.Services.CreateScope();
        var eventoRepositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var inscricaoRepositorio = scope.ServiceProvider.GetRequiredService<IInscricaoRepository>();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var evento = CriarEventoDeTeste();
        await eventoRepositorio.AdicionarAsync(evento, CancellationToken.None);
        var usuario = await CriarUsuarioAsync(usuarioRepositorio);
        var inscricao = CriarInscricaoDeTeste(evento.Id, usuario.Id);

        await inscricaoRepositorio.AdicionarAsync(inscricao, CancellationToken.None);
        var encontrada = await inscricaoRepositorio.BuscarPorIdAsync(inscricao.Id, CancellationToken.None);

        Assert.NotNull(encontrada);
        Assert.Equal(StatusInscricao.Confirmada, encontrada!.Status);
    }

    [Fact]
    public async Task BuscarConfirmadaPorEventoEUsuarioAsync_ComInscricaoCancelada_RetornaNull()
    {
        using var scope = _factory.Services.CreateScope();
        var eventoRepositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var inscricaoRepositorio = scope.ServiceProvider.GetRequiredService<IInscricaoRepository>();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var evento = CriarEventoDeTeste();
        await eventoRepositorio.AdicionarAsync(evento, CancellationToken.None);
        var usuario = await CriarUsuarioAsync(usuarioRepositorio);
        await inscricaoRepositorio.AdicionarAsync(CriarInscricaoDeTeste(evento.Id, usuario.Id, StatusInscricao.Cancelada), CancellationToken.None);

        var encontrada = await inscricaoRepositorio.BuscarConfirmadaPorEventoEUsuarioAsync(evento.Id, usuario.Id, CancellationToken.None);

        Assert.Null(encontrada);
    }

    [Fact]
    public async Task ListarPorUsuarioAsync_RetornaSoAsDoUsuario()
    {
        using var scope = _factory.Services.CreateScope();
        var eventoRepositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var inscricaoRepositorio = scope.ServiceProvider.GetRequiredService<IInscricaoRepository>();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var evento = CriarEventoDeTeste();
        await eventoRepositorio.AdicionarAsync(evento, CancellationToken.None);
        var usuario = await CriarUsuarioAsync(usuarioRepositorio);
        var outroUsuario = await CriarUsuarioAsync(usuarioRepositorio);
        var minha = CriarInscricaoDeTeste(evento.Id, usuario.Id);
        var deOutro = CriarInscricaoDeTeste(evento.Id, outroUsuario.Id);
        await inscricaoRepositorio.AdicionarAsync(minha, CancellationToken.None);
        await inscricaoRepositorio.AdicionarAsync(deOutro, CancellationToken.None);

        var resultado = await inscricaoRepositorio.ListarPorUsuarioAsync(usuario.Id, CancellationToken.None);

        Assert.Contains(resultado, i => i.Id == minha.Id);
        Assert.DoesNotContain(resultado, i => i.Id == deOutro.Id);
    }

    [Fact]
    public async Task AtualizarAsync_AlteraOStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var eventoRepositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var inscricaoRepositorio = scope.ServiceProvider.GetRequiredService<IInscricaoRepository>();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var evento = CriarEventoDeTeste();
        await eventoRepositorio.AdicionarAsync(evento, CancellationToken.None);
        var usuario = await CriarUsuarioAsync(usuarioRepositorio);
        var inscricao = CriarInscricaoDeTeste(evento.Id, usuario.Id);
        await inscricaoRepositorio.AdicionarAsync(inscricao, CancellationToken.None);

        inscricao.Status = StatusInscricao.Cancelada;
        await inscricaoRepositorio.AtualizarAsync(inscricao, CancellationToken.None);
        var recarregada = await inscricaoRepositorio.BuscarPorIdAsync(inscricao.Id, CancellationToken.None);

        Assert.Equal(StatusInscricao.Cancelada, recarregada!.Status);
    }
}
