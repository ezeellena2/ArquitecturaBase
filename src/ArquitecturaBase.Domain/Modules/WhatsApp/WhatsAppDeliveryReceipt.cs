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

    public static WhatsAppDeliveryReceipt Create(string waMessageId, WhatsAppMessageStatus status, DateTime occurredAtUtc, int? errorCode)
    {
        if (!WhatsAppMessage.IsValidWaMessageId(waMessageId))
        {
            throw new ArgumentException("The receipt needs a valid provider message id.", nameof(waMessageId));
        }

        return new WhatsAppDeliveryReceipt
        {
            WaMessageId = waMessageId, Status = status, OccurredAtUtc = occurredAtUtc,
            ErrorCode = status == WhatsAppMessageStatus.Failed ? errorCode : null,
        };
    }

    public void Apply(WhatsAppMessageStatus status, DateTime occurredAtUtc, int? errorCode)
    {
        if (WhatsAppMessage.ShouldApplyStatus(Status, OccurredAtUtc, status, occurredAtUtc))
        {
            Status = status;
            OccurredAtUtc = occurredAtUtc;
            ErrorCode = status == WhatsAppMessageStatus.Failed ? errorCode : null;
        }
    }
}
