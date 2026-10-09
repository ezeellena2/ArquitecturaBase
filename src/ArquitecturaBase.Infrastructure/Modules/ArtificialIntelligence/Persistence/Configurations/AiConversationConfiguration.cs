using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Configurations;

internal sealed class AiConversationConfiguration : IEntityTypeConfiguration<AiConversation>
{
    public void Configure(EntityTypeBuilder<AiConversation> builder)
    {
        builder.ToTable("AiConversations");
        builder.HasKey(conversation => conversation.Id);
        builder.HasAlternateKey(conversation => new { conversation.Id, conversation.OwnerUserId });
        builder.Property(conversation => conversation.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(conversation => conversation.Revision).IsConcurrencyToken();
        ConfigurePermissions(builder.Property(conversation => conversation.RequiredPermissions));
        builder.HasIndex(conversation => new { conversation.OwnerUserId, conversation.LastActivityAtUtc, conversation.Id });
        builder.HasQueryFilter(conversation => conversation.State == AiConversationState.Active);
    }

    internal static void ConfigurePermissions(PropertyBuilder<string[]> property) => property.HasColumnType("jsonb")
        .HasConversion(value => Serialize(value), value => Deserialize(value),
            new ValueComparer<string[]>((left, right) => Serialize(left!) == Serialize(right!),
                value => Serialize(value).GetHashCode(StringComparison.Ordinal), value => value.ToArray()));

    private static string Serialize(string[] permissions) => JsonSerializer.Serialize(permissions);
    private static string[] Deserialize(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];
}
