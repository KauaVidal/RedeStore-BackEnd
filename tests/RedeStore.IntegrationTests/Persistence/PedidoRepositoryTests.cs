using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Persistence;

[Collection(IntegrationTestCollection.Name)]
public class PedidoRepositoryTests
{
    private readonly ApiFactory _factory;

    public PedidoRepositoryTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private static async Task<Guid> CriarUsuarioDeTesteAsync(IUsuarioRepository usuarioRepositorio)
    {
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = "Cliente Teste",
            Email = $"{Guid.NewGuid()}@teste.com",
            Papel = Papel.Jovem,
            SenhaHash = "hash-fake",
        };
        await usuarioRepositorio.AdicionarAsync(usuario, CancellationToken.None);
        return usuario.Id;
    }

    private static Pedido CriarPedidoDeTeste(Guid usuarioId, Endereco? endereco = null) => new()
    {
        Id = Guid.NewGuid(),
        UsuarioId = usuarioId,
        FormaEntrega = endereco is null ? FormaEntrega.Retirada : FormaEntrega.Entrega,
        Endereco = endereco,
        ValorTotal = 79.90m,
        Status = StatusPedido.Pago,
        CriadoEm = DateTime.UtcNow,
        Itens =
        [
            new ItemPedido
            {
                Id = Guid.NewGuid(),
                ProdutoId = Guid.NewGuid(),
                Nome = "Camiseta Rede",
                PrecoUnitario = 79.90m,
                FotoUrl = "https://exemplo.com/foto.jpg",
                Tamanho = "M",
                Cor = "Preto",
                Quantidade = 1,
            },
        ],
    };

    [Fact]
    public async Task AdicionarAsync_ThenBuscarPorIdAsync_RetornaPedidoComItens()
    {
        using var scope = _factory.Services.CreateScope();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var pedidoRepositorio = scope.ServiceProvider.GetRequiredService<IPedidoRepository>();
        var usuarioId = await CriarUsuarioDeTesteAsync(usuarioRepositorio);
        var pedido = CriarPedidoDeTeste(usuarioId);

        await pedidoRepositorio.AdicionarAsync(pedido, CancellationToken.None);
        var encontrado = await pedidoRepositorio.BuscarPorIdAsync(pedido.Id, CancellationToken.None);

        Assert.NotNull(encontrado);
        Assert.Single(encontrado!.Itens);
        Assert.Null(encontrado.Endereco);
    }

    [Fact]
    public async Task AdicionarAsync_ComEndereco_PersisteEnderecoCompleto()
    {
        using var scope = _factory.Services.CreateScope();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var pedidoRepositorio = scope.ServiceProvider.GetRequiredService<IPedidoRepository>();
        var usuarioId = await CriarUsuarioDeTesteAsync(usuarioRepositorio);
        var endereco = new Endereco { Rua = "Rua A", Numero = "123", Bairro = "Centro", Cidade = "São Paulo", Cep = "01000-000" };
        var pedido = CriarPedidoDeTeste(usuarioId, endereco);

        await pedidoRepositorio.AdicionarAsync(pedido, CancellationToken.None);
        var encontrado = await pedidoRepositorio.BuscarPorIdAsync(pedido.Id, CancellationToken.None);

        Assert.NotNull(encontrado!.Endereco);
        Assert.Equal("Rua A", encontrado.Endereco!.Rua);
        Assert.Null(encontrado.Endereco.Complemento);
    }

    [Fact]
    public async Task ListarPorUsuarioAsync_RetornaSoOsDoUsuarioMaisRecentesPrimeiro()
    {
        using var scope = _factory.Services.CreateScope();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var pedidoRepositorio = scope.ServiceProvider.GetRequiredService<IPedidoRepository>();
        var usuarioId = await CriarUsuarioDeTesteAsync(usuarioRepositorio);
        var outroUsuarioId = await CriarUsuarioDeTesteAsync(usuarioRepositorio);
        var pedidoAntigo = CriarPedidoDeTeste(usuarioId);
        pedidoAntigo.CriadoEm = DateTime.UtcNow.AddDays(-1);
        var pedidoRecente = CriarPedidoDeTeste(usuarioId);
        var pedidoDeOutro = CriarPedidoDeTeste(outroUsuarioId);
        await pedidoRepositorio.AdicionarAsync(pedidoAntigo, CancellationToken.None);
        await pedidoRepositorio.AdicionarAsync(pedidoRecente, CancellationToken.None);
        await pedidoRepositorio.AdicionarAsync(pedidoDeOutro, CancellationToken.None);

        var resultado = await pedidoRepositorio.ListarPorUsuarioAsync(usuarioId, CancellationToken.None);

        Assert.Equal(2, resultado.Count);
        Assert.Equal(pedidoRecente.Id, resultado[0].Id);
        Assert.Equal(pedidoAntigo.Id, resultado[1].Id);
    }

    [Fact]
    public async Task AtualizarAsync_AlteraOStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var pedidoRepositorio = scope.ServiceProvider.GetRequiredService<IPedidoRepository>();
        var usuarioId = await CriarUsuarioDeTesteAsync(usuarioRepositorio);
        var pedido = CriarPedidoDeTeste(usuarioId);
        await pedidoRepositorio.AdicionarAsync(pedido, CancellationToken.None);

        pedido.Status = StatusPedido.EmPreparo;
        await pedidoRepositorio.AtualizarAsync(pedido, CancellationToken.None);
        var recarregado = await pedidoRepositorio.BuscarPorIdAsync(pedido.Id, CancellationToken.None);

        Assert.Equal(StatusPedido.EmPreparo, recarregado!.Status);
    }

    [Fact]
    public async Task ListarTodosAsync_RetornaDeTodosOsUsuarios()
    {
        using var scope = _factory.Services.CreateScope();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var pedidoRepositorio = scope.ServiceProvider.GetRequiredService<IPedidoRepository>();
        var usuarioUmId = await CriarUsuarioDeTesteAsync(usuarioRepositorio);
        var usuarioDoisId = await CriarUsuarioDeTesteAsync(usuarioRepositorio);
        var pedidoUm = CriarPedidoDeTeste(usuarioUmId);
        var pedidoDois = CriarPedidoDeTeste(usuarioDoisId);
        await pedidoRepositorio.AdicionarAsync(pedidoUm, CancellationToken.None);
        await pedidoRepositorio.AdicionarAsync(pedidoDois, CancellationToken.None);

        var resultado = await pedidoRepositorio.ListarTodosAsync(CancellationToken.None);

        Assert.Contains(resultado, p => p.Id == pedidoUm.Id);
        Assert.Contains(resultado, p => p.Id == pedidoDois.Id);
    }
}
