namespace ArquitecturaBase.Application.Models.StructuredData;
public sealed record StructuredRecordOrigin(Guid SourceReceiptId, string ChannelId, Guid? SenderUserId, string? SenderName,
    DateTime SentAtUtc, Guid? ConversationId, string? MaskedAddress)
{
    public override string ToString() => "StructuredRecordOrigin { Content = [REDACTED] }";
}
