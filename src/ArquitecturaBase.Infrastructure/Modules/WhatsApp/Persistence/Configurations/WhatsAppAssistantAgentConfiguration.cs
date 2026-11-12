using System.Text.Json;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;

/// <summary>
/// Casos del asistente: one row per agent, today only the main one (unique). The profile, the cases, «Cualquier otra cosa» and the
/// limits are saved whole, so they go in JSONB, compared by their JSON to know whether a save changed anything.
/// </summary>
internal sealed class WhatsAppAssistantAgentConfiguration : IEntityTypeConfiguration<WhatsAppAssistantAgent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<WhatsAppAssistantAgent> builder)
    {
        builder.ToTable("WhatsAppAssistantAgents", table => table.HasCheckConstraint("CK_WhatsAppAssistantAgents_OriginalCulture", "\"OriginalCulture\" IN ('es', 'en')"));
        builder.HasIndex(agent => agent.IsMain).IsUnique().HasFilter("\"IsMain\"");
        builder.Property(agent => agent.OriginalCulture).HasMaxLength(2);
        builder.Property(agent => agent.Revision).IsConcurrencyToken();
        Json(builder.Property(agent => agent.Profile));
        Json(builder.Property(agent => agent.Cases));
        Json(builder.Property(agent => agent.Fallback));
        Json(builder.Property(agent => agent.Limits));
    }

    private static void Json<T>(PropertyBuilder<T> property) => property.HasColumnType("jsonb").HasConversion(
        value => JsonSerializer.Serialize(value, JsonOptions), value => JsonSerializer.Deserialize<T>(value, JsonOptions)!,
        new ValueComparer<T>((left, right) => JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions),
            value => JsonSerializer.Serialize(value, JsonOptions).GetHashCode(StringComparison.Ordinal),
            value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!));
}
