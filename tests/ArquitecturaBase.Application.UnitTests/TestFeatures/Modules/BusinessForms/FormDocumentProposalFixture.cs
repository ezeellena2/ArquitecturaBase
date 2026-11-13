using System.Collections.Concurrent;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Configuration;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Channels;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

// Task 14 (DOC6): the inbox in memory (FormReceivedDocumentFixture) with the requests of «Armar formulario con la IA», the topics of
// «Formularios que faltan» by their documents, a scripted engine that keeps what it is asked and finds it only with the context it was
// sealed with, a reviewer who designs forms and the services built on all of it. No provider.
internal sealed class FormDocumentProposalFixture : IFormDocumentProposalRepository, IFormDocumentProposalReader
{
    public static readonly string[] Designer = [BusinessFormsPermissions.DocumentsRead, BusinessFormsPermissions.DocumentsReview,
        BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.CatalogsRead];
    public FormReceivedDocumentFixture Inbox { get; } = new();
    public InMemoryUserAccounts Users { get; } = new();
    public FakePermissionService Permissions { get; } = new();
    public FakeCurrentUser Current { get; } = new();
    public ScriptedFormProposalEngine Engine { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public List<FormDocumentProposal> Proposals { get; } = [];
    /// <summary>The unclassified documents each topic's «Otro» noted, the newest first, as the reader finds them.</summary>
    public Dictionary<Guid, IReadOnlyList<Guid>> Topics { get; } = [];
    public Dictionary<Guid, string> TopicNames { get; } = [];
    /// <summary>The locks taken, in order: a person's re-reads (<c>asker</c>, their daily share) or a request (<c>request</c>).</summary>
    public List<(string Kind, Guid Id)> Locks { get; } = [];
    /// <summary>The re-reads a day of each person who asked (<c>BusinessForms:DocumentReread:DailyPerAsker</c>).</summary>
    public int RereadsPerDay { get; init; } = new FormDocumentRereadOptions().DailyPerAsker;
    /// <summary>When set, the next save of a request fails with it before keeping anything: the database went away, or the call was cancelled.</summary>
    public Exception? LockFailure { get; set; }
    public Guid Reviewer { get; }

    public FormDocumentProposalFixture()
    {
        Reviewer = Users.AddUser("revisa@example.test").Id;
        Permissions.Permissions[Reviewer] = Designer;
        Current.UserId = Reviewer;
    }

    public FormReceivedDocumentAccessGuard Guard() => new(Current, Users, Permissions, Inbox);
    public FormReceivedDocumentContentPolicy Contents() => new(Inbox);
    public FormDocumentAuthoringPolicy Policy() => new(Guard(), this, Inbox, Contents());
    public FormDocumentRereadService Rereads() => new(this, this, Inbox, UnitOfWork, Inbox.Clock,
        Options.Create(new FormDocumentRereadOptions { DailyPerAsker = RereadsPerDay }));
    // The list as the inbox shows it: the documents' items need only their reads and readings.
    public FormReceivedDocumentPresentationPolicy Presentation() => new(Inbox, Contents(), null!, null!, null!);
    public FormDocumentAuthoringService Authoring() => new(Policy(), this, this, Engine, Presentation(), UnitOfWork, Inbox.Clock);
    public FormReceivedDocumentExtractionService Extractions() => new(Guard(), Inbox, Inbox, Engine,
        new FormReceivedDocumentExtractionPolicy(Contents(), new FormDocumentProposalPolicy(Inbox, Inbox, []), Inbox), Inbox.Recorder(), UnitOfWork, Inbox.Clock);
    // The owner of creating from a proposal is the real one, over the given draft save and authoring reads; DOC6 never looks a proposal up.
    public FormDocumentDraftService Drafts(IFormDraftService drafts, IFormAuthoringReader authoring) =>
        new(Policy(), this, Engine, new FormDraftProposalService(new StubAssistantProposalLookup(), authoring, drafts, new FormDraftAccessGuard(Current, Permissions)),
            new FormDocumentProposalRecorder(this));

    /// <summary>
    /// An unclassified document of the inbox (as «Otro» leaves it) with its copy and, unless it has none, a reading with that text; it
    /// arrives when the test says.
    /// </summary>
    public FormReceivedDocument Document(string text = "| Total | $ 52.360,00 |", bool reading = true, DateTime? receivedAtUtc = null,
        string fileName = "ticket.pdf")
    {
        var id = Guid.CreateVersion7();
        var now = Inbox.Clock.GetUtcNow().UtcDateTime;
        var document = FormReceivedDocument.Receive(id, "whatsapp", "wamid." + id.ToString("N"), Guid.NewGuid(), receivedAtUtc ?? now,
            FormReceivedDocumentCopyPolicy.Key(id, "application/pdf"), fileName, "application/pdf", 2048, new string('a', 64), 1, null).Value;
        var protectedReading = reading
            ? Contents().ProtectReading(id, new StructuredDocumentReading(StructuredDocumentReadability.Readable, null, "es", text, false)) : null;
        Assert.True(document.Describe(protectedReading, "ticket de combustible", "").IsSuccess);
        Assert.True(document.LeaveUnclassified(FormReceivedDocument.ChoseOther, null, now).IsSuccess);
        Inbox.Documents.Add(document);
        return document;
    }

    /// <summary>A request this reviewer (or someone else) asked for those documents, of a topic or of one document.</summary>
    public FormDocumentProposal Request(IReadOnlyList<Guid> documentIds, Guid? topicId = null, Guid? actor = null)
    {
        var proposal = FormDocumentProposal.Request(Guid.NewGuid(), actor ?? Reviewer, "es", InMemoryUserAccounts.DefaultTimeZoneId, topicId, documentIds,
            Inbox.Clock.GetUtcNow().UtcDateTime).Value;
        Proposals.Add(proposal);
        return proposal;
    }

    // IFormDocumentProposalRepository
    public Task LockRequestAsync(Guid actorUserId, Guid requestId, CancellationToken cancellationToken)
    {
        if (LockFailure is not { } failure) return Task.CompletedTask;
        LockFailure = null;
        return Task.FromException(failure);
    }
    public Task LockAsync(Guid proposalId, CancellationToken cancellationToken) { Locks.Add(("request", proposalId)); return Task.CompletedTask; }
    public Task LockAskerAsync(Guid actorUserId, CancellationToken cancellationToken) { Locks.Add(("asker", actorUserId)); return Task.CompletedTask; }
    public Task<int> CountRereadsOnDayAsync(Guid actorUserId, DateOnly day, CancellationToken cancellationToken) =>
        Task.FromResult(Proposals.Where(proposal => proposal.ActorUserId == actorUserId).Sum(proposal => proposal.RereadsOn(day)));
    public Task<bool> ExistsRereadHeldAsync(Guid actorUserId, Guid exceptProposalId, DateTime nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult(Proposals.Any(proposal => proposal.ActorUserId == actorUserId && proposal.Id != exceptProposalId
            && proposal.PendingRereadIds.Length > 0 && proposal.RereadAfterUtc > nowUtc));
    public Task<FormDocumentProposal?> GetAsync(Guid proposalId, CancellationToken cancellationToken) =>
        Task.FromResult(Proposals.SingleOrDefault(proposal => proposal.Id == proposalId));
    public Task<FormDocumentProposal?> GetByRequestAsync(Guid actorUserId, Guid requestId, CancellationToken cancellationToken) =>
        Task.FromResult(Proposals.SingleOrDefault(proposal => proposal.ActorUserId == actorUserId && proposal.RequestId == requestId));
    public void Add(FormDocumentProposal proposal) => Proposals.Add(proposal);

    // IFormDocumentProposalReader
    public Task<FormDocumentProposalStoredRow?> FindByRequestAsync(Guid actorUserId, Guid requestId, CancellationToken cancellationToken) =>
        Task.FromResult(Proposals.Where(proposal => proposal.ActorUserId == actorUserId && proposal.RequestId == requestId).Select(Row).SingleOrDefault());
    public Task<FormDocumentProposalStoredRow?> FindAsync(Guid proposalId, CancellationToken cancellationToken) =>
        Task.FromResult(Proposals.Where(proposal => proposal.Id == proposalId).Select(Row).SingleOrDefault());
    // In no particular order, as the reader: the newest first here, whatever the ids say.
    public async Task<IReadOnlyList<FormReceivedDocumentStoredRow>> ListDocumentsAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken)
    {
        var rows = new List<FormReceivedDocumentStoredRow>();
        foreach (var id in documentIds)
            if (await Inbox.FindAsync(id, cancellationToken) is { } row) rows.Add(row);
        return rows.OrderByDescending(row => row.Id).ToArray();
    }
    public Task<IReadOnlyList<Guid>> ListTopicDocumentIdsAsync(Guid topicId, int maximum, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Topics.TryGetValue(topicId, out var ids) ? ids.Take(maximum).ToArray() : []);
    public Task<IReadOnlyList<Guid>> ListRereadsDueAsync(DateTime nowUtc, int maximum, CancellationToken cancellationToken) =>
        throw new NotSupportedException("What the re-reading lists is covered with PostgreSQL.");
    public Task<IReadOnlyList<Guid>> ListRereadingDocumentIdsAsync(int maximum, CancellationToken cancellationToken) =>
        throw new NotSupportedException("What the re-reading lists is covered with PostgreSQL.");

    private FormDocumentProposalStoredRow Row(FormDocumentProposal proposal) => new(proposal.Id, proposal.RequestId, proposal.ActorUserId, proposal.Culture,
        proposal.TimeZoneId, proposal.RequestedAtUtc, proposal.TopicId, proposal.TopicId is { } topic ? TopicNames.GetValueOrDefault(topic) : null,
        proposal.DocumentIds, proposal.DefinitionId, proposal.ProposalId, proposal.RereadVersionId);
}

/// <summary>
/// The engine, scripted: it keeps what it is submitted and answers it as pending until the test completes it, found only with the context it
/// was sealed with (the channel, the people, the language and zone, the fixed form and the channel conversation), with the instant it was
/// sealed at as its provenance. Like the real one, it keeps a receipt per person and request: the same request again (the same fingerprint:
/// context, text, instants and attachment) answers what it took without a new operation, and other content under that ID is refused. A
/// submission or a read can fail as an engine that refuses or is not available would, and another attempt can get there first.
/// </summary>
internal sealed class ScriptedFormProposalEngine : IAssistantOperations
{
    /// <summary>What the real engine answers a request ID it already took with other content.</summary>
    public static readonly Error RequestReused = Error.Conflict("Ai.Assistant.RequestReused", "The request identifier was used with another request.");
    private readonly ConcurrentDictionary<Guid, AssistantOperationResponse> completed = new();
    private readonly Dictionary<(Guid Actor, Guid RequestId), string> receipts = [];
    public List<AssistantOperationRequest> Submitted { get; } = [];
    /// <summary>How many submissions answered a request it had already taken.</summary>
    public int Replays { get; private set; }
    public int Reads { get; private set; }
    public Error? SubmitFailure { get; set; }
    public Error? ReadFailure { get; set; }
    /// <summary>Runs once, before the next submission is taken: another attempt of the same request can get there first.</summary>
    public Func<AssistantOperationRequest, Task>? BeforeSubmit { get; set; }

    public Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AssistantAvailabilityResponse("available"));

    public async Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request, CancellationToken cancellationToken)
    {
        if (BeforeSubmit is { } before)
        {
            BeforeSubmit = null;
            await before(request);
        }
        return SubmitFailure is { } failure ? Result.Failure<AssistantOperationAcceptedResponse>(failure) : Take(request);
    }

    /// <summary>Takes a request as the engine does: a new one is an operation, the same one again answers it, other content under its ID is refused.</summary>
    public Result<AssistantOperationAcceptedResponse> Take(AssistantOperationRequest request)
    {
        var key = (request.Context.ActorUserId, request.RequestId);
        var fingerprint = Fingerprint(request);
        if (receipts.TryGetValue(key, out var kept))
        {
            if (kept != fingerprint) return RequestReused;
            Replays++;
            return new AssistantOperationAcceptedResponse(request.RequestId, "pending");
        }
        receipts[key] = fingerprint;
        Submitted.Add(request);
        return new AssistantOperationAcceptedResponse(request.RequestId, "pending");
    }

    public Task<Result> CancelAsync(AssistantChannelContext context, Guid requestId, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

    public Task<Result<AssistantOperationResponse>> GetAsync(AssistantChannelContext context, Guid requestId, CancellationToken cancellationToken)
    {
        Reads++;
        if (ReadFailure is { } failure) return Task.FromResult(Result.Failure<AssistantOperationResponse>(failure));
        var submitted = Submitted.LastOrDefault(request => request.RequestId == requestId);
        if (submitted is null || !SameScope(context, submitted.Context))
            return Task.FromResult(Result.Failure<AssistantOperationResponse>(FormReceivedDocumentErrors.ExtractionNotFound));
        return Task.FromResult(Result.Success(completed.TryGetValue(requestId, out var response) ? response
            : new AssistantOperationResponse(requestId, "pending", null,
                new AssistantOperationProvenance("fictitious-profile", 1, "capabilities-v1", 1, submitted.OccurredAtUtc, submitted.Context.ChannelId), null)));
    }

    /// <summary>What the engine finished for that request.</summary>
    public void Complete(AssistantOperationResponse response) => completed[response.RequestId] = response;

    /// <summary>The engine no longer knows that request, as once its retention removed it.</summary>
    public void Forget(Guid requestId)
    {
        Submitted.RemoveAll(request => request.RequestId == requestId);
        foreach (var key in receipts.Keys.Where(key => key.RequestId == requestId).ToArray()) receipts.Remove(key);
    }

    private static bool SameScope(AssistantChannelContext requested, AssistantChannelContext sealedContext) =>
        requested.ChannelId == sealedContext.ChannelId && requested.ActorUserId == sealedContext.ActorUserId && requested.SubjectUserId == sealedContext.SubjectUserId
        && requested.Culture == sealedContext.Culture && requested.TimeZoneId == sealedContext.TimeZoneId && requested.FocusDefinitionId == sealedContext.FocusDefinitionId
        && requested.ChannelConversationId == sealedContext.ChannelConversationId;

    // What the real fingerprint binds: the context, the text, both instants and the attachment's copy, expiry and the documents after it.
    private static string Fingerprint(AssistantOperationRequest request) => JsonSerializer.Serialize(new object?[]
    {
        request.RequestId, request.Context.ChannelId, request.Context.ActorUserId, request.Context.SubjectUserId, request.Context.Culture,
        request.Context.TimeZoneId, request.Context.ChannelConversationId, request.Context.FocusDefinitionId,
        request.Context.AllowedCapabilities.Select(reference => reference.Id + "@" + reference.Version).ToArray(),
        request.Context.AllowedTerminals.Select(reference => reference.Id + "@" + reference.Version).ToArray(),
        request.Text, request.OccurredAtUtc, request.ContentExpiresAtUtc,
        request.Attachment?.StorageKey, request.Attachment?.Name, request.Attachment?.ContentType, request.Attachment?.Size, request.Attachment?.Sha256,
        request.Attachment?.ExpiresAtUtc, request.Attachment?.MoreDocuments?.Select(document => document?.Sha256).ToArray(),
    });
}
