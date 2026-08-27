using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Configurations;

public class ProdutoConfiguration : IEntityTypeConfiguration<Produto>
{
    public void Configure(EntityTypeBuilder<Produto> builder)
    {
        builder.ToTable("Produtos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Nome).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Categoria).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Preco).HasPrecision(10, 2);
        builder.Property(p => p.Descricao).IsRequired();
        builder.Property(p => p.Fotos).IsRequired();
        builder.Property(p => p.Tamanhos).IsRequired();
        builder.Property(p => p.Cores).IsRequired();
        builder.HasMany(p => p.Variacoes).WithOne().HasForeignKey(v => v.ProdutoId).OnDelete(DeleteBehavior.Cascade);
    }
}
