using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Configurations;

public class VariacaoConfiguration : IEntityTypeConfiguration<Variacao>
{
    public void Configure(EntityTypeBuilder<Variacao> builder)
    {
        builder.ToTable("Variacoes");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Tamanho).IsRequired().HasMaxLength(50);
        builder.Property(v => v.Cor).IsRequired().HasMaxLength(50);
    }
}
