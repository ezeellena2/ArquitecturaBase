using ArquitecturaBase.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("StoredFiles", table => table.HasCheckConstraint("CK_StoredFiles_Size", "\"Size\" > 0"));
        builder.HasKey(file => file.Id);
        builder.HasIndex(file => file.OwnerUserId);
        builder.HasIndex(file => file.StorageKey).IsUnique();
        builder.Property(file => file.Name).HasMaxLength(255);
        builder.Property(file => file.ContentType).HasMaxLength(128);
        builder.Property(file => file.Sha256).HasMaxLength(64);
        builder.Property(file => file.StorageKey).HasMaxLength(512);
        builder.Property(file => file.ThumbnailKey).HasMaxLength(512);
    }
}
