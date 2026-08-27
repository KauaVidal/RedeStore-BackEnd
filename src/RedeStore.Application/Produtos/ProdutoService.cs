using RedeStore.Application.Common;
using RedeStore.Application.Produtos.Dtos;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;

namespace RedeStore.Application.Produtos;

public sealed class ProdutoService : IProdutoService
{
    private readonly IProdutoRepository _produtoRepository;

    public ProdutoService(IProdutoRepository produtoRepository)
    {
        _produtoRepository = produtoRepository;
    }

    public async Task<List<ProdutoDto>> ListarAsync(string? categoria, string? busca, CancellationToken ct)
    {
        var produtos = await _produtoRepository.ListarAsync(ParseCategoria(categoria), busca, ct);
        return produtos.Select(MapearParaDto).ToList();
    }

    public async Task<List<ProdutoDto>> ListarDestaquesAsync(CancellationToken ct)
    {
        var produtos = await _produtoRepository.ListarDestaquesAsync(ct);
        return produtos.Select(MapearParaDto).ToList();
    }

    public async Task<ProdutoDto> ObterPorIdAsync(Guid id, CancellationToken ct)
    {
        var produto = await _produtoRepository.BuscarPorIdAsync(id, ct)
            ?? throw new ProdutoNaoEncontradoException($"Produto '{id}' não encontrado.");
        return MapearParaDto(produto);
    }

    public async Task<ProdutoDto> CriarAsync(CriarProdutoRequest request, CancellationToken ct)
    {
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            Nome = request.Nome,
            Categoria = ParseCategoriaObrigatoria(request.Categoria),
            Preco = request.Preco,
            Descricao = request.Descricao,
            Fotos = request.Fotos ?? [],
            Destaque = request.Destaque,
            Variacoes = request.Variacoes.Select(MapearVariacao).ToList(),
        };
        RecalcularTamanhosECores(produto);

        await _produtoRepository.AdicionarAsync(produto, ct);
        return MapearParaDto(produto);
    }

    public async Task<ProdutoDto> AtualizarAsync(Guid id, AtualizarProdutoRequest request, CancellationToken ct)
    {
        var produto = await _produtoRepository.BuscarPorIdAsync(id, ct)
            ?? throw new ProdutoNaoEncontradoException($"Produto '{id}' não encontrado.");

        if (request.Nome is not null)
        {
            produto.Nome = request.Nome;
        }

        if (request.Categoria is not null)
        {
            produto.Categoria = ParseCategoriaObrigatoria(request.Categoria);
        }

        if (request.Preco is not null)
        {
            produto.Preco = request.Preco.Value;
        }

        if (request.Descricao is not null)
        {
            produto.Descricao = request.Descricao;
        }

        if (request.Fotos is not null)
        {
            produto.Fotos = request.Fotos;
        }

        if (request.Destaque is not null)
        {
            produto.Destaque = request.Destaque.Value;
        }

        if (request.Variacoes is not null)
        {
            produto.Variacoes.Clear();
            foreach (var variacao in request.Variacoes)
            {
                produto.Variacoes.Add(MapearVariacao(variacao));
            }
        }

        RecalcularTamanhosECores(produto);

        await _produtoRepository.AtualizarAsync(produto, ct);
        return MapearParaDto(produto);
    }

    public async Task RemoverAsync(Guid id, CancellationToken ct)
    {
        var produto = await _produtoRepository.BuscarPorIdAsync(id, ct)
            ?? throw new ProdutoNaoEncontradoException($"Produto '{id}' não encontrado.");

        await _produtoRepository.RemoverAsync(produto, ct);
    }

    private static Variacao MapearVariacao(VariacaoRequest request) => new()
    {
        Id = Guid.NewGuid(),
        Tamanho = request.Tamanho,
        Cor = request.Cor,
        Estoque = request.Estoque,
    };

    private static void RecalcularTamanhosECores(Produto produto)
    {
        produto.Tamanhos = produto.Variacoes.Select(v => v.Tamanho).Distinct().ToList();
        produto.Cores = produto.Variacoes.Select(v => v.Cor).Distinct().ToList();
    }

    private static CategoriaProduto? ParseCategoria(string? valor) => valor switch
    {
        "camisetas" => CategoriaProduto.Camisetas,
        "moletons" => CategoriaProduto.Moletons,
        "acessorios" => CategoriaProduto.Acessorios,
        _ => null,
    };

    private static CategoriaProduto ParseCategoriaObrigatoria(string valor) =>
        ParseCategoria(valor) ?? throw new InvalidOperationException($"Categoria '{valor}' inválida.");

    private static ProdutoDto MapearParaDto(Produto produto) => new(
        produto.Id,
        produto.Nome,
        produto.Categoria.ToString().ToLowerInvariant(),
        produto.Preco,
        produto.Descricao,
        produto.Fotos,
        produto.Tamanhos,
        produto.Cores,
        produto.Destaque,
        produto.Variacoes.Select(v => new VariacaoDto(v.Id, v.Tamanho, v.Cor, v.Estoque)).ToList());
}
