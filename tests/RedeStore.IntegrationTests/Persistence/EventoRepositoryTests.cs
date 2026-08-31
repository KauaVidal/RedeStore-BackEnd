using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Persistence;

[Collection(IntegrationTestCollection.Name)]
public class EventoRepositoryTests
{
    private readonly ApiFactory _factory;

    public EventoRepositoryTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private static Evento CriarEventoDeTeste(string? titulo = null, DateTime? dataHora = null) => new()
    {
        Id = Guid.NewGuid(),
        Titulo = titulo ?? $"Culto Jovem {Guid.NewGuid()}",
        Descricao = "Encontro semanal dos jovens",
        DataHora = dataHora ?? DateTime.UtcNow.AddDays(7),
        Local = "Templo Sede",
        Preco = 0m,
        VagasTotais = 50,
        Foto = "https://exemplo.com/evento.jpg",
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
    public async Task AdicionarAsync_ThenBuscarPorIdAsync_RetornaEvento()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var evento = CriarEventoDeTeste();

        await repositorio.AdicionarAsync(evento, CancellationToken.None);
        var encontrado = await repositorio.BuscarPorIdAsync(evento.Id, CancellationToken.None);

        Assert.NotNull(encontrado);
        Assert.Equal(evento.Titulo, encontrado!.Titulo);
        Assert.Empty(encontrado.Inscricoes);
    }

    [Fact]
    public async Task BuscarPorIdAsync_ComIdInexistente_RetornaNull()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();

        var encontrado = await repositorio.BuscarPorIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(encontrado);
    }

    [Fact]
    public async Task ListarAsync_ComApenasFuturosTrue_RetornaSoEventosFuturos()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var futuro = CriarEventoDeTeste(dataHora: DateTime.UtcNow.AddDays(7));
        var passado = CriarEventoDeTeste(dataHora: DateTime.UtcNow.AddDays(-7));
        await repositorio.AdicionarAsync(futuro, CancellationToken.None);
        await repositorio.AdicionarAsync(passado, CancellationToken.None);

        var resultado = await repositorio.ListarAsync(apenasFuturos: true, CancellationToken.None);

        Assert.Contains(resultado, e => e.Id == futuro.Id);
        Assert.DoesNotContain(resultado, e => e.Id == passado.Id);
    }

    [Fact]
    public async Task ListarAsync_ComApenasFuturosFalse_RetornaTodos()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var passado = CriarEventoDeTeste(dataHora: DateTime.UtcNow.AddDays(-7));
        await repositorio.AdicionarAsync(passado, CancellationToken.None);

        var resultado = await repositorio.ListarAsync(apenasFuturos: false, CancellationToken.None);

        Assert.Contains(resultado, e => e.Id == passado.Id);
    }

    [Fact]
    public async Task LockAndCountInscricoesConfirmadasAsync_ComEventoInexistente_RetornaNull()
    {
        using var scope = _factory.Services.CreateScope();
        var eventoRepositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await using var transacao = await unitOfWork.IniciarTransacaoAsync(CancellationToken.None);
        var resultado = await eventoRepositorio.LockAndCountInscricoesConfirmadasAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task LockAndCountInscricoesConfirmadasAsync_ComInscricoesConfirmadasEAlgumaCancelada_ContaSoAsConfirmadas()
    {
        using var scope = _factory.Services.CreateScope();
        var eventoRepositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var inscricaoRepositorio = scope.ServiceProvider.GetRequiredService<IInscricaoRepository>();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var evento = CriarEventoDeTeste();
        await eventoRepositorio.AdicionarAsync(evento, CancellationToken.None);
        var usuarioConfirmado = await CriarUsuarioAsync(usuarioRepositorio);
        var usuarioCancelado = await CriarUsuarioAsync(usuarioRepositorio);
        await inscricaoRepositorio.AdicionarAsync(new Inscricao
        {
            Id = Guid.NewGuid(),
            EventoId = evento.Id,
            UsuarioId = usuarioConfirmado.Id,
            Status = StatusInscricao.Confirmada,
            ValorPago = 0m,
            CriadoEm = DateTime.UtcNow,
        }, CancellationToken.None);
        await inscricaoRepositorio.AdicionarAsync(new Inscricao
        {
            Id = Guid.NewGuid(),
            EventoId = evento.Id,
            UsuarioId = usuarioCancelado.Id,
            Status = StatusInscricao.Cancelada,
            ValorPago = 0m,
            CriadoEm = DateTime.UtcNow,
        }, CancellationToken.None);

        await using var transacao = await unitOfWork.IniciarTransacaoAsync(CancellationToken.None);
        var resultado = await eventoRepositorio.LockAndCountInscricoesConfirmadasAsync(evento.Id, CancellationToken.None);
        await transacao.ConfirmarAsync(CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.Equal(evento.Id, resultado!.Value.Evento.Id);
        Assert.Equal(1, resultado.Value.VagasConfirmadas);
    }

    [Fact]
    public async Task RemoverAsync_ExcluiOEvento()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var evento = CriarEventoDeTeste();
        await repositorio.AdicionarAsync(evento, CancellationToken.None);

        await repositorio.RemoverAsync(evento, CancellationToken.None);
        var encontrado = await repositorio.BuscarPorIdAsync(evento.Id, CancellationToken.None);

        Assert.Null(encontrado);
    }
}
