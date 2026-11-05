using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// IA3-A: a summary's buttons create the draft, wait for a change or say that the proposal changed; only the latest one counts.
public sealed class WhatsAppAssistantAuthoringTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Arranged(WhatsAppAssistantSuggestionFixture Host, WhatsAppAssistantDispatchService Dispatch,
        WhatsAppAssistantAuthoringService Taps, Owner Owner, WhatsAppAssistantRequest Summary);

    private static async Task<Arranged> SummarizedAsync()
    {
        var host = await WhatsAppAssistantSuggestionFixture.ReadyAsync();
        var owner = new Owner(host);
        var automatic = new WhatsAppAssistantAutomaticPolicy(WhatsAppAssistantAcceptanceFixture.Create(host), host.Reader, host.Context.Clock, host.Conversation,
            host.Messages, new(owner, owner, owner), new(host.Reader, host.Context.Clock));
        var dispatch = new WhatsAppAssistantDispatchService(host.Context.Policy, new(host.Reader, host.Assistant, host.Forms, host.Context.Clock, host.Presentation),
            host.Reader, host.Requests, host.Conversation, host.UnitOfWork, automatic, NullLogger<WhatsAppAssistantDispatchService>.Instance);
        var taps = new WhatsAppAssistantAuthoringService(host.Reader, host.Requests, host.Conversation, host.UnitOfWork, automatic,
            new(host.Reader, host.Assistant, owner, owner), host.Context.Clock, NullLogger<WhatsAppAssistantAuthoringService>.Instance);
        host.Requests.Rows.Clear(); host.Reader.Reference = null; host.Context.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var work = await dispatch.PrepareAsync(host.Context.Contact.Id, Ct);
        var id = work.Value!.RequestId;
        host.Assistant.Outcome = host.Assistant.Outcome.Value with { RequestId = id,
            Proposal = host.Assistant.Outcome.Value.Proposal! with { Capability = new("forms.authoring.propose-form", 1) },
            Provenance = host.Assistant.Outcome.Value.Provenance! with { ChannelId = "whatsapp-assistant" } };
        Assert.True((await dispatch.CompleteAsync(host.Context.Contact.Id, id, host.Assistant.Outcome, Ct)).IsSuccess);
        var summary = Assert.Single(host.Requests.Rows);
        Assert.Equal(WhatsAppAssistantRequestState.Sent, summary.State);
        host.Reader.LatestAuthoringRequest = summary.RequestId;
        return new(host, dispatch, taps, owner, summary);
    }

    private static void Tap(Arranged arranged, string action, Guid? requestId = null) => arranged.Host.Reader.AuthoringTap =
        new(Guid.NewGuid(), "wamid.fictitious-tap", WhatsAppAssistantAuthoringReplyCodec.Encode(requestId ?? arranged.Summary.RequestId, arranged.Summary.ProposalId!.Value, action),
            arranged.Host.Context.Clock.GetUtcNow().UtcDateTime);

    [Fact]
    public async Task Creating_lets_the_owner_create_it_outside_the_channel_transaction_and_replies_with_its_link()
    {
        var arranged = await SummarizedAsync();
        Tap(arranged, WhatsAppAssistantAuthoringReplyCodec.Create);
        Assert.True((await arranged.Taps.ProcessTapAsync(arranged.Host.Context.Contact.Id, Ct)).IsSuccess);
        var request = Assert.Single(arranged.Owner.Created);
        Assert.Equal((arranged.Summary.ProposalId, "whatsapp-assistant"), (request.Proposal.Id, request.Context.ChannelId));
        var reply = arranged.Host.Requests.Rows.Single(row => row.SourceMessageId == arranged.Host.Reader.AuthoringTap!.Id);
        Assert.Equal((WhatsAppAssistantRequestState.Sent, (Guid?)null), (reply.State, reply.ChangeRequestId));
        var outgoing = arranged.Host.Conversation.Outgoing.Single(row => row.Id == reply.OutgoingId);
        Assert.Equal(WhatsAppOutgoingMessageKind.Text, outgoing.Kind);
        Assert.Equal($"Listo: armé el borrador Parte de lluvias. Revisalo y publicalo desde la app: https://business.example.test/formularios/{arranged.Owner.CreatedId:D}", outgoing.Body);
    }

    [Fact]
    public async Task Changing_something_waits_for_the_change_and_the_next_message_continues_that_operation()
    {
        var arranged = await SummarizedAsync();
        Tap(arranged, WhatsAppAssistantAuthoringReplyCodec.Change);
        Assert.True((await arranged.Taps.ProcessTapAsync(arranged.Host.Context.Contact.Id, Ct)).IsSuccess);
        var reply = arranged.Host.Requests.Rows.Single(row => row.SourceMessageId == arranged.Host.Reader.AuthoringTap!.Id);
        Assert.Equal(arranged.Summary.RequestId, reply.ChangeRequestId);
        Assert.Equal("Decime qué cambio, por escrito o con un audio.", arranged.Host.Conversation.Outgoing.Single(row => row.Id == reply.OutgoingId).Body);
        Assert.Empty(arranged.Owner.Created);
        arranged.Host.Reader.ChangeRequest = reply.ChangeRequestId;
        arranged.Host.Reader.Source = new(Guid.NewGuid(), arranged.Host.Context.Contact.Id, "Fictitious change: add the date",
            arranged.Host.Context.Clock.GetUtcNow().UtcDateTime, arranged.Host.Context.Clock.GetUtcNow().UtcDateTime.AddDays(90));
        var work = await arranged.Dispatch.PrepareAsync(arranged.Host.Context.Contact.Id, Ct);
        Assert.Equal(arranged.Summary.RequestId, work.Value!.Operation!.PreviousRequestId);
        Assert.Equal(arranged.Summary.RequestId, arranged.Host.Requests.Rows.Single(row => row.RequestId == work.Value.RequestId).PreviousRequestId);
    }

    [Fact]
    public async Task A_button_of_an_earlier_proposal_says_that_it_changed_and_creates_nothing()
    {
        var arranged = await SummarizedAsync();
        arranged.Host.Reader.LatestAuthoringRequest = Guid.NewGuid();
        Tap(arranged, WhatsAppAssistantAuthoringReplyCodec.Create);
        Assert.True((await arranged.Taps.ProcessTapAsync(arranged.Host.Context.Contact.Id, Ct)).IsSuccess);
        var reply = arranged.Host.Requests.Rows.Single(row => row.SourceMessageId == arranged.Host.Reader.AuthoringTap!.Id);
        Assert.Equal("Esa propuesta ya cambió. Usá los botones de la última.", arranged.Host.Conversation.Outgoing.Single(row => row.Id == reply.OutgoingId).Body);
        Assert.Empty(arranged.Owner.Created);
    }

    [Fact]
    public async Task An_owner_refusal_replies_that_it_could_not_be_built()
    {
        var arranged = await SummarizedAsync();
        arranged.Owner.Failure = true;
        Tap(arranged, WhatsAppAssistantAuthoringReplyCodec.Create);
        await arranged.Taps.ProcessTapAsync(arranged.Host.Context.Contact.Id, Ct);
        var reply = arranged.Host.Requests.Rows.Single(row => row.SourceMessageId == arranged.Host.Reader.AuthoringTap!.Id);
        Assert.StartsWith("No pude armarlo.", arranged.Host.Conversation.Outgoing.Single(row => row.Id == reply.OutgoingId).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tap_on_an_unknown_proposal_is_only_marked_and_without_a_tap_nothing_happens()
    {
        var arranged = await SummarizedAsync();
        var outgoing = arranged.Host.Conversation.Outgoing.Count;
        Assert.True((await arranged.Taps.ProcessTapAsync(arranged.Host.Context.Contact.Id, Ct)).IsSuccess);
        arranged.Host.Reader.AuthoringTap = new(Guid.NewGuid(), "wamid.other", WhatsAppAssistantAuthoringReplyCodec.Encode(arranged.Summary.RequestId, Guid.NewGuid(),
            WhatsAppAssistantAuthoringReplyCodec.Create), arranged.Host.Context.Clock.GetUtcNow().UtcDateTime);
        Assert.True((await arranged.Taps.ProcessTapAsync(arranged.Host.Context.Contact.Id, Ct)).IsSuccess);
        Assert.Equal(outgoing, arranged.Host.Conversation.Outgoing.Count);
        Assert.Single(arranged.Host.Requests.Rows);
        Assert.Empty(arranged.Owner.Created);
    }

    [Theory]
    [InlineData("FORM_AUTHORING:")]
    [InlineData("FORM_ASSISTANT:00000000000000000000000000000000:00000000000000000000000000000000:create")]
    [InlineData("FORM_AUTHORING:0193f2c47a1e7c109d4e2b1f3a4c5d6e:0193f2c47a1e7c109d4e2b1f3a4c5d6f:delete")]
    [InlineData("FORM_AUTHORING:00000000000000000000000000000000:0193f2c47a1e7c109d4e2b1f3a4c5d6f:create")]
    public void The_button_id_is_closed_and_round_trips(string invalid)
    {
        var requestId = Guid.NewGuid(); var proposalId = Guid.NewGuid();
        Assert.True(WhatsAppAssistantAuthoringReplyCodec.TryParse(WhatsAppAssistantAuthoringReplyCodec.Encode(requestId, proposalId, WhatsAppAssistantAuthoringReplyCodec.Change),
            out var request, out var proposal, out var action));
        Assert.Equal((requestId, proposalId, WhatsAppAssistantAuthoringReplyCodec.Change), (request, proposal, action));
        Assert.False(WhatsAppAssistantAuthoringReplyCodec.TryParse(invalid, out _, out _, out _));
    }

    // The owner summarizes and, only from the button, creates; the origin is the app's public web origin.
    private sealed class Owner(WhatsAppAssistantSuggestionFixture host) : IStructuredAuthoringProposalChannel, IStructuredMissingFormRequestSink, IPublicOrigin
    {
        public List<StructuredAuthoringProposalRequest> Created { get; } = [];
        public Guid CreatedId { get; } = Guid.NewGuid();
        public bool Failure { get; set; }
        public Uri? Value => new("https://business.example.test/");
        public StructuredAuthoringSummary? Summarize(AssistantObservedProposal proposal, string culture) =>
            new(StructuredAuthoringKind.Form, "Parte de lluvias · LLU", ["1. Milímetros · número"], 0);
        public Task<Result<StructuredAuthoringCreatedResponse>> CreateAsync(StructuredAuthoringProposalRequest request, CancellationToken ct)
        {
            Assert.False(host.UnitOfWork.InTransaction);
            if (Failure) return Task.FromResult(Result.Failure<StructuredAuthoringCreatedResponse>(WhatsAppAssistantErrors.Unavailable));
            Created.Add(request);
            return Task.FromResult(Result.Success(new StructuredAuthoringCreatedResponse(StructuredAuthoringKind.Form, CreatedId, "Parte de lluvias", $"/formularios/{CreatedId:D}")));
        }
        public Task<Result<StructuredMissingFormRequestCaptureResponse>> CaptureAsync(StructuredMissingFormRequestCaptureRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
