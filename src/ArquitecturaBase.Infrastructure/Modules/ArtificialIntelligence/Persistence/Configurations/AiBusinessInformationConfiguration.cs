using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Configurations;
internal sealed class AiBusinessInformationConfiguration : IEntityTypeConfiguration<AiBusinessInformation>
{
    public void Configure(EntityTypeBuilder<AiBusinessInformation> builder)
    {
        builder.ToTable("AiBusinessInformation", table => table.HasCheckConstraint("CK_AiBusinessInformation_Scope", "\"Scope\" = 1"));
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Scope).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(120);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Hours).HasMaxLength(500);
        builder.Property(x => x.Location).HasMaxLength(500);
        builder.Property(x => x.Services).HasMaxLength(2000);
        builder.Property(x => x.Policies).HasMaxLength(2000);
        builder.Property(x => x.Faq).HasMaxLength(4000);
        builder.Property(x => x.Details).HasColumnType("jsonb").HasConversion(
            value => Serialize(value), value => Deserialize(value),
            new ValueComparer<AiBusinessDetails?>((left, right) => Serialize(left) == Serialize(right),
                value => Serialize(value).GetHashCode(StringComparison.Ordinal), value => Deserialize(Serialize(value))));
        builder.Property(x => x.Revision).IsConcurrencyToken();
    }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static string Serialize(AiBusinessDetails? details) => JsonSerializer.Serialize(details, JsonOptions);
    private static AiBusinessDetails? Deserialize(string json) => JsonSerializer.Deserialize<AiBusinessDetails>(json, JsonOptions);
}
