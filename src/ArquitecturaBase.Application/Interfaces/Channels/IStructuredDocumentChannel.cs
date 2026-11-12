using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// The inbox «Documentos recibidos» for a channel that receives documents (decisions 9 and 11). The owner registers a document once
/// per channel and source id, with its own private copy, and says what to tell the person; prepares the form a person chose with what
/// was read; and leaves it unclassified when the person chooses «Otro». The channel calls it outside any transaction of its own and
/// never receives an <c>Error</c>: every outcome is a state. The core brings the unavailable version and the forms module replaces it.
/// </summary>
public interface IStructuredDocumentChannel
{
    Task<StructuredDocumentRegistration> RegisterAsync(StructuredDocumentRegistrationRequest request, CancellationToken cancellationToken);
    Task<StructuredDocumentSendPreparation> PrepareSendAsync(StructuredDocumentSendRequest request, CancellationToken cancellationToken);
    Task<StructuredDocumentChoiceState> ChooseOtherAsync(StructuredDocumentChoiceRequest request, CancellationToken cancellationToken);
}
