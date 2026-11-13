using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

// Task 8 of the documents: a neutral owner of the inbox for the WhatsApp compositions. It says whether some form reads documents and who
// can load one (Loading, for the person it names), and registers what the channel hands it without reading the copy or deciding anything;
// it never imports formularios. Registering the same source twice answers the same, as the owner does.
internal sealed class CapturingStructuredDocumentChannel : IStructuredDocumentChannel
{
    private readonly object gate = new();
    public StructuredDocumentLoading Loading { get; set; } = new(true, true);
    public Guid? LoadingPerson { get; set; }
    public List<StructuredDocumentRegistrationRequest> Registrations { get; } = [];
    /// <summary>Task 9: what the owner says about a read document, as the scenario fixes it; without it, a read document is rejected.</summary>
    public StructuredDocumentRegistration? ReadReply { get; set; }
    /// <summary>Task 9: every registration as the channel asked for it, repeated ones too (a choice asks again for the document's id).</summary>
    public List<StructuredDocumentRegistrationRequest> Asked { get; } = [];
    public List<StructuredDocumentChoiceRequest> Others { get; } = [];
    /// <summary>Review of task 9: the form prepared for a chosen candidate, as the scenario fixes it, and every send asked for.</summary>
    public StructuredDocumentSendPreparation Preparation { get; set; } = new(StructuredDocumentSendState.Unavailable);
    public List<StructuredDocumentSendRequest> Sends { get; } = [];
    private readonly Dictionary<string, Guid> documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime?> deadlines = new(StringComparer.Ordinal);

    /// <summary>The inbox's document a channel source became, once registered.</summary>
    public Guid? DocumentOf(string channelId, string sourceId)
    {
        lock (gate) return documents.TryGetValue(channelId + ":" + sourceId, out var documentId) ? documentId : null;
    }

    public Task<StructuredDocumentLoading> FindLoadingAsync(Guid personUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Loading with { CanLoad = Loading.CanLoad && (LoadingPerson is null ? personUserId != Guid.Empty : personUserId == LoadingPerson) });

    public Task<StructuredDocumentRegistration> RegisterAsync(StructuredDocumentRegistrationRequest request, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Asked.Add(request);
            var again = Registrations.Any(item => item.ChannelId == request.ChannelId && item.SourceId == request.SourceId);
            if (!again) Registrations.Add(request);
            var key = request.ChannelId + ":" + request.SourceId;
            if (!documents.TryGetValue(key, out var documentId)) documents[key] = documentId = Guid.NewGuid();
            // As the owner keeps it: the first registration's deadline, never recomputed by a repeated one.
            if (!deadlines.TryGetValue(key, out var deadline)) deadlines[key] = deadline = request.ReplyExpiresAtUtc;
            if (request.Reading is not null && ReadReply is { } reply)
                return Task.FromResult(reply with { DocumentId = documentId, WasAlreadyRegistered = again, ReplyExpiresAtUtc = deadline });
            return Task.FromResult(new StructuredDocumentRegistration(documentId,
                request.NotRead == StructuredDocumentNotRead.Quota ? StructuredDocumentReplyKind.Quota : StructuredDocumentReplyKind.Rejected)
            { WasAlreadyRegistered = again });
        }
    }

    public Task<StructuredDocumentSendPreparation> PrepareSendAsync(StructuredDocumentSendRequest request, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Sends.Add(request);
            return Task.FromResult(Preparation);
        }
    }

    public Task<StructuredDocumentChoiceState> ChooseOtherAsync(StructuredDocumentChoiceRequest request, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Others.Add(request);
            return Task.FromResult(ReadReply is null ? StructuredDocumentChoiceState.Unavailable : StructuredDocumentChoiceState.Recorded);
        }
    }
}
