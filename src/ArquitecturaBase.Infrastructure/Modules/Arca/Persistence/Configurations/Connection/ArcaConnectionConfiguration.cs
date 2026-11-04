using ArquitecturaBase.Domain.Modules.Arca.Connection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Configurations.Connection;

internal sealed class ArcaConnectionConfiguration : IEntityTypeConfiguration<ArcaConnection>
{
    public void Configure(EntityTypeBuilder<ArcaConnection> builder)
    {
        builder.ToTable("ArcaConnections", table => table.HasCheckConstraint("CK_ArcaConnections_Scope", "\"Scope\" = 1"));
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Scope).IsUnique();
        builder.Property(x => x.Mode).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.Environment).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Cuit).HasMaxLength(11);
        builder.Property(x => x.EnabledServices).HasColumnType("text[]");
        builder.Property(x => x.Revision).IsConcurrencyToken();
    }
}
