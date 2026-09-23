using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

/// <summary>
/// El índice único del id de Meta es la defensa contra los duplicados que se escapen del lock. La dirección, la clase y
/// el estado se guardan como texto, como el método de ingreso en LoginAudits: se leen de una consulta SQL sin tener que
/// traducir un número. Un contacto no se puede borrar mientras tenga mensajes.
/// </summary>
internal sealed class WhatsAppMessageConfiguration : IEntityTypeConfiguration<WhatsAppMessage>
{
    public const int EnumMaxLength = 20;

    public void Configure(EntityTypeBuilder<WhatsAppMessage> builder)
    {
        builder.Property(message => message.WaMessageId).HasMaxLength(WhatsAppMessage.MaxWaMessageIdLength);
        builder.Property(message => message.Direction).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(message => message.Kind).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(message => message.Body).HasMaxLength(WhatsAppMessage.MaxBodyLength);
        builder.Property(message => message.ReplyId).HasMaxLength(WhatsAppMessage.MaxReplyIdLength);

        builder.HasIndex(message => message.WaMessageId).IsUnique();

        builder.HasOne<WhatsAppContact>()
            .WithMany()
            .HasForeignKey(message => message.ContactId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
