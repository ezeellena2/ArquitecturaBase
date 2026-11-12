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

    public Task<StructuredDocumentLoading> FindLoadingAsync(Guid personUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Loading with { CanLoad = Loading.CanLoad && (LoadingPerson is null ? personUserId != Guid.Empty : personUserId == LoadingPerson) });

    public Task<StructuredDocumentRegistration> RegisterAsync(StructuredDocumentRegistrationRequest request, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var again = Registrations.Any(item => item.ChannelId == request.ChannelId && item.SourceId == request.SourceId);
            if (!again) Registrations.Add(request);
            return Task.FromResult(new StructuredDocumentRegistration(Guid.NewGuid(),
                request.NotRead == StructuredDocumentNotRead.Quota ? StructuredDocumentReplyKind.Quota : StructuredDocumentReplyKind.Rejected)
            { WasAlreadyRegistered = again });
        }
    }

    public Task<StructuredDocumentSendPreparation> PrepareSendAsync(StructuredDocumentSendRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new StructuredDocumentSendPreparation(StructuredDocumentSendState.Unavailable));

    public Task<StructuredDocumentChoiceState> ChooseOtherAsync(StructuredDocumentChoiceRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(StructuredDocumentChoiceState.Unavailable);
}
