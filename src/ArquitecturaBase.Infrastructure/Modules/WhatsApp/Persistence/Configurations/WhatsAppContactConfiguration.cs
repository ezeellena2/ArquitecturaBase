using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;

/// <summary>
/// El BSUID y la cuenta son únicos cuando están: en Postgres un índice único admite varios NULL, así que un contacto
/// sin BSUID o sin cuenta no choca con otro. El número no es único (un número puede pasar a otra persona, que llega con
/// otro BSUID), pero tiene índice porque se busca por él.
/// </summary>
internal sealed class WhatsAppContactConfiguration : IEntityTypeConfiguration<WhatsAppContact>
{
    public void Configure(EntityTypeBuilder<WhatsAppContact> builder)
    {
        // La tabla, con su nombre de siempre: el contexto no tiene un DbSet del módulo (el núcleo no lo nombra), y sin
        // él EF la llamaría como la entidad, en singular, y la migración borraría la tabla para crear otra.
        builder.ToTable("WhatsAppContacts");

        builder.Property(contact => contact.WaId).HasMaxLength(WhatsAppContact.MaxWaIdLength);
        builder.Property(contact => contact.UserIdentifier).HasMaxLength(WhatsAppContact.MaxUserIdentifierLength);
        builder.Property(contact => contact.ProfileName).HasMaxLength(WhatsAppContact.MaxProfileNameLength);

        builder.HasIndex(contact => contact.UserIdentifier).IsUnique();
        builder.HasIndex(contact => contact.UserId).IsUnique();
        builder.HasIndex(contact => contact.WaId);
    }
}
