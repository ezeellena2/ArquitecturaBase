using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.DisplayName).HasMaxLength(ApplicationUser.DisplayNameMaxLength);
        builder.Property(user => user.Culture).HasMaxLength(ApplicationUser.CultureMaxLength);
        builder.Property(user => user.TimeZoneId).HasMaxLength(ApplicationUser.TimeZoneIdMaxLength);

        // El correo es opcional, y que no se repita lo garantiza la base: su índice EmailIndex pasa a único, y en
        // Postgres un índice único admite varios NULL.
        builder.HasIndex(user => user.NormalizedEmail).IsUnique();

        // El número de WhatsApp, en formato internacional. Único como el correo: una cuenta borrada conserva el suyo,
        // y mientras un número cargado por un administrador espera que lo verifiquen, nadie más lo puede usar.
        builder.Property(user => user.PhoneNumber).HasMaxLength(PhoneNumber.MaxLength);
        builder.HasIndex(user => user.PhoneNumber).IsUnique();
    }
}
