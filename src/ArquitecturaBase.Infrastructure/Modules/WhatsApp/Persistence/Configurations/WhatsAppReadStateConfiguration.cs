using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;

internal sealed class WhatsAppReadStateConfiguration : IEntityTypeConfiguration<WhatsAppReadState>
{
    public void Configure(EntityTypeBuilder<WhatsAppReadState> builder)
    {
        builder.ToTable("WhatsAppReadStates");
        builder.HasIndex(item => new { item.ContactId, item.OperatorUserId }).IsUnique();
        builder.HasOne<WhatsAppContact>().WithMany().HasForeignKey(item => item.ContactId).OnDelete(DeleteBehavior.Restrict);
    }
}
