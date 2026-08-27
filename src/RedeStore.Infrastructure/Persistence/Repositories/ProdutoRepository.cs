using Microsoft.EntityFrameworkCore;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Repositories;

public sealed class ProdutoRepository : IProdutoRepository
{
    private readonly RedeStoreDbContext _dbContext;

    public ProdutoRepository(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Produto>> ListarAsync(CategoriaProduto? categoria, string? busca, CancellationToken ct)
    {
        var query = _dbContext.Produtos.Include(p => p.Variacoes).AsQueryable();

        if (categoria is not null)
        {
            query = query.Where(p => p.Categoria == categoria);
        }

        if (!string.IsNullOrWhiteSpace(busca))
        {
            query = query.Where(p => EF.Functions.ILike(p.Nome, $"%{busca}%"));
        }

        return await query.ToListAsync(ct);
    }

    public Task<List<Produto>> ListarDestaquesAsync(CancellationToken ct) =>
        _dbContext.Produtos.Include(p => p.Variacoes).Where(p => p.Destaque).ToListAsync(ct);

    public Task<Produto?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        _dbContext.Produtos.Include(p => p.Variacoes).SingleOrDefaultAsync(p => p.Id == id, ct);

    public async Task AdicionarAsync(Produto produto, CancellationToken ct)
    {
        _dbContext.Produtos.Add(produto);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task AtualizarAsync(Produto produto, CancellationToken ct)
    {
        _dbContext.Produtos.Update(produto);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task RemoverAsync(Produto produto, CancellationToken ct)
    {
        _dbContext.Produtos.Remove(produto);
        await _dbContext.SaveChangesAsync(ct);
    }
}
