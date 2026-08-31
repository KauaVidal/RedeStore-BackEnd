using Microsoft.EntityFrameworkCore;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence;

public sealed class RedeStoreDbContext : DbContext
{
    public RedeStoreDbContext(DbContextOptions<RedeStoreDbContext> options) : base(options)
    {
    }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<Variacao> Variacoes => Set<Variacao>();
    public DbSet<Evento> Eventos => Set<Evento>();
    public DbSet<Inscricao> Inscricoes => Set<Inscricao>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RedeStoreDbContext).Assembly);
    }
}
