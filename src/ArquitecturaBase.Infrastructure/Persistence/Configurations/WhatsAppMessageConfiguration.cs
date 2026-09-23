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

    public const string PendingInboundIndexName = "IX_WhatsAppMessages_PendingInbound";

    public void Configure(EntityTypeBuilder<WhatsAppMessage> builder)
    {
        builder.Property(message => message.WaMessageId).HasMaxLength(WhatsAppMessage.MaxWaMessageIdLength);
        builder.Property(message => message.Direction).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(message => message.Kind).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(message => message.Body).HasMaxLength(WhatsAppMessage.MaxBodyLength);
        builder.Property(message => message.ReplyId).HasMaxLength(WhatsAppMessage.MaxReplyIdLength);

        builder.HasIndex(message => message.WaMessageId).IsUnique();

        // El de la clave foránea, explícito: EF lo daría por cubierto con el de los pendientes, que empieza por la misma
        // columna, pero ese solo tiene los entrantes sin procesar y no sirve para buscar los mensajes de un contacto.
        builder.HasIndex(message => message.ContactId);

        // Los entrantes que el bot todavía no procesó: el procesador los revisa cada 30 segundos. Filtrado, así no crece
        // con el historial, que sí crece siempre.
        builder.HasIndex(message => new { message.ContactId, message.OccurredAtUtc })
            .HasDatabaseName(PendingInboundIndexName)
            .HasFilter($"\"{nameof(WhatsAppMessage.Direction)}\" = '{nameof(WhatsAppMessageDirection.Inbound)}' AND \"{nameof(WhatsAppMessage.ProcessedAtUtc)}\" IS NULL");

        builder.HasOne<WhatsAppContact>()
            .WithMany()
            .HasForeignKey(message => message.ContactId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
