using ArquitecturaBase.Domain.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

/// <summary>
/// Los enlaces de ingreso, retirados el 2026-11-16 (B4): la tabla queda hasta que la vacíe la retención. El hash es
/// único, como cuando se canjeaban, y la invalidación de los pendientes busca por la cuenta. Como en los códigos y la
/// auditoría, la cuenta no lleva clave foránea: las cuentas no se borran de verdad.
/// </summary>
internal sealed class LoginLinkConfiguration : IEntityTypeConfiguration<LoginLink>
{
    /// <summary>El SHA-256 del token en hexadecimal.</summary>
    public const int TokenHashMaxLength = 64;

    public void Configure(EntityTypeBuilder<LoginLink> builder)
    {
        builder.Property(link => link.TokenHash).HasMaxLength(TokenHashMaxLength);

        builder.HasIndex(link => link.TokenHash).IsUnique();
        builder.HasIndex(link => new { link.UserId, link.CreatedAtUtc });
    }
}
