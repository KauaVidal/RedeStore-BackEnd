using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Configurations;

public class PedidoConfiguration : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> builder)
    {
        builder.ToTable("Pedidos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.FormaEntrega).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.ValorTotal).HasPrecision(10, 2);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(p => p.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Itens).WithOne().HasForeignKey(i => i.PedidoId).OnDelete(DeleteBehavior.Cascade);

        builder.OwnsOne(p => p.Endereco, endereco =>
        {
            endereco.Property(e => e.Rua).HasMaxLength(200);
            endereco.Property(e => e.Numero).HasMaxLength(20);
            endereco.Property(e => e.Complemento).HasMaxLength(200);
            endereco.Property(e => e.Bairro).HasMaxLength(200);
            endereco.Property(e => e.Cidade).HasMaxLength(200);
            endereco.Property(e => e.Cep).HasMaxLength(20);
        });
        builder.Navigation(p => p.Endereco).IsRequired(false);
    }
}
