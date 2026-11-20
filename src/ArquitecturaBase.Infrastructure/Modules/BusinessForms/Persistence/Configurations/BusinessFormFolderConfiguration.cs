using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

// CP1-A and CM1 to CM6: a unique name per parent without telling case apart (also at the top, where the parent is null), at most one
// folder per system key and the subfolders go with their parent. Exactly the folders of the first level (the menus) carry an icon, but
// the CHECK that says so, CK_FormFolders_MenuIcon = ("ParentId" IS NULL) = ("MenuIcon" IS NOT NULL), comes with the next deployment:
// until then the previous version may still write a menu without an icon or a subfolder with one (FoldersAsMenus).
internal sealed class BusinessFormFolderConfiguration : IEntityTypeConfiguration<BusinessFormFolder>
{
    public void Configure(EntityTypeBuilder<BusinessFormFolder> builder)
    {
        builder.ToTable("FormFolders");
        builder.HasKey(folder => folder.Id);
        builder.Property(folder => folder.Name).HasMaxLength(BusinessFormFolder.NameMaxLength);
        builder.Property(folder => folder.NormalizedName).HasMaxLength(BusinessFormFolder.NameMaxLength);
        builder.Property(folder => folder.SystemKey).HasMaxLength(BusinessFormFolder.SystemKeyMaxLength);
        builder.Property(folder => folder.MenuIcon).HasMaxLength(BusinessFormFolder.MenuIconMaxLength);
        builder.Property(folder => folder.Revision).IsConcurrencyToken();
        builder.HasOne<BusinessFormFolder>().WithMany().HasForeignKey(folder => folder.ParentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(folder => new { folder.ParentId, folder.NormalizedName }).IsUnique().AreNullsDistinct(false);
        builder.HasIndex(folder => folder.SystemKey).IsUnique().HasFilter("\"SystemKey\" IS NOT NULL");
    }
}
