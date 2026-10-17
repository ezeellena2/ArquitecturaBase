using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;
internal sealed class BusinessFormMissingTopicConfiguration : IEntityTypeConfiguration<BusinessFormMissingTopic>
{
    public void Configure(EntityTypeBuilder<BusinessFormMissingTopic> builder)
    {
        builder.ToTable("BusinessFormMissingTopics");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Name).HasMaxLength(120);
        builder.Property(value => value.NormalizedName).HasMaxLength(120);
        builder.Property(value => value.Description).HasMaxLength(500);
        builder.Property(value => value.OriginalCulture).HasMaxLength(2);
        builder.Property(value => value.State).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(value => value.NormalizedName).IsUnique();
    }
}
