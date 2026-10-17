using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;
internal sealed class BusinessFormMissingRequestConfiguration : IEntityTypeConfiguration<BusinessFormMissingRequest>
{
    public void Configure(EntityTypeBuilder<BusinessFormMissingRequest> builder)
    {
        builder.ToTable("BusinessFormMissingRequests");
        builder.HasKey(value => value.Id);
        builder.HasIndex(value => value.SourceReceiptId).IsUnique();
        builder.HasIndex(value => new { value.TopicId, value.SubjectUserId });
        builder.HasIndex(value => new { value.TopicId, value.RequestedAtUtc });
        builder.HasOne<BusinessFormMissingTopic>().WithMany().HasForeignKey(value => value.TopicId).OnDelete(DeleteBehavior.Restrict);
    }
}
