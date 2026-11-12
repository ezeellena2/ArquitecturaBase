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

    /// <summary>Keeps what came of the reading: its text with the message, a reading that waits again, or the reason it ends without one.</summary>
    Task<Result> CompleteAsync(Guid contactId, Guid requestId, Result<AssistantOperationResponse> outcome, CancellationToken ct);
}
