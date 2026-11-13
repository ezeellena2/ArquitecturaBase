using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// VC1-A: the rows of a spreadsheet a person sent, for the inbox «Documentos recibidos» (decision 12), apart from
/// <see cref="IStructuredDocumentChannel"/> so each owner keeps its limits. The owner registers a spreadsheet once per channel and source id,
/// with its own copy, which its rows share: an entry per row with what became of it (a load, one for the team, a repeated one, one that fits
/// no form), or a single entry when none fits, when it is the same spreadsheet again or when the day's quota ran out. Each load is then prepared
/// with <see cref="IStructuredDocumentChannel.PrepareSendAsync"/>, like a document. The channel calls it outside any transaction of its own and
/// never receives an <c>Error</c>. The core brings the unavailable version and the forms module replaces it.
/// </summary>
public interface IStructuredDocumentRowsChannel
{
    Task<StructuredDocumentRowsRegistration> RegisterRowsAsync(StructuredDocumentRowsRegistrationRequest request, CancellationToken cancellationToken);
}
