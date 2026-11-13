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

    /// <summary>
    /// A spreadsheet none of whose rows fit, whose person chose a form from the «No lo reconoce» list (DOC2): with the proposal of the interpretation
    /// that continued with that form fixed (<see cref="StructuredDocumentRowsRegistrationRequest.FocusDefinitionId"/>), its single entry becomes
    /// its rows, armed from the owner's copy with the same rules as a first registration, and the reply says what became of each, like
    /// <see cref="RegisterRowsAsync"/>. Idempotent by channel and source id: rows already recognized, a choice that fits no row, or an entry that is
    /// no longer waiting for a form answer what the owner kept.
    /// </summary>
    Task<StructuredDocumentRowsRegistration> RecognizeRowsAsync(StructuredDocumentRowsRegistrationRequest request, CancellationToken cancellationToken);
}
