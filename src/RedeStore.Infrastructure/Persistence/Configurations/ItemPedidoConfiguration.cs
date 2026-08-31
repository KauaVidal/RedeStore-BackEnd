using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Configurations;

public class ItemPedidoConfiguration : IEntityTypeConfiguration<ItemPedido>
{
    public void Configure(EntityTypeBuilder<ItemPedido> builder)
    {
        builder.ToTable("ItensPedido");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Nome).IsRequired().HasMaxLength(200);
        builder.Property(i => i.PrecoUnitario).HasPrecision(10, 2);
        builder.Property(i => i.FotoUrl).IsRequired();
        builder.Property(i => i.Tamanho).IsRequired().HasMaxLength(50);
        builder.Property(i => i.Cor).IsRequired().HasMaxLength(50);
    }
}
