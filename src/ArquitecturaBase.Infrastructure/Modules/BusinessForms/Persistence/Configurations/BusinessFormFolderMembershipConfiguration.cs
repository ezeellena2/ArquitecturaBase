using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

// CP2-A: an item is once in each folder and leaves it when the folder is deleted. The item id is opaque (form or catalog), without a
// foreign key, so deleting a form logically only hides its memberships.
internal sealed class BusinessFormFolderMembershipConfiguration : IEntityTypeConfiguration<BusinessFormFolderMembership>
{
    public void Configure(EntityTypeBuilder<BusinessFormFolderMembership> builder)
    {
        builder.ToTable("FormFolderMemberships", table =>
            table.HasCheckConstraint("CK_FormFolderMemberships_ItemKind", "\"ItemKind\" IN ('Form', 'Catalog')"));
        builder.HasKey(membership => membership.Id);
        builder.Property(membership => membership.ItemKind).HasConversion<string>().HasMaxLength(16);
        builder.HasOne<BusinessFormFolder>().WithMany().HasForeignKey(membership => membership.FolderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(membership => new { membership.FolderId, membership.ItemKind, membership.ItemId }).IsUnique();
        builder.HasIndex(membership => new { membership.ItemKind, membership.ItemId });
    }
}
