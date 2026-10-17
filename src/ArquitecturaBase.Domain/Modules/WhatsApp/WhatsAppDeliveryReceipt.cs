using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>Delivery metadata retained when a webhook arrives before the outbound history commit.</summary>
public sealed class WhatsAppDeliveryReceipt : Entity
{
    private WhatsAppDeliveryReceipt() { WaMessageId = string.Empty; }
    public string WaMessageId { get; private set; }
    public WhatsAppMessageStatus Status { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public int? ErrorCode { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    public static WhatsAppDeliveryReceipt Create(string waMessageId, WhatsAppMessageStatus status, DateTime occurredAtUtc, int? errorCode)
    {
        if (!WhatsAppMessage.IsValidWaMessageId(waMessageId))
        {
            throw new ArgumentException("The receipt needs a valid provider message id.", nameof(waMessageId));
        }

        return new WhatsAppDeliveryReceipt
        {
            WaMessageId = waMessageId,
            Status = status,
            OccurredAtUtc = occurredAtUtc,
            ErrorCode = status == WhatsAppMessageStatus.Failed ? errorCode : null,
            DeliveredAtUtc = status == WhatsAppMessageStatus.Delivered ? occurredAtUtc : null,
            ReadAtUtc = status == WhatsAppMessageStatus.Read ? occurredAtUtc : null,
        };
    }

    public void Apply(WhatsAppMessageStatus status, DateTime occurredAtUtc, int? errorCode)
    {
        // Preserve the one real event retained by a receipt created before the nullable date columns existed.
        if (Status == WhatsAppMessageStatus.Delivered && DeliveredAtUtc is null) DeliveredAtUtc = OccurredAtUtc;
        if (Status == WhatsAppMessageStatus.Read && ReadAtUtc is null) ReadAtUtc = OccurredAtUtc;
        if (status == WhatsAppMessageStatus.Delivered && (DeliveredAtUtc is null || occurredAtUtc < DeliveredAtUtc)) DeliveredAtUtc = occurredAtUtc;
        if (status == WhatsAppMessageStatus.Read && (ReadAtUtc is null || occurredAtUtc < ReadAtUtc)) ReadAtUtc = occurredAtUtc;
        if (WhatsAppMessage.ShouldApplyStatus(Status, OccurredAtUtc, status, occurredAtUtc))
        {
            Status = status;
            OccurredAtUtc = occurredAtUtc;
            ErrorCode = status == WhatsAppMessageStatus.Failed ? errorCode : null;
        }
    }
}
