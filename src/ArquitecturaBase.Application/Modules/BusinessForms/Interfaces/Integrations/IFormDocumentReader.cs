using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;

/// <summary>
/// Reads a document attached to the assistant from bytes already in memory, never from its name, without rendering, decoding its
/// pixels or running anything, and accepting only what the private files accept. The caller applies the limits.
/// </summary>
public interface IFormDocumentReader
{
    /// <summary>What the bytes are as a document; <see cref="FormDocumentFormat.Unknown"/> when they are not one.</summary>
    FormDocumentFormat DetectFormat(byte[] content);

    /// <summary>
    /// The bytes as <see cref="DetectFormat"/> said (Pdf, Jpeg, Png or Docx): a PDF's pages, a photo's declared size or a Word's text.
    /// </summary>
    Task<FormDocumentContent> ReadAsync(byte[] content, FormDocumentFormat format, CancellationToken cancellationToken);
}
