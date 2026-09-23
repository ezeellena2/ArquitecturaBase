using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

/// <summary>
/// El BSUID y la cuenta son únicos cuando están: en Postgres un índice único admite varios NULL, así que un contacto
/// sin BSUID o sin cuenta no choca con otro. El número no es único (un número puede pasar a otra persona, que llega con
/// otro BSUID), pero tiene índice porque se busca por él.
/// </summary>
internal sealed class WhatsAppContactConfiguration : IEntityTypeConfiguration<WhatsAppContact>
{
    public void Configure(EntityTypeBuilder<WhatsAppContact> builder)
    {
        builder.Property(contact => contact.WaId).HasMaxLength(WhatsAppContact.MaxWaIdLength);
        builder.Property(contact => contact.UserIdentifier).HasMaxLength(WhatsAppContact.MaxUserIdentifierLength);
        builder.Property(contact => contact.ProfileName).HasMaxLength(WhatsAppContact.MaxProfileNameLength);

        builder.HasIndex(contact => contact.UserIdentifier).IsUnique();
        builder.HasIndex(contact => contact.UserId).IsUnique();
        builder.HasIndex(contact => contact.WaId);
    }
}
