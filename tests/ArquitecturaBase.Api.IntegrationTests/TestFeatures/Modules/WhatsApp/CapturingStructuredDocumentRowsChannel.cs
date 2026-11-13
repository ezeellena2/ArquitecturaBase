using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

/// <summary>
/// VC1-A (task 29): a neutral owner of the rows of a spreadsheet that keeps every request and answers what the scenario scripts for each
/// one, idempotent by channel and source id like the inbox: a registered spreadsheet answers again what it was told, marked as already registered.
/// </summary>
public sealed class CapturingStructuredDocumentRowsChannel : IStructuredDocumentRowsChannel
{
    private readonly Dictionary<string, StructuredDocumentRowsRegistration> registered = new(StringComparer.Ordinal);

    public List<StructuredDocumentRowsRegistrationRequest> Requests { get; } = [];
    public Func<StructuredDocumentRowsRegistrationRequest, StructuredDocumentRowsRegistration>? Reply { get; set; }

    public Task<StructuredDocumentRowsRegistration> RegisterRowsAsync(StructuredDocumentRowsRegistrationRequest request, CancellationToken cancellationToken)
    {
        lock (registered)
        {
            Requests.Add(request);
            var key = request.ChannelId + ":" + request.SourceId;
            if (registered.TryGetValue(key, out var stored)) return Task.FromResult(stored with { WasAlreadyRegistered = true });
            var reply = Reply?.Invoke(request) ?? new StructuredDocumentRowsRegistration(StructuredDocumentReplyKind.Unavailable);
            if (reply.Kind != StructuredDocumentReplyKind.Unavailable) registered[key] = reply;
            return Task.FromResult(reply);
        }
    }
}
