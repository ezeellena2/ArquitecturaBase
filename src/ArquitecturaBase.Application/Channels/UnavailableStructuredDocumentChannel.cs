using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Channels;

// Without the owner of the inbox nothing is registered, prepared or chosen: every outcome is Unavailable, never an error.
internal sealed class UnavailableStructuredDocumentChannel : IStructuredDocumentChannel
{
    public Task<StructuredDocumentRegistration> RegisterAsync(StructuredDocumentRegistrationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new StructuredDocumentRegistration(null, StructuredDocumentReplyKind.Unavailable));
    }

    public Task<StructuredDocumentSendPreparation> PrepareSendAsync(StructuredDocumentSendRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new StructuredDocumentSendPreparation(StructuredDocumentSendState.Unavailable));
    }

    public Task<StructuredDocumentChoiceState> ChooseOtherAsync(StructuredDocumentChoiceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StructuredDocumentChoiceState.Unavailable);
    }
}
