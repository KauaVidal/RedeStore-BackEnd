using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Configurations;

public class EventoConfiguration : IEntityTypeConfiguration<Evento>
{
    public void Configure(EntityTypeBuilder<Evento> builder)
    {
        builder.ToTable("Eventos");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Titulo).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Descricao).IsRequired();
        builder.Property(e => e.Local).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Preco).HasPrecision(10, 2);
        builder.Property(e => e.Foto).IsRequired();
        builder.HasMany(e => e.Inscricoes).WithOne().HasForeignKey(i => i.EventoId).OnDelete(DeleteBehavior.Cascade);
    }
}
