using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Channels;

// Without the owner of the inbox nothing is registered, prepared or chosen: every outcome is Unavailable, never an error, and no form
// reads documents, so a channel keeps what it did before them.
internal sealed class UnavailableStructuredDocumentChannel : IStructuredDocumentChannel
{
    public Task<StructuredDocumentLoading> FindLoadingAsync(Guid personUserId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StructuredDocumentLoading.None);
    }

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
