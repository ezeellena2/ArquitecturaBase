namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>A server-generated private PDF reference; it is never exposed by the conversation API.</summary>
public sealed record WhatsAppOutgoingDocument(string StorageKey, string Name, string ContentType, long Size, string Sha256)
{
    public override string ToString() => "WhatsAppOutgoingDocument { Content = [REDACTED] }";
}
