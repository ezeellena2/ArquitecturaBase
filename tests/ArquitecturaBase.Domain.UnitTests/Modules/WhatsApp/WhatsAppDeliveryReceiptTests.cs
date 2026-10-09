using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppDeliveryReceiptTests
{
    [Fact]
    public void Early_receipt_preserves_the_latest_status_for_later_history_recording()
    {
        var now = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        var receipt = WhatsAppDeliveryReceipt.Create("wamid.early", WhatsAppMessageStatus.Read, now, null);
        receipt.Apply(WhatsAppMessageStatus.Sent, now.AddSeconds(-1), null);
        Assert.Equal(WhatsAppMessageStatus.Read, receipt.Status);
        receipt.Apply(WhatsAppMessageStatus.Failed, now, 131026);
        receipt.Apply(WhatsAppMessageStatus.Read, now.AddDays(1), null);
        Assert.Equal(WhatsAppMessageStatus.Failed, receipt.Status);
        Assert.Equal(131026, receipt.ErrorCode);
    }
}
