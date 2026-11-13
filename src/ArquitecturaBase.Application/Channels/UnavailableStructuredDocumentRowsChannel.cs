using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Channels;

// Without the owner of the inbox no row of a spreadsheet is registered: Unavailable, never an error.
internal sealed class UnavailableStructuredDocumentRowsChannel : IStructuredDocumentRowsChannel
{
    public Task<StructuredDocumentRowsRegistration> RegisterRowsAsync(StructuredDocumentRowsRegistrationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new StructuredDocumentRowsRegistration(StructuredDocumentReplyKind.Unavailable));
    }

    public Task<StructuredDocumentRowsRegistration> RecognizeRowsAsync(StructuredDocumentRowsRegistrationRequest request, CancellationToken cancellationToken) =>
        RegisterRowsAsync(request, cancellationToken);
}
