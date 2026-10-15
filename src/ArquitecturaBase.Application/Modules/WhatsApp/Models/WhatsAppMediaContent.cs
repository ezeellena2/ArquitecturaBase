namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;
public sealed record WhatsAppMediaContent(byte[] Content)
{ public override string ToString() => "WhatsAppMediaContent { Content = [REDACTED] }"; }
