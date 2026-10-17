using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppDeliveryReceiptTests
{
    private static readonly DateTime Start=new(2026,10,17,12,0,0,DateTimeKind.Utc);
    [Fact]
    public void A_read_receipt_records_only_the_read_event()
    {
        var receipt=WhatsAppDeliveryReceipt.Create("wamid.read-only",WhatsAppMessageStatus.Read,Start,null);
        Assert.Equal(Start,receipt.ReadAtUtc);Assert.Null(receipt.DeliveredAtUtc);
    }
    [Fact]
    public void An_older_delivery_is_retained_after_read_won_the_last_status_fence()
    {
        var receipt=WhatsAppDeliveryReceipt.Create("wamid.reordered",WhatsAppMessageStatus.Read,Start.AddMinutes(2),null);
        receipt.Apply(WhatsAppMessageStatus.Delivered,Start,null);
        Assert.Equal(WhatsAppMessageStatus.Read,receipt.Status);Assert.Equal(Start.AddMinutes(2),receipt.OccurredAtUtc);
        Assert.Equal(Start,receipt.DeliveredAtUtc);Assert.Equal(Start.AddMinutes(2),receipt.ReadAtUtc);
    }
    [Fact]
    public void A_legacy_delivered_event_survives_the_next_read_without_a_data_backfill()
    {
        var receipt=Legacy(WhatsAppMessageStatus.Delivered,Start);
        receipt.Apply(WhatsAppMessageStatus.Read,Start.AddMinutes(2),null);
        Assert.Equal(Start,receipt.DeliveredAtUtc);Assert.Equal(Start.AddMinutes(2),receipt.ReadAtUtc);
    }
    [Fact]
    public void A_legacy_read_event_is_retained_before_an_older_delivery_is_observed()
    {
        var receipt=Legacy(WhatsAppMessageStatus.Read,Start.AddMinutes(2));
        receipt.Apply(WhatsAppMessageStatus.Delivered,Start,null);
        Assert.Equal(WhatsAppMessageStatus.Read,receipt.Status);Assert.Equal(Start,receipt.DeliveredAtUtc);Assert.Equal(Start.AddMinutes(2),receipt.ReadAtUtc);
    }
    [Fact]
    public void Real_receipt_dates_are_observed_even_when_a_failure_keeps_the_last_status()
    {
        var receipt=WhatsAppDeliveryReceipt.Create("wamid.failed",WhatsAppMessageStatus.Failed,Start.AddMinutes(3),131026);
        receipt.Apply(WhatsAppMessageStatus.Read,Start.AddMinutes(2),null);receipt.Apply(WhatsAppMessageStatus.Delivered,Start,null);
        Assert.Equal(WhatsAppMessageStatus.Failed,receipt.Status);Assert.Equal(131026,receipt.ErrorCode);Assert.Equal(Start.AddMinutes(3),receipt.OccurredAtUtc);
        Assert.Equal(Start,receipt.DeliveredAtUtc);Assert.Equal(Start.AddMinutes(2),receipt.ReadAtUtc);
    }
    [Fact]
    public void Repeated_events_preserve_the_first_real_timestamp_for_each_status()
    {
        var receipt=WhatsAppDeliveryReceipt.Create("wamid.repeated",WhatsAppMessageStatus.Delivered,Start.AddMinutes(1),null);
        receipt.Apply(WhatsAppMessageStatus.Delivered,Start,null);receipt.Apply(WhatsAppMessageStatus.Delivered,Start.AddMinutes(3),null);
        receipt.Apply(WhatsAppMessageStatus.Read,Start.AddMinutes(2),null);receipt.Apply(WhatsAppMessageStatus.Read,Start.AddMinutes(4),null);
        Assert.Equal(Start,receipt.DeliveredAtUtc);Assert.Equal(Start.AddMinutes(2),receipt.ReadAtUtc);
    }
    [Theory]
    [InlineData(WhatsAppMessageStatus.Sent)]
    [InlineData(WhatsAppMessageStatus.Failed)]
    public void Other_statuses_do_not_invent_delivery_or_read_events(WhatsAppMessageStatus status)
    {
        var receipt=WhatsAppDeliveryReceipt.Create("wamid.other",status,Start,null);Assert.Null(receipt.DeliveredAtUtc);Assert.Null(receipt.ReadAtUtc);
    }
    private static WhatsAppDeliveryReceipt Legacy(WhatsAppMessageStatus status,DateTime occurredAtUtc)
    {
        var receipt=WhatsAppDeliveryReceipt.Create("wamid.legacy",status,occurredAtUtc,null);
        typeof(WhatsAppDeliveryReceipt).GetProperty(nameof(WhatsAppDeliveryReceipt.DeliveredAtUtc))!.SetValue(receipt,null);
        typeof(WhatsAppDeliveryReceipt).GetProperty(nameof(WhatsAppDeliveryReceipt.ReadAtUtc))!.SetValue(receipt,null);
        return receipt;
    }
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
