namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public sealed record WhatsAppOutgoingQuickReply(string Id, string Title, long DefinitionRevision)
{
    public override string ToString() => "WhatsAppOutgoingQuickReply { Content = [REDACTED] }";
}
