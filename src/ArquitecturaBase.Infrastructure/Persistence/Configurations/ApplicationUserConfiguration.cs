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

        // Identity valida que el email no se repita, pero la base no lo garantizaba: su índice EmailIndex pasa a único.
        builder.HasIndex(user => user.NormalizedEmail).IsUnique();
    }
}
