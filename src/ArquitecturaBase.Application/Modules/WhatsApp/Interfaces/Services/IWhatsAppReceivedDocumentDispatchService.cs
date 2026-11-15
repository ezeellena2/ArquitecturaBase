using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

public interface IWhatsAppReceivedDocumentDispatchService
{
    /// <summary>
    /// Takes the contact's oldest received document that waits: decides who may have it downloaded, downloads, checks, prepares and
    /// copies it outside the transaction, and keeps what came of it (stored, rejected and answered once, refused and handed to a person,
    /// or unavailable). A PDF or a photo that will be used returns the engine's reading to submit or poll; one being read returns it again.
    /// </summary>
    Task<Result<WhatsAppAssistantWorkResponse?>> PrepareAsync(Guid contactId, CancellationToken ct);

    /// <summary>
    /// Before <see cref="PrepareAsync"/> reads it, counts the taking of a document that is going to be downloaded, so one that hangs the
    /// reading or ends the process is not taken for ever: taken too many times it is left unreadable and the person is told once. False
    /// when the document is not to be read now.
    /// </summary>
    Task<Result<bool>> TakeAsync(Guid contactId, CancellationToken ct);

    /// <summary>
    /// Gives back the count of a taking that was cut before the reading came back (the host stopped, the work was cancelled), so restarts
    /// during the download do not leave the document unreadable. The caller does not pass the token that was cancelled.
    /// </summary>
    Task<Result> GiveBackAsync(Guid contactId, CancellationToken ct);

    /// <summary>Keeps what came of the reading: its text with the message, a reading that waits again, or the reason it ends without one.</summary>
    Task<Result> CompleteAsync(Guid contactId, Guid requestId, Result<AssistantOperationResponse> outcome, CancellationToken ct);
}
