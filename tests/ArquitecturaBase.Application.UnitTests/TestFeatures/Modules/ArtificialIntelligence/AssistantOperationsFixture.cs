using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class AssistantOperationsFixture
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));
    public InMemoryUserAccounts Users { get; } = new();
    public FakePermissionService Permissions { get; } = new();
    public FakeUnitOfWork Work { get; } = new();
    public OperationsStore Store { get; }
    public OperationsProtector Protector { get; } = new();
    public RoundtripAssistantSource Source { get; } = new();
    public AiAssistantModelProfile[] Profiles { get; }
    public AiAssistantAudioModelProfile[] AudioProfiles { get; }
    public AssistantChannelContext Context { get; }
    public Guid ActorId => Context.ActorUserId;
    public Guid SubjectId => Context.SubjectUserId!.Value;
    public AiAssistantOperations Service { get; }

    public AssistantOperationsFixture(bool qualified = true, bool audioQualified = false)
    {
        var actor = Users.AddUser("operator-operations@example.test");
        var subject = Users.AddUser("subject-operations@example.test");
        Permissions.Permissions[actor.Id] = ["fictional.channel.read", "fictional.channel.send"];
        Permissions.Permissions[subject.Id] = ["formrecords.create"];
        Context = new("fixture-operations", actor.Id, subject.Id, "en", "UTC",
            new(["fictional.channel.read", "fictional.channel.send"], []), [new("fixture.proposal", 1)], [new("assistant_proposal", 1)]);
        Store = new(Clock, Work);
        var limits = new AiAssistantLimits();
        Profiles = qualified ? [new AiAssistantModelProfile(AiProvider.OpenAI, "model", limits.ContextBytes, true, true) { Id = "fixture-profile", Version = 3 }] : [];
        AudioProfiles = audioQualified ? [new(AiProvider.OpenAI, "audio-model", "fixture-audio", 2, ["audio/ogg"], 25000000)] : [];
        var catalog = new AiAssistantCapabilityCatalogPolicy([Source], Permissions);
        var guard = new AiAssistantAccessGuard(Users, Permissions, Profiles, limits, new FakeCurrentUser { UserId = Guid.NewGuid() }, catalog, AudioProfiles);
        var submissions = new AiAssistantSubmissionPolicy(Store, Store, Store, guard, Protector, new OperationsFingerprint(), limits, Clock);
        Service = new(Store, guard, submissions, Work, Store, new AiAssistantResponsePolicy(Protector, Clock, catalog), NullLogger<AiAssistantOperations>.Instance);
    }

    public AssistantOperationRequest Request(Guid? requestId = null) => new(requestId ?? Guid.NewGuid(), Context,
        "Fictitious visit requiring a form", Clock.GetUtcNow().UtcDateTime.AddMinutes(-2), Clock.GetUtcNow().UtcDateTime.AddHours(2));

    public (AiAssistantTurn Turn, AssistantObservedProposal Proposal, DateTime OccurredAtUtc) SeedCompleted()
    {
        var now = Clock.GetUtcNow().UtcDateTime;
        var occurredAtUtc = now.AddMinutes(-10);
        var capturedContext = Context with { RequiredPermissions = new(Context.RequiredPermissions.Actor, ["formrecords.create"]) };
        var profile = new AiAssistantExecutionProfileSnapshot("fixture-profile", 3, "capabilities-v1", 1, capturedContext);
        var profileJson = JsonSerializer.SerializeToNode(profile, AiAssistantResponsePolicy.JsonOptions)!;
        profileJson["occurredAtUtc"] = JsonValue.Create(occurredAtUtc);
        var conversation = AiConversation.Create(ActorId, now, Context.ChannelId);
        var turn = AiAssistantTurn.Create(ActorId, conversation.Id, Guid.NewGuid(), 1, "", Context.Culture, Context.TimeZoneId,
            AiProvider.OpenAI, "model", 1, now, now.AddHours(2), Context.ChannelId);
        if (turn.SealExecutionProfile(profileJson.ToJsonString(AiAssistantResponsePolicy.JsonOptions)).IsFailure)
            throw new InvalidOperationException("The operation fixture profile must seal before claiming.");
        turn.ProtectQuestion(Protector.Protect(ActorId, conversation.Id, turn.Id, "question", turn.PayloadRevision, "Fictional private input"));
        if (turn.Claim(Guid.NewGuid(), now.AddSeconds(90)).IsFailure) throw new InvalidOperationException("The operation fixture must claim.");
        var proposal = new AssistantObservedProposal(turn.Id, Guid.NewGuid(), new("fixture.proposal", 1), new RoundtripAssistantPayload(Source.ResourceId, Source.PrivateText));
        var output = JsonSerializer.Serialize(new { output = new { state = "proposed", proposalId = proposal.Id,
            payload = new { resourceId = Source.ResourceId, privateText = Source.PrivateText } }, readOrdinals = Array.Empty<int>() }, AiAssistantResponsePolicy.JsonOptions);
        var protectedOutput = Protector.Protect(ActorId, conversation.Id, turn.Id, "tool:1", turn.PayloadRevision, output);
        var selection = Protector.Protect(ActorId, conversation.Id, turn.Id, "answer", turn.PayloadRevision, "{\"kind\":\"proposal\",\"ordinal\":1}");
        var evidences = Protector.Protect(ActorId, conversation.Id, turn.Id, "evidences", turn.PayloadRevision, "[]");
        if (turn.Complete(turn.Fence, selection, evidences, now).IsFailure) throw new InvalidOperationException("The operation fixture must complete.");
        Store.Conversations.Add(conversation);
        Store.Turns.Add(turn);
        Store.Tools.Add(AiAssistantToolExecution.Create(turn.Id, 1, "fixture-call", "fixture.proposal", 1, [], "", now, protectedOutput, output.Length, "proposed"));
        Store.Receipts.Add(AiAssistantRequestReceipt.Create(ActorId, turn.RequestId, "fixture-fingerprint", "test", conversation.Id, turn.Id, now, now.AddDays(30), conversation.ChannelId, false));
        return (turn, proposal, occurredAtUtc);
    }
}

internal sealed class OperationsProtector : IAiConversationProtector
{
    private readonly RoundtripAssistantProtector inner = new();
    public int Reads { get; private set; }
    public string Protect(Guid owner, Guid conversation, Guid turn, string purpose, long revision, string plaintext) =>
        inner.Protect(owner, conversation, turn, purpose, revision, plaintext);
    public string? Unprotect(Guid owner, Guid conversation, Guid turn, string purpose, long revision, string ciphertext)
    {
        Reads++;
        return inner.Unprotect(owner, conversation, turn, purpose, revision, ciphertext);
    }
}

internal sealed class OperationsFingerprint : IAiAssistantRequestFingerprint
{
    public string ActiveKeyId => "fixture-key";
    public string Create(Guid owner, SubmitAiAssistantTurnRequest request, string keyId) => Hash(owner.ToString("D") + keyId + JsonSerializer.Serialize(request));
    public bool Matches(Guid owner, SubmitAiAssistantTurnRequest request, string keyId, string fingerprint) => Create(owner, request, keyId) == fingerprint;
    public string Create(AssistantOperationRequest request, string keyId) => Hash(keyId + JsonSerializer.Serialize(request));
    public bool Matches(AssistantOperationRequest request, string keyId, string fingerprint) => Create(request, keyId) == fingerprint;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

internal sealed class OperationsStore(FakeTimeProvider clock, FakeUnitOfWork work) : IAiAssistantRepository, IAiAssistantReader, IAiConnectionRepository, IAiConnectionReader
{
    public List<AiConversation> Conversations { get; } = [];
    public List<AiAssistantTurn> Turns { get; } = [];
    public List<AiAssistantRequestReceipt> Receipts { get; } = [];
    public List<AiAssistantToolExecution> Tools { get; } = [];
    public AiConnectionSnapshot Connection { get; set; } = new(AiProvider.OpenAI, "model", "cipher-key", true, 1);
    public int BudgetReads { get; private set; }
    public int ConnectionReads { get; private set; }
    public Guid? LastReadActor { get; private set; }
    public Guid? LastReadRequest { get; private set; }
    private void RequireTransaction()
    {
        if (!work.InTransaction) throw new InvalidOperationException("Operation writes and locks require their owning transaction.");
    }
    public Task LockAsync(CancellationToken ct) { RequireTransaction(); return Task.CompletedTask; }
    public Task LockBudgetAsync(CancellationToken ct) { RequireTransaction(); return Task.CompletedTask; }
    public Task LockActorAsync(Guid actor, CancellationToken ct) { RequireTransaction(); return Task.CompletedTask; }
    public Task LockConversationAsync(Guid conversation, CancellationToken ct) { RequireTransaction(); return Task.CompletedTask; }
    public Task<AiConversation?> GetConversationAsync(Guid actor, Guid conversation, CancellationToken ct) =>
        Task.FromResult(Conversations.SingleOrDefault(item => item.OwnerUserId == actor && item.Id == conversation && item.State == AiConversationState.Active));
    public Task<AiAssistantTurn?> GetTurnAsync(Guid turn, CancellationToken ct) => Task.FromResult(Turns.SingleOrDefault(item => item.Id == turn));
    public Task<AiAssistantRequestReceipt?> GetReceiptAsync(Guid actor, Guid request, CancellationToken ct) =>
        Task.FromResult(Receipts.SingleOrDefault(item => item.OwnerUserId == actor && item.RequestId == request && item.ExpiresAtUtc > clock.GetUtcNow().UtcDateTime));
    public Task<AiAssistantTurn?> GetPendingAsync(CancellationToken ct) => Task.FromResult(Turns.FirstOrDefault(item => item.State == AiAssistantTurnState.Pending));
    public Task<AiAssistantTurn?> GetActiveForActorChannelAsync(Guid actor, string channelId, CancellationToken ct) =>
        Task.FromResult(Turns.FirstOrDefault(item => item.OwnerUserId == actor && item.ChannelId == channelId && item.State is AiAssistantTurnState.Pending or AiAssistantTurnState.Running));
    public Task<AiAssistantTurn?> GetActiveForConversationAsync(Guid conversation, CancellationToken ct) =>
        Task.FromResult(Turns.FirstOrDefault(item => item.ConversationId == conversation && item.State is AiAssistantTurnState.Pending or AiAssistantTurnState.Running));
    public Task<int> CountTurnsAsync(Guid conversation, CancellationToken ct) => Task.FromResult(Turns.Count(item => item.ConversationId == conversation));
    public Task<AiAssistantBudgetCounts> CountBudgetAsync(Guid actor, DateOnly day, string? channelId, bool? isAudio, CancellationToken ct)
    {
        BudgetReads++;
        return Task.FromResult(new AiAssistantBudgetCounts(Receipts.Count(item => item.OwnerUserId == actor && item.AcceptedDayUtc == day
                && (channelId is null || isAudio is null || (item.ChannelId is null || item.ChannelId == channelId)
                    && (item.IsAudio is null || item.IsAudio == isAudio))),
            Receipts.Count(item => item.AcceptedDayUtc == day), Turns.Count(item => item.State == AiAssistantTurnState.Pending), Turns.Count(item => item.State == AiAssistantTurnState.Running)));
    }
    public void AddConversation(AiConversation conversation) { RequireTransaction(); Conversations.Add(conversation); }
    public void AddTurn(AiAssistantTurn turn) { RequireTransaction(); Turns.Add(turn); }
    public void AddReceipt(AiAssistantRequestReceipt receipt) { RequireTransaction(); Receipts.Add(receipt); }
    public void AddTool(AiAssistantToolExecution execution) { RequireTransaction(); Tools.Add(execution); }
    public Task<AiConnectionSnapshot> FindAsync(CancellationToken ct) { ConnectionReads++; return Task.FromResult(Connection); }
    public Task<AiAssistantTurnRow?> FindTurnByRequestAsync(Guid actor, Guid request, CancellationToken ct)
    {
        LastReadActor = actor; LastReadRequest = request;
        var turn = Turns.SingleOrDefault(item => item.OwnerUserId == actor && item.RequestId == request);
        return Task.FromResult(turn is null ? null : Project(turn));
    }
    public Task<AiAssistantTurnRow?> FindTurnAsync(Guid turnId, CancellationToken ct)
    {
        var turn = Turns.SingleOrDefault(item => item.Id == turnId);
        return Task.FromResult(turn is null ? null : Project(turn));
    }
    private AiAssistantTurnRow Project(AiAssistantTurn turn)
    {
        var conversation = Conversations.Single(item => item.Id == turn.ConversationId);
        return new(turn.Id, turn.OwnerUserId, turn.ConversationId, turn.RequestId, turn.Ordinal, turn.State, turn.Stage, turn.Revision,
            conversation.Revision, conversation.RequiredPermissions, turn.ProtectedQuestion, turn.ProtectedAnswer, turn.ProtectedEvidences,
            turn.PayloadRevision, turn.Culture, turn.TimeZoneId, turn.Provider, turn.Model, turn.ConnectionRevision, turn.AttemptId, turn.Fence,
            turn.SubmittedAtUtc, turn.ContentExpiresAtUtc, turn.DeadlineAtUtc, turn.CancelRequestedAtUtc, turn.ProviderSteps, turn.ToolCalls,
            turn.ToolBytes, turn.InputTokens, turn.OutputTokens, turn.UsageUnknown, turn.FailureCode)
        { AudioSeconds = turn.AudioSeconds, ChannelId = conversation.ChannelId, ExecutionProfileJson = turn.ExecutionProfileJson,
            ToolResults = Tools.Where(tool => tool.TurnId == turn.Id).Select(tool => new AiAssistantToolResultRow(tool.Ordinal, tool.ToolId, tool.Version, tool.ProtectedResult)).ToArray() };
    }
    public Task<AiConnection?> GetAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<AiConnectionChange?> GetChangeAsync(Guid actor, Guid request, CancellationToken ct) => throw new NotSupportedException();
    public void Add(AiConnection connection) => throw new NotSupportedException();
    public void AddChange(AiConnectionChange change) => throw new NotSupportedException();
    public Task DeleteExpiredChangesAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<AiConnectionChangeResponse?> FindChangeAsync(Guid actor, Guid request, CancellationToken ct) => throw new NotSupportedException();
    public Task<AiConversationRow?> FindConversationAsync(Guid actor, Guid conversation, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<AiConversationRow>> ListConversationsAsync(Guid actor, ListAiConversationsRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<AiAssistantTurnRow>> ListTurnsAsync(Guid actor, Guid conversation, ListAiAssistantTurnsRequest request, CancellationToken ct)
    {
        var rows=Turns.Where(turn=>turn.OwnerUserId==actor && turn.ConversationId==conversation).OrderByDescending(turn=>turn.Ordinal).ToArray();
        return Task.FromResult(new PagedResult<AiAssistantTurnRow>(rows.Skip((request.Page-1)*request.PageSize).Take(request.PageSize).Select(Project).ToArray(),request.Page,request.PageSize,rows.Length));
    }
    public Task<AiAssistantWorkloadRow> FindWorkloadAsync(DateTime now, int grace, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<AiAssistantTurn>> ListExpiredAsync(DateTime now, int pending, int grace, int limit, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<AiAssistantTurn>> ListPurgeableAsync(DateTime now, int limit, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<AiConversation>> ListRemovableConversationsAsync(DateTime now, int limit, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<AiAssistantRequestReceipt>> ListExpiredReceiptsAsync(DateTime now, int limit, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<AiAssistantToolExecution>> ListToolsAsync(Guid turn, CancellationToken ct) => Task.FromResult<IReadOnlyList<AiAssistantToolExecution>>(Tools.Where(item => item.TurnId == turn).ToArray());
    public void RemoveTool(AiAssistantToolExecution execution) => throw new NotSupportedException();
    public void RemoveReceipt(AiAssistantRequestReceipt receipt) => throw new NotSupportedException();
    public void RemoveConversation(AiConversation conversation) => throw new NotSupportedException();
}
