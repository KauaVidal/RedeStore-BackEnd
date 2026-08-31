using RedeStore.Application.Eventos;
using RedeStore.Application.Eventos.Dtos;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Eventos;

public class EventoServiceTests
{
    private readonly FakeEventoRepository _repositorio = new();
    private readonly EventoService _sut;

    public EventoServiceTests()
    {
        _sut = new EventoService(_repositorio);
    }

    private static CriarEventoRequest RequestValido(string? titulo = null, int vagasTotais = 10) => new(
        Titulo: titulo ?? "Culto Jovem",
        Descricao: "Encontro semanal",
        DataHora: DateTime.UtcNow.AddDays(7),
        Local: "Templo Sede",
        Preco: 0m,
        VagasTotais: vagasTotais,
        Foto: "https://exemplo.com/evento.jpg");

    [Fact]
    public async Task CriarAsync_ComVagasTotaisDez_RetornaVagasRestantesDez()
    {
        var resposta = await _sut.CriarAsync(RequestValido(vagasTotais: 10), CancellationToken.None);

        Assert.Equal(10, resposta.VagasRestantes);
    }

    [Fact]
    public async Task ObterPorIdAsync_ComIdInexistente_LancaEventoNaoEncontradoException()
    {
        await Assert.ThrowsAsync<EventoNaoEncontradoException>(() =>
            _sut.ObterPorIdAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ObterPorIdAsync_ComInscricaoConfirmada_DescontaDasVagasRestantes()
    {
        var criado = await _sut.CriarAsync(RequestValido(vagasTotais: 10), CancellationToken.None);
        _repositorio.EventosPorId[criado.Id].Inscricoes.Add(new Inscricao
        {
            Id = Guid.NewGuid(),
            EventoId = criado.Id,
            UsuarioId = Guid.NewGuid(),
            Status = StatusInscricao.Confirmada,
            ValorPago = 0m,
            CriadoEm = DateTime.UtcNow,
        });

        var encontrado = await _sut.ObterPorIdAsync(criado.Id, CancellationToken.None);

        Assert.Equal(9, encontrado.VagasRestantes);
    }

    [Fact]
    public async Task ObterVagasRestantesAsync_IgnoraInscricoesCanceladas()
    {
        var criado = await _sut.CriarAsync(RequestValido(vagasTotais: 10), CancellationToken.None);
        _repositorio.EventosPorId[criado.Id].Inscricoes.Add(new Inscricao
        {
            Id = Guid.NewGuid(),
            EventoId = criado.Id,
            UsuarioId = Guid.NewGuid(),
            Status = StatusInscricao.Cancelada,
            ValorPago = 0m,
            CriadoEm = DateTime.UtcNow,
        });

        var resultado = await _sut.ObterVagasRestantesAsync(criado.Id, CancellationToken.None);

        Assert.Equal(10, resultado.VagasRestantes);
    }

    [Fact]
    public async Task AtualizarAsync_ComIdInexistente_LancaEventoNaoEncontradoException()
    {
        await Assert.ThrowsAsync<EventoNaoEncontradoException>(() =>
            _sut.AtualizarAsync(Guid.NewGuid(), new AtualizarEventoRequest(null, null, null, null, null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task AtualizarAsync_ComApenasTitulo_MantemOsOutrosCampos()
    {
        var criado = await _sut.CriarAsync(RequestValido(), CancellationToken.None);

        var atualizado = await _sut.AtualizarAsync(
            criado.Id,
            new AtualizarEventoRequest("Novo Título", null, null, null, null, null, null),
            CancellationToken.None);

        Assert.Equal("Novo Título", atualizado.Titulo);
        Assert.Equal(criado.Local, atualizado.Local);
    }

    [Fact]
    public async Task RemoverAsync_ComIdInexistente_LancaEventoNaoEncontradoException()
    {
        await Assert.ThrowsAsync<EventoNaoEncontradoException>(() =>
            _sut.RemoverAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RemoverAsync_ComInscricaoConfirmada_LancaEventoComInscricoesConfirmadasException()
    {
        var criado = await _sut.CriarAsync(RequestValido(), CancellationToken.None);
        _repositorio.EventosPorId[criado.Id].Inscricoes.Add(new Inscricao
        {
            Id = Guid.NewGuid(),
            EventoId = criado.Id,
            UsuarioId = Guid.NewGuid(),
            Status = StatusInscricao.Confirmada,
            ValorPago = 0m,
            CriadoEm = DateTime.UtcNow,
        });

        await Assert.ThrowsAsync<EventoComInscricoesConfirmadasException>(() =>
            _sut.RemoverAsync(criado.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RemoverAsync_ComApenasInscricaoCancelada_RemoveOEvento()
    {
        var criado = await _sut.CriarAsync(RequestValido(), CancellationToken.None);
        _repositorio.EventosPorId[criado.Id].Inscricoes.Add(new Inscricao
        {
            Id = Guid.NewGuid(),
            EventoId = criado.Id,
            UsuarioId = Guid.NewGuid(),
            Status = StatusInscricao.Cancelada,
            ValorPago = 0m,
            CriadoEm = DateTime.UtcNow,
        });

        await _sut.RemoverAsync(criado.Id, CancellationToken.None);

        await Assert.ThrowsAsync<EventoNaoEncontradoException>(() =>
            _sut.ObterPorIdAsync(criado.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ListarAsync_ComApenasFuturosTrue_NaoRetornaEventosPassados()
    {
        await _sut.CriarAsync(RequestValido("Evento Futuro"), CancellationToken.None);
        var eventoPassadoId = Guid.NewGuid();
        _repositorio.EventosPorId[eventoPassadoId] = new Evento
        {
            Id = eventoPassadoId,
            Titulo = "Evento Passado",
            Descricao = "Já aconteceu",
            DataHora = DateTime.UtcNow.AddDays(-7),
            Local = "Templo Sede",
            Preco = 0m,
            VagasTotais = 10,
            Foto = "https://exemplo.com/evento.jpg",
        };

        var resultado = await _sut.ListarAsync(apenasFuturos: true, CancellationToken.None);

        Assert.DoesNotContain(resultado, e => e.Id == eventoPassadoId);
    }
}
