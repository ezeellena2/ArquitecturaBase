using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Channels;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;

// ReadDocument with the real Context, Execution, Dispatch and Get, a sealed reading turn and a fictional descriptor: no private store,
// HTTP or PostgreSQL behind it.
internal sealed class AssistantDocumentExecutionFixture
{
    public AssistantOperationsFixture Inner { get; }
    public AiAssistantLimits Limits { get; }
    public AiAssistantCapabilityCatalogPolicy Catalog { get; }
    public AiAssistantAccessGuard Guard { get; }
    public AiAssistantExecutionProfileSnapshot Profile { get; }
    public AiAssistantTurn Turn { get; }
    public AssistantDocumentInput Document { get; }
    public AiAssistantClaimResponse Claim { get; }
    public AiAssistantContextPolicy ContextPolicy { get; }
    public AiAssistantExecutionService Execution { get; }
    public AiAssistantDispatchService Dispatch { get; }
    public DocumentConversationClient Client { get; } = new();
    public RecordingAssistantOperationCompletionSignal CompletionSignal { get; } = new();

    public AssistantDocumentExecutionFixture(string culture = "en", bool reasoning = false, AiAssistantLimits? limits = null)
    {
        Limits = limits ?? new AiAssistantLimits();
        Inner = new(qualified: false, documentQualified: true, limits: Limits);
        if (reasoning) Inner.DocumentProfiles[0] = Inner.DocumentProfiles[0] with { SupportsReasoningEffort = true };
        Inner.Permissions.Permissions[Inner.ActorId] = ["fictional.channel.read"];
        var now = Inner.Clock.GetUtcNow().UtcDateTime;
        var channel = Inner.Context with { SubjectUserId = null, Culture = culture, RequiredPermissions = new(["fictional.channel.read"], []),
            AllowedCapabilities = [], AllowedTerminals = [] };
        Profile = new("fixture-document", 4, "document-read-v1", 1, channel) { Purpose = AiAssistantPurpose.ReadDocument, OccurredAtUtc = now.AddMinutes(-1) };
        var conversation = AiConversation.Create(Inner.ActorId, now, channel.ChannelId);
        Turn = AiAssistantTurn.Create(Inner.ActorId, conversation.Id, Guid.NewGuid(), 1, "", culture, "UTC", AiProvider.OpenAI,
            "vision-model", 1, now, now.AddHours(1), channel.ChannelId);
        Document = new("documents/private-object.pdf", "Comprobante.pdf", "application/pdf", 2048, new string('a', 64), 2, Turn.ContentExpiresAtUtc);
        Turn.SealExecutionProfile(JsonSerializer.Serialize(Profile, AiAssistantResponsePolicy.JsonOptions));
        ProtectDescriptor(Document);
        Turn.Claim(Guid.NewGuid(), now.AddSeconds(90));
        Inner.Store.Conversations.Add(conversation);
        Inner.Store.Turns.Add(Turn);
        Claim = new(Turn.Id, Inner.ActorId, conversation.Id, Turn.RequestId, Turn.Fence, Turn.AttemptId!.Value, Turn.DeadlineAtUtc!.Value, culture);
        Catalog = new([Inner.Source], Inner.Permissions);
        Guard = new(Inner.Users, Inner.Permissions, [], Limits, new FakeCurrentUser(), Catalog, new([], Inner.DocumentProfiles, new MemoryPrivateFileStore()));
        var responses = new AiAssistantResponsePolicy(Inner.Protector, Inner.Clock, Catalog);
        ContextPolicy = new(Inner.Store, Inner.Store, Guard, responses, new AiConnectionCandidatePolicy(new DocumentSecret()), Catalog, Limits, Inner.Clock);
        Execution = new(ContextPolicy, Guard, Catalog, null!, Client, Limits, Inner.Clock, NullLogger<AiAssistantExecutionService>.Instance);
        Dispatch = new(Inner.Store, Inner.Work, new(Inner.Store, Inner.Store, Inner.Store, Guard, Catalog), Inner.Protector, Limits, Inner.Clock,
            NullLogger<AiAssistantDispatchService>.Instance, CompletionSignal);
    }

    public static StructuredDocumentReading Reading(string text = "| Monto | $ 1.000 |") => new(StructuredDocumentReadability.Readable, null, "es", text, false);

    public void ProtectDescriptor(AssistantDocumentInput descriptor) => Turn.ProtectQuestion(Inner.Protector.Protect(Inner.ActorId, Turn.ConversationId,
        Turn.Id, "question", Turn.PayloadRevision, AiAssistantDocumentPolicy.Serialize(descriptor)));

    public async Task<AiAssistantExecutionContextResponse> ContextAsync(CancellationToken ct)
    {
        var row = (await Inner.Store.FindTurnAsync(Turn.Id, ct))!;
        return new(Claim, row, new(AiProvider.OpenAI, "vision-model", "fixture-document-key", "document instructions", [], [], [], null, 8192, 262144, 131072)
        { Document = Document, DocumentMaximumBytes = 10 * 1024 * 1024 }, [], [], 0, 0) { ExecutionProfile = Profile };
    }

    private sealed class DocumentSecret : IAiSecretProtector
    {
        public string Protect(AiProvider provider, string key) => throw new NotSupportedException();
        public string? Unprotect(AiProvider provider, string key) => "fixture-document-key";
    }
}

internal sealed class DocumentConversationClient : ILlmConversationClient
{
    public int Calls { get; private set; }
    public LlmConversationRequest? Last { get; private set; }
    public Task<Result<LlmConversationResponse>> SendAsync(LlmConversationRequest request, CancellationToken ct)
    {
        Calls++;
        Last = request;
        return Task.FromResult<Result<LlmConversationResponse>>(new LlmConversationResponse(LlmDocumentReadingResponse.Read, [], null, new(1200, 300))
            { DocumentReading = AssistantDocumentExecutionFixture.Reading() });
    }
}
