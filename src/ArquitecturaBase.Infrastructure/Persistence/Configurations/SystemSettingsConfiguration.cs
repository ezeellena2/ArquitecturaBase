using ArquitecturaBase.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

/// <summary>
/// Tabla de una sola fila. La clave primaria ya lo garantiza desde el modelo; el CHECK lo garantiza también contra
/// un INSERT hecho a mano en la base. El modo se guarda como texto, como el método de ingreso en LoginAudits: se
/// lee de una consulta SQL sin tener que traducir un número.
/// </summary>
internal sealed class SystemSettingsConfiguration : IEntityTypeConfiguration<SystemSettings>
{
    public const int RegistrationModeMaxLength = 20;
    public const string SingleRowConstraintName = "CK_SystemSettings_SingleRow";

    public void Configure(EntityTypeBuilder<SystemSettings> builder)
    {
        builder.Property(settings => settings.RegistrationMode)
            .HasConversion<string>()
            .HasMaxLength(RegistrationModeMaxLength);

        builder.ToTable(table => table.HasCheckConstraint(
            SingleRowConstraintName,
            "\"Id\" = '" + SystemSettings.SingletonIdValue + "'"));
    }
}
