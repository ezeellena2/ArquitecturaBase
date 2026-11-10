namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;
public sealed record WhatsAppMediaContent(byte[] Content)
{
    /// <summary>The largest media the client downloads: the limit of the files of a Flow, which an audio also respects.</summary>
    public const long MaximumBytes = 25 * 1024 * 1024;
    public override string ToString() => "WhatsAppMediaContent { Content = [REDACTED] }";
}
