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

        builder.Property(settings => settings.DefaultCulture).HasMaxLength(5).HasDefaultValue(SystemSettings.InitialCulture);
        builder.Property(settings => settings.DefaultTimeZoneId).HasMaxLength(100).HasDefaultValue(SystemSettings.InitialTimeZoneId);
        builder.Property(settings => settings.DefaultPageSize).HasDefaultValue(SystemSettings.InitialPageSize);
        builder.Property(settings => settings.Revision).HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(settings => settings.PrimaryColor).HasMaxLength(AppearancePalette.ColorLength).HasDefaultValue(AppearancePalette.InitialPrimaryColor);
        builder.Property(settings => settings.MenuColor).HasMaxLength(AppearancePalette.ColorLength).HasDefaultValue(AppearancePalette.InitialMenuColor);
        builder.Property(settings => settings.HeaderColor).HasMaxLength(AppearancePalette.ColorLength).HasDefaultValue(AppearancePalette.InitialHeaderColor);
        builder.Property(settings => settings.AlternateRowColor).HasMaxLength(AppearancePalette.ColorLength).HasDefaultValue(AppearancePalette.InitialAlternateRowColor);
        // RC6-B and RC1-A: the row saved before reads the values the installation had in its configuration.
        builder.Property(settings => settings.AppName).HasMaxLength(SystemBranding.MaxNameLength).HasDefaultValue(SystemBranding.InitialAppName);
        builder.Property(settings => settings.EmailSenderName).HasMaxLength(SystemBranding.MaxNameLength).HasDefaultValue(SystemBranding.InitialEmailSenderName);
        builder.Property(settings => settings.EmailLogoUrl).HasMaxLength(SystemBranding.MaxLogoUrlLength);
        builder.Property(settings => settings.LoginAuditRetentionMonths).HasDefaultValue(IdentityRetentionPeriods.InitialLoginAuditMonths);
        builder.Property(settings => settings.LoginCodeRetentionDays).HasDefaultValue(IdentityRetentionPeriods.InitialLoginCodeDays);
        builder.Property(settings => settings.InvitationRetentionDays).HasDefaultValue(IdentityRetentionPeriods.InitialInvitationDays);

        builder.ToTable(table => table.HasCheckConstraint(
            SingleRowConstraintName,
            "\"Id\" = '" + SystemSettings.SingletonIdValue + "'"));
    }
}
