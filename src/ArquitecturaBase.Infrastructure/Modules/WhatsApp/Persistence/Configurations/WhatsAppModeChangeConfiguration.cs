using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;

/// <summary>H19: the history of the mode of each conversation; a revision of a contact has one change.</summary>
internal sealed class WhatsAppModeChangeConfiguration : IEntityTypeConfiguration<WhatsAppModeChange>
{
    public void Configure(EntityTypeBuilder<WhatsAppModeChange> builder)
    {
        builder.ToTable("WhatsAppModeChanges");
        builder.HasKey(change => change.Id);
        // The entity sets its id: a new change found in the contact's collection is added, not taken as an existing row.
        builder.Property(change => change.Id).ValueGeneratedNever();
        builder.Property(change => change.Mode).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(change => new { change.ContactId, change.ModeRevision }).IsUnique();
    }
}
