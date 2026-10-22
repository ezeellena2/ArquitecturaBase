using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormVersionConfiguration : IEntityTypeConfiguration<BusinessFormVersion>
{
    public void Configure(EntityTypeBuilder<BusinessFormVersion> builder)
    {
        builder.ToTable("BusinessFormVersions"); builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.DefinitionId, x.Id });
        builder.HasIndex(x => new { x.DefinitionId, x.Number }).IsUnique();
        builder.Property(x => x.ContentHash).HasMaxLength(64);
        builder.Property(x => x.SchemaJson).HasColumnType("jsonb");
        builder.Property(x => x.NameJson).HasColumnType("jsonb");
        builder.Property(x => x.SubmitLabelJson).HasColumnType("jsonb");
        builder.Property(x => x.Culture).HasMaxLength(2);
        builder.Property(x => x.PublishedCultures).HasColumnType("text[]");
        builder.Property<string>("DisplayNameEs").HasMaxLength(120)
            .HasComputedColumnSql("\"NameJson\" ->> 'es'", stored: true);
        builder.Property<string>("DisplayNameEn").HasMaxLength(120)
            .HasComputedColumnSql("\"NameJson\" ->> 'en'", stored: true);
        builder.HasOne<BusinessFormDefinition>().WithMany().HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
    }
}
