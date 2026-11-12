using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;

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
        // La tabla, con su nombre de siempre: el contexto no tiene un DbSet del módulo (el núcleo no lo nombra), y sin
        // él EF la llamaría como la entidad, en singular, y la migración borraría la tabla para crear otra.
        builder.ToTable("WhatsAppMessages");

        builder.Property(message => message.WaMessageId).HasMaxLength(WhatsAppMessage.MaxWaMessageIdLength);
        builder.Property(message => message.Origin).HasConversion<string>().HasMaxLength(EnumMaxLength).HasDefaultValue(WhatsAppMessageOrigin.Conversation);
        builder.Property(message => message.Direction).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(message => message.Kind).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(message => message.Purpose).HasConversion<string>().HasMaxLength(EnumMaxLength).HasDefaultValue(WhatsAppMessagePurpose.Unclassified);
        builder.Property(message => message.AccessEventKind).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(message => message.QuickReplyTitlesJson).HasColumnType("jsonb");
        builder.Property(message => message.AssistantReplyJson).HasColumnType("jsonb");
        builder.Property(message => message.Body).HasMaxLength(WhatsAppMessage.MaxBodyLength);
        builder.Property(message => message.ReplyId).HasMaxLength(WhatsAppMessage.MaxReplyIdLength);
        // W1-A: the point of a received location.
        builder.Property(message => message.Latitude).HasPrecision(9, 6);
        builder.Property(message => message.Longitude).HasPrecision(9, 6);
        builder.Property(message => message.ProtectedFlowResponse).HasMaxLength(96 * 1024);
        builder.Property(message => message.FlowCaptureState).HasConversion<string>().HasMaxLength(16);
        builder.Property(message => message.FlowRecordNumber).HasMaxLength(64);
        builder.Property(message => message.FlowCaptureErrorCode).HasMaxLength(128);
        builder.Property(message => message.FlowRejectedQuestion).HasMaxLength(80);
        builder.Property(message => message.HasFlowFiles).HasDefaultValue(false);
        builder.Property(message => message.FlowReplyFailureCount).HasDefaultValue(0);
        builder.Property(message => message.AudioState).HasConversion<string>().HasMaxLength(20);
        builder.Property(message => message.AudioMediaId).HasMaxLength(128);
        builder.Property(message => message.AudioContentType).HasMaxLength(128);
        builder.Property(message => message.AudioSha256).HasMaxLength(64);
        builder.Property(message => message.AudioStorageKey).HasMaxLength(500);
        builder.Property(message => message.AudioContextJson).HasMaxLength(16384);
        builder.Property(message => message.AudioFailureCode).HasMaxLength(128);
        builder.HasIndex(message => message.AudioRequestId).IsUnique().HasFilter("\"AudioRequestId\" IS NOT NULL");

        // Una planilla recibida: el descriptor de Meta, la copia privada, su estado y lo preparado. El procesador busca las
        // pendientes en cada vuelta, así que tienen su índice filtrado.
        builder.Property(message => message.SpreadsheetState).HasConversion<string>().HasMaxLength(20);
        builder.Property(message => message.SpreadsheetMediaId).HasMaxLength(128);
        builder.Property(message => message.SpreadsheetContentType).HasMaxLength(128);
        builder.Property(message => message.SpreadsheetSha256).HasMaxLength(64);
        builder.Property(message => message.SpreadsheetFileName).HasMaxLength(WhatsAppMessage.MaxSpreadsheetFileNameLength);
        builder.Property(message => message.SpreadsheetStorageKey).HasMaxLength(500);
        builder.Property(message => message.SpreadsheetCopySha256).HasMaxLength(64);
        builder.Property(message => message.SpreadsheetPreparedJson).HasMaxLength(WhatsAppMessage.MaxSpreadsheetPreparedLength);
        builder.Property(message => message.SpreadsheetFailureCode).HasMaxLength(128);
        // Con nombre propio: sin él, EF tomaría el índice de la clave foránea, que es sobre la misma columna.
        builder.HasIndex(message => message.ContactId, "IX_WhatsAppMessages_PendingSpreadsheets")
            .HasFilter($"\"{nameof(WhatsAppMessage.SpreadsheetState)}\" = '{nameof(WhatsAppSpreadsheetState.Pending)}'");

        // Un documento recibido (tarea 8 de documentos): el descriptor de Meta, la copia privada, su estado, la lectura y su pedido al
        // motor. El procesador busca en cada vuelta los que esperan bajarse o leerse, así que tienen su índice filtrado.
        builder.Property(message => message.DocumentState).HasConversion<string>().HasMaxLength(20);
        builder.Property(message => message.DocumentMediaId).HasMaxLength(128);
        builder.Property(message => message.DocumentContentType).HasMaxLength(128);
        builder.Property(message => message.DocumentSha256).HasMaxLength(64);
        builder.Property(message => message.DocumentFileName).HasMaxLength(WhatsAppMessage.MaxDocumentFileNameLength);
        builder.Property(message => message.DocumentStorageKey).HasMaxLength(500);
        builder.Property(message => message.DocumentCopySha256).HasMaxLength(64);
        builder.Property(message => message.DocumentReadingJson).HasMaxLength(WhatsAppMessage.MaxDocumentReadingLength);
        builder.Property(message => message.DocumentContextJson).HasMaxLength(WhatsAppMessage.MaxDocumentContextBytes);
        builder.Property(message => message.DocumentFailureCode).HasMaxLength(128);
        builder.HasIndex(message => message.DocumentRequestId).IsUnique().HasFilter("\"DocumentRequestId\" IS NOT NULL");
        builder.HasIndex(message => message.ContactId, "IX_WhatsAppMessages_PendingDocuments")
            .HasFilter($"\"{nameof(WhatsAppMessage.DocumentState)}\" IN ('{nameof(WhatsAppDocumentState.Pending)}', '{nameof(WhatsAppDocumentState.Reading)}')");

        builder.HasIndex(message => message.WaMessageId).IsUnique();

        // El de la clave foránea, explícito: EF lo daría por cubierto con el de los pendientes, que empieza por la misma
        // columna, pero ese solo tiene los entrantes sin procesar y no sirve para buscar los mensajes de un contacto.
        builder.HasIndex(message => message.ContactId);
        builder.HasIndex(message => new { message.ContactId, message.InboundSequence });
        builder.HasIndex(message => new { message.ContactId, message.OccurredAtUtc, message.Id });
        builder.HasIndex(message => message.FlowSessionId);

        // Los entrantes que el bot todavía no procesó: el procesador los revisa cada 30 segundos. Filtrado, así no crece
        // con el historial, que sí crece siempre.
        builder.HasIndex(message => new { message.ContactId, message.OccurredAtUtc })
            .HasDatabaseName(PendingInboundIndexName)
            .HasFilter($"\"{nameof(WhatsAppMessage.Direction)}\" = '{nameof(WhatsAppMessageDirection.Inbound)}' AND \"{nameof(WhatsAppMessage.Kind)}\" <> '{nameof(WhatsAppMessageKind.FlowReply)}' AND \"{nameof(WhatsAppMessage.ProcessedAtUtc)}\" IS NULL");
        builder.HasIndex(message => new { message.OccurredAtUtc, message.Id })
            .HasDatabaseName("IX_WhatsAppMessages_PendingFlowReplies")
            .HasFilter($"\"{nameof(WhatsAppMessage.Direction)}\" = '{nameof(WhatsAppMessageDirection.Inbound)}' AND \"{nameof(WhatsAppMessage.Kind)}\" = '{nameof(WhatsAppMessageKind.FlowReply)}' AND \"{nameof(WhatsAppMessage.ProcessedAtUtc)}\" IS NULL");
        // Los toques en botones que nadie atendió: cada vuelta busca los de un aviso de regla recibidos dentro de su ventana
        // (un día). Filtrado, así recorre sólo los toques recientes sin atender y no el historial.
        builder.HasIndex(message => message.OccurredAtUtc, "IX_WhatsAppMessages_PendingTaps")
            .HasFilter($"\"{nameof(WhatsAppMessage.Direction)}\" = '{nameof(WhatsAppMessageDirection.Inbound)}' AND \"{nameof(WhatsAppMessage.Kind)}\" = '{nameof(WhatsAppMessageKind.ButtonReply)}' AND \"{nameof(WhatsAppMessage.AssistantProcessedAtUtc)}\" IS NULL");

        builder.HasOne<WhatsAppContact>()
            .WithMany()
            .HasForeignKey(message => message.ContactId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
