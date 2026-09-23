using ArquitecturaBase.Domain.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

/// <summary>
/// El canje busca por el hash, que es único: dos enlaces con el mismo hash tendrían el mismo token. La emisión busca
/// por la cuenta, del enlace más nuevo al más viejo, para los límites y para invalidar los anteriores. Como en los
/// códigos y la auditoría, la cuenta no lleva clave foránea: las cuentas no se borran de verdad.
/// </summary>
internal sealed class LoginLinkConfiguration : IEntityTypeConfiguration<LoginLink>
{
    /// <summary>El SHA-256 en hexadecimal que calcula SecureTokenGenerator.</summary>
    public const int TokenHashMaxLength = 64;

    public void Configure(EntityTypeBuilder<LoginLink> builder)
    {
        builder.Property(link => link.TokenHash).HasMaxLength(TokenHashMaxLength);

        builder.HasIndex(link => link.TokenHash).IsUnique();
        builder.HasIndex(link => new { link.UserId, link.CreatedAtUtc });
    }
}
