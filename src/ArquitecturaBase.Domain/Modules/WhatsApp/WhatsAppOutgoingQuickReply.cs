namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public sealed record WhatsAppOutgoingQuickReply(string Id, string Title)
{
    public override string ToString() => "WhatsAppOutgoingQuickReply { Content = [REDACTED] }";
}
