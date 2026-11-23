using System.Text.Json;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;

/// <summary>
/// Configuración › WhatsApp (RC3-A): one row, whose key the CHECK fixes, with the ranges of the entity also in the base. The countries are
/// saved whole, as JSONB, compared by their JSON.
/// </summary>
internal sealed class WhatsAppSettingsConfiguration : IEntityTypeConfiguration<WhatsAppSettings>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<WhatsAppSettings> builder)
    {
        builder.ToTable("WhatsAppSettings", table =>
        {
            table.HasCheckConstraint("CK_WhatsAppSettings_Singleton", $"\"Id\" = '{WhatsAppSettings.SingletonId}'");
            table.HasCheckConstraint("CK_WhatsAppSettings_FlowSessionLifetimeHours",
                $"\"FlowSessionLifetimeHours\" BETWEEN {WhatsAppSettings.MinFlowSessionLifetimeHours} AND {WhatsAppSettings.MaxFlowSessionLifetimeHours}");
            table.HasCheckConstraint("CK_WhatsAppSettings_MessageRetentionDays",
                $"\"MessageRetentionDays\" BETWEEN {WhatsAppSettings.MinMessageRetentionDays} AND {WhatsAppSettings.MaxMessageRetentionDays}");
        });
        builder.Property(settings => settings.Id).ValueGeneratedNever();
        builder.Property(settings => settings.Revision).IsConcurrencyToken();
        builder.Property(settings => settings.AllowedCountries).HasColumnType("jsonb").HasConversion(
            value => JsonSerializer.Serialize(value, JsonOptions),
            value => JsonSerializer.Deserialize<IReadOnlyList<string>>(value, JsonOptions)!,
            new ValueComparer<IReadOnlyList<string>>((left, right) => left!.SequenceEqual(right!),
                value => JsonSerializer.Serialize(value, JsonOptions).GetHashCode(StringComparison.Ordinal),
                value => value.ToArray()));
    }
}
