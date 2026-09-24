using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Domain.WhatsApp;

namespace ArquitecturaBase.Application.Models.WhatsApp;

/// <summary>
/// Lo que trajo un webhook de WhatsApp que importa (sección 7 del spec del ingreso con WhatsApp), ya leído del formato
/// de Meta y filtrado: solo los mensajes y los estados del número configurado. Lo arma
/// <see cref="IWhatsAppWebhookReader"/>. Ningún registro imprime su contenido: si uno terminara en un log, dejaría a la
/// vista textos, BSUID y números.
/// </summary>
public sealed record WhatsAppWebhookBatch(
    IReadOnlyList<WhatsAppWebhookMessage> Messages,
    IReadOnlyList<WhatsAppWebhookStatus> Statuses)
{
    public static WhatsAppWebhookBatch Empty { get; } = new([], []);

    public bool IsEmpty => Messages.Count == 0 && Statuses.Count == 0;

    public sealed override string ToString() =>
        $"{nameof(WhatsAppWebhookBatch)} ({Messages.Count} messages, {Statuses.Count} statuses)";
}

/// <summary>
/// Quién mandó un mensaje: su BSUID (<c>user_id</c>), su número (<c>wa_id</c>, solo dígitos) y el nombre de su
/// perfil. Llega al menos uno de los dos identificadores.
/// </summary>
public sealed record WhatsAppWebhookContact(string? WaId, string? UserIdentifier, string? ProfileName)
{
    public sealed override string ToString() => nameof(WhatsAppWebhookContact);
}

/// <summary>
/// Un mensaje que mandó una persona. <see cref="Body"/> es el texto, el título del botón o el aviso de WhatsApp (null
/// en una foto o un audio), y <see cref="ReplyId"/> el identificador del botón que se tocó.
/// </summary>
public sealed record WhatsAppWebhookMessage(
    string WaMessageId,
    WhatsAppWebhookContact From,
    WhatsAppMessageKind Kind,
    string? Body,
    string? ReplyId,
    DateTime OccurredAtUtc)
{
    public sealed override string ToString() => $"{nameof(WhatsAppWebhookMessage)} ({Kind})";
}

/// <summary>Un aviso de Meta sobre un mensaje que mandó el bot, con el código de error si falló.</summary>
public sealed record WhatsAppWebhookStatus(
    string WaMessageId,
    WhatsAppMessageStatus Status,
    DateTime OccurredAtUtc,
    int? ErrorCode)
{
    public sealed override string ToString() => $"{nameof(WhatsAppWebhookStatus)} ({Status})";
}
