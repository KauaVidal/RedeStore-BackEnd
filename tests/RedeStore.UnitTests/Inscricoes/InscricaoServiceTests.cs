using RedeStore.Application.Inscricoes;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;
using RedeStore.UnitTests.Eventos;
using Xunit;

namespace RedeStore.UnitTests.Inscricoes;

public class InscricaoServiceTests
{
    private readonly FakeEventoRepository _eventoRepositorio = new();
    private readonly FakeInscricaoRepository _inscricaoRepositorio;
    private readonly InscricaoService _sut;

    public InscricaoServiceTests()
    {
        _inscricaoRepositorio = new FakeInscricaoRepository(_eventoRepositorio);
        _sut = new InscricaoService(_eventoRepositorio, _inscricaoRepositorio, new FakeUnitOfWork());
    }

    private Guid CriarEvento(int vagasTotais, decimal preco = 25m)
    {
        var id = Guid.NewGuid();
        _eventoRepositorio.EventosPorId[id] = new Evento
        {
            Id = id,
            Titulo = "Retiro",
            Descricao = "Retiro anual",
            DataHora = DateTime.UtcNow.AddDays(30),
            Local = "Sítio da Rede",
            Preco = preco,
            VagasTotais = vagasTotais,
            Foto = "https://exemplo.com/retiro.jpg",
        };
        return id;
    }

    [Fact]
    public async Task InscreverAsync_ComVagaDisponivel_RetornaCriadaComValorPagoIgualAoPrecoDoEvento()
    {
        var eventoId = CriarEvento(vagasTotais: 10, preco: 25m);
        var usuarioId = Guid.NewGuid();

        var resultado = await _sut.InscreverAsync(eventoId, usuarioId, CancellationToken.None);

        Assert.Equal("criada", resultado.Resultado);
        Assert.NotNull(resultado.Inscricao);
        Assert.Equal(25m, resultado.Inscricao!.ValorPago);
        Assert.Equal("confirmada", resultado.Inscricao.Status);
    }

    [Fact]
    public async Task InscreverAsync_ComEventoInexistente_LancaEventoNaoEncontradoException()
    {
        await Assert.ThrowsAsync<EventoNaoEncontradoException>(() =>
            _sut.InscreverAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task InscreverAsync_MesmoUsuarioDuasVezes_RetornaJaInscritoNaSegundaVez()
    {
        var eventoId = CriarEvento(vagasTotais: 10);
        var usuarioId = Guid.NewGuid();
        await _sut.InscreverAsync(eventoId, usuarioId, CancellationToken.None);

        var segunda = await _sut.InscreverAsync(eventoId, usuarioId, CancellationToken.None);

        Assert.Equal("ja_inscrito", segunda.Resultado);
        var todasAsInscricoes = await _sut.ListarPorUsuarioAsync(usuarioId, CancellationToken.None);
        Assert.Single(todasAsInscricoes);
    }

    [Fact]
    public async Task InscreverAsync_QuandoVagasEsgotadas_RetornaEsgotadoSemCriarRegistro()
    {
        var eventoId = CriarEvento(vagasTotais: 1);
        await _sut.InscreverAsync(eventoId, Guid.NewGuid(), CancellationToken.None);

        var resultado = await _sut.InscreverAsync(eventoId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("esgotado", resultado.Resultado);
        Assert.Null(resultado.Inscricao);
    }

    [Fact]
    public async Task CancelarAsync_ComIdInexistente_LancaInscricaoNaoEncontradaException()
    {
        await Assert.ThrowsAsync<InscricaoNaoEncontradaException>(() =>
            _sut.CancelarAsync(Guid.NewGuid(), Guid.NewGuid(), ehAdmin: false, CancellationToken.None));
    }

    [Fact]
    public async Task CancelarAsync_PeloProprioDono_MarcaComoCancelada()
    {
        var eventoId = CriarEvento(vagasTotais: 10);
        var usuarioId = Guid.NewGuid();
        var criada = await _sut.InscreverAsync(eventoId, usuarioId, CancellationToken.None);

        var cancelada = await _sut.CancelarAsync(criada.Inscricao!.Id, usuarioId, ehAdmin: false, CancellationToken.None);

        Assert.Equal("cancelada", cancelada.Status);
    }

    [Fact]
    public async Task CancelarAsync_PeloAdmin_MarcaComoCancelada()
    {
        var eventoId = CriarEvento(vagasTotais: 10);
        var criada = await _sut.InscreverAsync(eventoId, Guid.NewGuid(), CancellationToken.None);

        var cancelada = await _sut.CancelarAsync(criada.Inscricao!.Id, Guid.NewGuid(), ehAdmin: true, CancellationToken.None);

        Assert.Equal("cancelada", cancelada.Status);
    }

    [Fact]
    public async Task CancelarAsync_PorUsuarioNaoDonoNaoAdmin_LancaAcessoNegadoException()
    {
        var eventoId = CriarEvento(vagasTotais: 10);
        var criada = await _sut.InscreverAsync(eventoId, Guid.NewGuid(), CancellationToken.None);

        await Assert.ThrowsAsync<AcessoNegadoException>(() =>
            _sut.CancelarAsync(criada.Inscricao!.Id, Guid.NewGuid(), ehAdmin: false, CancellationToken.None));
    }

    [Fact]
    public async Task InscreverAsync_AposCancelamento_PermiteNovaInscricaoDoMesmoUsuario()
    {
        var eventoId = CriarEvento(vagasTotais: 10);
        var usuarioId = Guid.NewGuid();
        var primeira = await _sut.InscreverAsync(eventoId, usuarioId, CancellationToken.None);
        await _sut.CancelarAsync(primeira.Inscricao!.Id, usuarioId, ehAdmin: false, CancellationToken.None);

        var segunda = await _sut.InscreverAsync(eventoId, usuarioId, CancellationToken.None);

        Assert.Equal("criada", segunda.Resultado);
    }

    [Fact]
    public async Task ListarPorEventoAsync_RetornaAsInscricoesDaquelEvento()
    {
        var eventoId = CriarEvento(vagasTotais: 10);
        await _sut.InscreverAsync(eventoId, Guid.NewGuid(), CancellationToken.None);
        await _sut.InscreverAsync(eventoId, Guid.NewGuid(), CancellationToken.None);
        var outroEventoId = CriarEvento(vagasTotais: 10);
        await _sut.InscreverAsync(outroEventoId, Guid.NewGuid(), CancellationToken.None);

        var resultado = await _sut.ListarPorEventoAsync(eventoId, CancellationToken.None);

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, i => Assert.Equal(eventoId, i.EventoId));
    }
}
