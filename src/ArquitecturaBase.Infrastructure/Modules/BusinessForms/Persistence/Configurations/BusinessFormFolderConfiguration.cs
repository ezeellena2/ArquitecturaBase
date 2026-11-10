using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

// CP1-A: a unique name per parent without telling case apart (also at the top, where the parent is null), at most one folder per
// system key, and the subfolders go with their parent.
internal sealed class BusinessFormFolderConfiguration : IEntityTypeConfiguration<BusinessFormFolder>
{
    public void Configure(EntityTypeBuilder<BusinessFormFolder> builder)
    {
        builder.ToTable("FormFolders", table =>
        {
            table.HasCheckConstraint("CK_FormFolders_MenuPlacement", "\"MenuPlacement\" IN ('None', 'Own', 'Forms', 'Administration')");
            table.HasCheckConstraint("CK_FormFolders_MenuIcon", "(\"MenuPlacement\" = 'Own') = (\"MenuIcon\" IS NOT NULL)");
        });
        builder.HasKey(folder => folder.Id);
        builder.Property(folder => folder.Name).HasMaxLength(BusinessFormFolder.NameMaxLength);
        builder.Property(folder => folder.NormalizedName).HasMaxLength(BusinessFormFolder.NameMaxLength);
        builder.Property(folder => folder.SystemKey).HasMaxLength(BusinessFormFolder.SystemKeyMaxLength);
        builder.Property(folder => folder.MenuPlacement).HasConversion<string>().HasMaxLength(16);
        builder.Property(folder => folder.MenuIcon).HasMaxLength(BusinessFormFolder.MenuIconMaxLength);
        builder.Property(folder => folder.Revision).IsConcurrencyToken();
        builder.HasOne<BusinessFormFolder>().WithMany().HasForeignKey(folder => folder.ParentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(folder => new { folder.ParentId, folder.NormalizedName }).IsUnique().AreNullsDistinct(false);
        builder.HasIndex(folder => folder.SystemKey).IsUnique().HasFilter("\"SystemKey\" IS NOT NULL");
    }
}
