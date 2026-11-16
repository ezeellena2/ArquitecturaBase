using ArquitecturaBase.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

/// <summary>
/// La historia de los cambios administrativos (N13). El tipo se guarda como texto, como el método de ingreso en
/// LoginAudits, y lo agregado y lo quitado como arreglos de texto, como los servicios de una conexión de ARCA: se leen de
/// una consulta SQL sin descifrar nada. Sin clave foránea: un rol se borra de verdad y su historia queda. Se busca por
/// objeto o por actor, de lo más nuevo a lo más viejo.
/// </summary>
internal sealed class AdministrativeChangeConfiguration : IEntityTypeConfiguration<AdministrativeChange>
{
    public const int KindMaxLength = 40;

    public void Configure(EntityTypeBuilder<AdministrativeChange> builder)
    {
        builder.Property(change => change.Kind).HasConversion<string>().HasMaxLength(KindMaxLength);
        builder.Property(change => change.TargetName).HasMaxLength(AdministrativeChange.TextMaxLength);
        builder.Property(change => change.Before).HasMaxLength(AdministrativeChange.TextMaxLength);
        builder.Property(change => change.After).HasMaxLength(AdministrativeChange.TextMaxLength);
        builder.Property(change => change.Added).HasColumnType("text[]");
        builder.Property(change => change.Removed).HasColumnType("text[]");

        builder.HasIndex(change => new { change.TargetId, change.OccurredAtUtc });
        builder.HasIndex(change => new { change.ActorUserId, change.OccurredAtUtc });
    }
}
