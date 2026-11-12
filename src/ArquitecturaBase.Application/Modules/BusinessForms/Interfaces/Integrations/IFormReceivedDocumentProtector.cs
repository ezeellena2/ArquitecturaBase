namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;

/// <summary>
/// Protects what a received document keeps private (its reading, its proposal and the detail of each step of its history), with a
/// purpose of its own bound to the document and to what it is: a value protected for one document or kind never reads for another.
/// </summary>
public interface IFormReceivedDocumentProtector
{
    byte[] Protect(Guid documentId, string kind, string json);
    /// <summary>The JSON, or null when it cannot be read (another key ring, another document or kind).</summary>
    string? Unprotect(Guid documentId, string kind, byte[] protectedValue);
}
