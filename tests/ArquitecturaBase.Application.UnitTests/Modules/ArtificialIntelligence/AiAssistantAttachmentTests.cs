using System.Text.Json;
using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Channels.SystemQueries;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// Stage 2: a spreadsheet goes with an interpretation, is sealed in its turn and stays the conversation's until another replaces it.
public sealed class AiAssistantAttachmentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly AssistantCapabilityReference Import = new("fixture.import", 1);

    private static AssistantOperationRequest WithSpreadsheet(AssistantOperationsFixture fixture, string key = "fixture/attachments/clientes.xlsx")
    {
        var request = fixture.Request();
        return request with { Attachment = FictionalAttachment.Input(request.ContentExpiresAtUtc, key) };
    }

    private static string? Reveal(AssistantOperationsFixture fixture, AiAssistantTurn turn, string purpose = "attachment") =>
        fixture.Protector.Unprotect(turn.OwnerUserId, turn.ConversationId, turn.Id, purpose, turn.PayloadRevision,
            purpose == "attachment" ? turn.ProtectedAttachment! : turn.ProtectedQuestion!);

    private static AssistantAttachmentInput AttachTo(AssistantOperationsFixture fixture, AiAssistantTurn turn, string key, DateTime expiresAtUtc)
    {
        var attachment = FictionalAttachment.Input(expiresAtUtc, key);
        turn.Attach(key, fixture.Protector.Protect(turn.OwnerUserId, turn.ConversationId, turn.Id, "attachment", turn.PayloadRevision,
            FictionalAttachment.Json(attachment)));
        return attachment;
    }

    private static IReadOnlyList<AssistantCapabilityReference> Offered(AiAssistantTurn turn) =>
        AiAssistantChannelPolicy.Read(turn.ExecutionProfileJson).Value.ChannelContext.AllowedCapabilities;

    private static AssistantCapabilityDefinition RequiringSpreadsheet() =>
        PreparedAssistantCapabilitySource.Definition(Import.Id, Import.Version, AssistantCapabilityKind.Proposal)
            with { RequiresAttachment = true, OfferedInHome = true };

    [Fact]
    public async Task An_interpretation_seals_its_spreadsheet_apart_from_the_text()
    {
        var fixture = new AssistantOperationsFixture();
        var request = WithSpreadsheet(fixture);
        Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsSuccess);
        var turn = fixture.Store.Turns.Single(row => row.RequestId == request.RequestId);
        Assert.Equal(request.Attachment!.StorageKey, turn.AttachmentKey);
        Assert.Equal(FictionalAttachment.Json(request.Attachment), Reveal(fixture, turn));
        Assert.Equal(request.Text, Reveal(fixture, turn, "question"));
        Assert.Equal(1, fixture.Work.Commits);
    }

    [Theory]
    [InlineData("audio")]
    [InlineData("noText")]
    [InlineData("expired")]
    [InlineData("key")]
    [InlineData("longKey")]
    [InlineData("path")]
    [InlineData("controlName")]
    [InlineData("emptySize")]
    [InlineData("size")]
    [InlineData("upperHash")]
    [InlineData("shortHash")]
    [InlineData("prepared")]
    public async Task A_spreadsheet_is_bounded_and_goes_only_with_a_written_interpretation(string invalid)
    {
        var fixture = new AssistantOperationsFixture(audioQualified: true);
        var request = WithSpreadsheet(fixture);
        var attachment = request.Attachment!;
        request = invalid switch
        {
            "audio" => request with { Text = "", Context = fixture.Context with { SubjectUserId = null,
                RequiredPermissions = new(["fictional.channel.read"], []), AllowedCapabilities = [], AllowedTerminals = [] },
                Audio = new("audio/private-object", "audio.ogg", "audio/ogg; codecs=opus", 42, new string('a', 64), request.ContentExpiresAtUtc) },
            "noText" => request with { Text = " " },
            "expired" => request with { Attachment = attachment with { ExpiresAtUtc = request.OccurredAtUtc } },
            "key" => request with { Attachment = attachment with { StorageKey = " " } },
            "longKey" => request with { Attachment = attachment with { StorageKey = new string('k', 501) } },
            "path" => request with { Attachment = attachment with { Name = "otra-carpeta/clientes.xlsx" } },
            "controlName" => request with { Attachment = attachment with { Name = "clientes\n.xlsx" } },
            "emptySize" => request with { Attachment = attachment with { Size = 0 } },
            "size" => request with { Attachment = attachment with { Size = AssistantAttachmentInput.MaximumBytes + 1 } },
            "upperHash" => request with { Attachment = attachment with { Sha256 = new string('C', 64) } },
            "shortHash" => request with { Attachment = attachment with { Sha256 = new string('c', 63) } },
            _ => request with { Attachment = attachment with
                { Spreadsheet = FictionalAttachment.Spreadsheet(new string('x', StructuredSpreadsheet.MaximumBytes)) } },
        };
        Assert.Equal(AiAssistantErrors.Invalid, (await fixture.Service.SubmitAsync(request, Ct)).Error);
        Assert.Equal(0, fixture.Work.Transactions);
        Assert.Empty(fixture.Store.Turns);
    }

    [Fact]
    public async Task A_spreadsheet_request_replays_and_its_id_cannot_carry_another_spreadsheet()
    {
        var fixture = new AssistantOperationsFixture();
        var request = WithSpreadsheet(fixture);
        Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsSuccess);
        Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsSuccess);
        Assert.Single(fixture.Store.Turns);
        Assert.Equal(AiAssistantErrors.RequestReused, (await fixture.Service.SubmitAsync(
            request with { Attachment = request.Attachment! with { Sha256 = new string('d', 64) } }, Ct)).Error);
        Assert.Equal(AiAssistantErrors.RequestReused, (await fixture.Service.SubmitAsync(request with { Attachment = null }, Ct)).Error);
    }

    [Fact]
    public async Task A_turn_keeps_its_content_no_longer_than_its_spreadsheet()
    {
        var fixture = new AssistantOperationsFixture();
        var request = fixture.Request();
        var expiresAtUtc = fixture.Clock.GetUtcNow().UtcDateTime.AddMinutes(30);
        request = request with { Attachment = FictionalAttachment.Input(expiresAtUtc) };
        Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsSuccess);
        Assert.Equal(expiresAtUtc, fixture.Store.Turns.Single().ContentExpiresAtUtc);
    }

    [Fact]
    public async Task A_continuation_keeps_the_spreadsheet_of_its_conversation_sealed_again_for_its_own_turn()
    {
        var fixture = new AssistantOperationsFixture();
        var previous = fixture.SeedCompleted().Turn;
        var attachment = AttachTo(fixture, previous, "fixture/attachments/previous.xlsx", previous.ContentExpiresAtUtc.AddDays(1));
        var request = fixture.Request() with { PreviousRequestId = previous.RequestId };
        Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsSuccess);
        var turn = fixture.Store.Turns.Single(row => row.RequestId == request.RequestId);
        Assert.Equal(previous.AttachmentKey, turn.AttachmentKey);
        Assert.NotEqual(previous.ProtectedAttachment, turn.ProtectedAttachment);
        Assert.Equal(FictionalAttachment.Json(attachment), Reveal(fixture, turn));
        // What is inherited is not part of the request: replaying it recovers the same turn.
        Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsSuccess);
        Assert.Equal(2, fixture.Store.Turns.Count);
    }

    [Fact]
    public async Task A_new_spreadsheet_replaces_the_one_of_the_conversation()
    {
        var fixture = new AssistantOperationsFixture();
        var previous = fixture.SeedCompleted().Turn;
        AttachTo(fixture, previous, "fixture/attachments/previous.xlsx", previous.ContentExpiresAtUtc);
        var request = WithSpreadsheet(fixture, "fixture/attachments/new.csv") with { PreviousRequestId = previous.RequestId };
        Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsSuccess);
        var turn = fixture.Store.Turns.Single(row => row.RequestId == request.RequestId);
        Assert.Equal("fixture/attachments/new.csv", turn.AttachmentKey);
        Assert.Equal(FictionalAttachment.Json(request.Attachment!), Reveal(fixture, turn));
    }

    [Fact]
    public async Task A_Home_turn_with_a_spreadsheet_may_come_without_text()
    {
        var host = new Host();
        var request = host.Home("") with { Attachment = FictionalAttachment.Input(host.Now.AddDays(30)) };
        Assert.True((await host.SubmitAsync(request)).IsSuccess);
        var turn = host.Turn(request.RequestId);
        Assert.Equal(request.Attachment!.StorageKey, turn.AttachmentKey);
        Assert.Equal("", Reveal(host.Fixture, turn, "question"));
        Assert.Equal(FictionalAttachment.Json(request.Attachment), Reveal(host.Fixture, turn));
    }

    [Fact]
    public async Task A_Home_spreadsheet_is_bounded_like_a_channel_one()
    {
        var host = new Host();
        var request = host.Home() with { Attachment = FictionalAttachment.Input(host.Now.AddDays(30)) with { Name = "a/b.xlsx" } };
        Assert.Equal(AiAssistantErrors.Invalid, (await host.SubmitAsync(request)).Error);
        Assert.Empty(host.Fixture.Store.Turns);
        Assert.Equal(0, host.Fixture.Work.Commits);
    }

    [Fact]
    public async Task A_Home_turn_keeps_the_spreadsheet_of_its_conversation_and_a_new_conversation_starts_without_one()
    {
        var host = new Host();
        var (conversation, first) = host.SeedTurn(null, "ai/assistant/attachments/first.xlsx", host.Now.AddDays(30), host.Now.AddDays(5));
        var next = host.Home("Agregale la columna de la zona", conversation);
        Assert.True((await host.SubmitAsync(next)).IsSuccess);
        var turn = host.Turn(next.RequestId);
        Assert.Equal(first.AttachmentKey, turn.AttachmentKey);
        Assert.Equal(host.Now.AddDays(5), turn.ContentExpiresAtUtc);
        var fresh = host.Home("Otra cosa");
        Assert.True((await host.SubmitAsync(fresh)).IsSuccess);
        Assert.Null(host.Turn(fresh.RequestId).AttachmentKey);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("purged")]
    [InlineData("replaced")]
    public async Task A_Home_turn_does_not_bring_back_a_spreadsheet_without_content_or_one_already_replaced(string reason)
    {
        var host = new Host();
        var (conversation, first) = host.SeedTurn(null, "ai/assistant/attachments/first.xlsx",
            reason == "expired" ? host.Now.AddHours(1) : host.Now.AddDays(30));
        if (reason == "purged") first.PurgeContent();
        if (reason == "replaced") host.SeedTurn(conversation, "ai/assistant/attachments/second.xlsx", host.Now.AddHours(1));
        if (reason != "purged") host.Fixture.Clock.Advance(TimeSpan.FromHours(2));
        var next = host.Home("Seguí con la planilla", conversation);
        Assert.True((await host.SubmitAsync(next)).IsSuccess);
        Assert.Null(host.Turn(next.RequestId).AttachmentKey);
    }

    [Fact]
    public async Task Home_offers_what_requires_a_spreadsheet_only_in_a_turn_that_has_one()
    {
        var host = new Host(RequiringSpreadsheet());
        var plain = host.Home();
        Assert.True((await host.SubmitAsync(plain)).IsSuccess);
        Assert.DoesNotContain(Import, Offered(host.Turn(plain.RequestId)));
        var withSheet = host.Home() with { Attachment = FictionalAttachment.Input(host.Now.AddDays(30)) };
        Assert.True((await host.SubmitAsync(withSheet)).IsSuccess);
        Assert.Contains(Import, Offered(host.Turn(withSheet.RequestId)));
    }

    [Fact]
    public async Task A_channel_offers_what_requires_a_spreadsheet_only_in_a_turn_that_has_one()
    {
        var host = new Host(RequiringSpreadsheet());
        var plain = host.Operation();
        Assert.True((await host.SubmitAsync(plain)).IsSuccess);
        Assert.Equal([new AssistantCapabilityReference("fixture.proposal", 1)], Offered(host.Turn(plain.RequestId)));
        host.Turn(plain.RequestId).RequestCancellation(host.Now);
        var withSheet = host.Operation() with { Attachment = FictionalAttachment.Input(host.Now.AddHours(1)) };
        Assert.True((await host.SubmitAsync(withSheet)).IsSuccess);
        Assert.Equal([new AssistantCapabilityReference("fixture.proposal", 1), Import], Offered(host.Turn(withSheet.RequestId)));
    }

    [Fact]
    public async Task The_model_receives_what_requires_the_spreadsheet_and_the_spreadsheet_itself_once_the_seal_is_checked_again()
    {
        var host = new Host(RequiringSpreadsheet());
        var request = host.Operation() with { Attachment = FictionalAttachment.Input(host.Now.AddHours(1)) };
        Assert.True((await host.SubmitAsync(request)).IsSuccess);
        var prepared = await host.PrepareAsync();
        Assert.True(prepared.IsSuccess);
        Assert.Contains(prepared.Value.ProviderRequest.Tools, tool => tool.Name == Import.Id);
        Assert.Equal(FictionalAttachment.Json(request.Attachment!.Spreadsheet), FictionalAttachment.Json(prepared.Value.Attachment!));
        Assert.EndsWith(FictionalAttachment.Json(request.Attachment.Spreadsheet), prepared.Value.ProviderRequest.Messages[^1].Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Each_capability_call_receives_the_prepared_spreadsheet_of_its_turn(bool attached)
    {
        var scenario = new ObservedReadAssistantScenario(AiAssistantDispatchServiceTests.CreateObservedReadStore);
        var spreadsheet = attached ? FictionalAttachment.Spreadsheet() : null;
        var read = await scenario.Execution.AdvanceAsync(scenario.Context() with { Attachment = spreadsheet },
            new("tools", [new("read-call", ObservedReadAssistantSource.ReadId, "{}")], "read", null), Ct);
        Assert.True(read.IsSuccess);
        var proposal = await scenario.Execution.AdvanceAsync(read.Value.Context, new("tools",
            [new("proposal-call", ObservedReadAssistantSource.ProposalId, JsonSerializer.Serialize(new { resourceId = scenario.Source.ResourceId }))], "proposal", null), Ct);
        Assert.True(proposal.IsSuccess);
        Assert.Same(spreadsheet, scenario.Source.LastProposalRequest!.Attachment);
    }

    // Home and a channel on the real acceptance, with the five core Home queries and an optional capability as metadata only.
    private sealed class Host
    {
        private readonly AiAssistantSubmissionPolicy submissions;
        private readonly AiAssistantAccessGuard guard;
        private readonly AiAssistantCapabilityCatalogPolicy catalog;
        private readonly AiAssistantLimits limits = new();
        public AssistantOperationsFixture Fixture { get; } = new();
        public Guid ActorId => Fixture.ActorId;
        public DateTime Now => Fixture.Clock.GetUtcNow().UtcDateTime;

        public Host(params AssistantCapabilityDefinition[] extra)
        {
            Fixture.Permissions.Permissions[ActorId] = [ArtificialIntelligencePermissions.AssistantUse, "fictional.channel.read", "fictional.channel.send"];
            catalog = new AiAssistantCapabilityCatalogPolicy([new HomeQuerySource(), Fixture.Source, new PreparedAssistantCapabilitySource(extra)], Fixture.Permissions);
            guard = new AiAssistantAccessGuard(Fixture.Users, Fixture.Permissions, Fixture.Profiles, limits,
                new FakeCurrentUser { UserId = ActorId }, catalog, [], new MemoryPrivateFileStore());
            submissions = new(Fixture.Store, Fixture.Store, Fixture.Store, guard, Fixture.Protector, new OperationsFingerprint(), limits, Fixture.Clock);
        }

        public SubmitAiAssistantTurnRequest Home(string question = "Armame un catálogo con esta planilla", AiConversation? conversation = null) =>
            new(Guid.NewGuid(), conversation?.Id, conversation?.Revision, Fixture.Store.Connection.Revision, question, "es");

        public AssistantOperationRequest Operation() =>
            Fixture.Request() with { Context = Fixture.Context with { AllowedCapabilities = [new("fixture.proposal", 1), Import] } };

        public Task<Result<AiAssistantTurnAcceptedResponse>> SubmitAsync(SubmitAiAssistantTurnRequest request) =>
            Fixture.Work.ExecuteInTransactionAsync(ct => submissions.AcceptAsync(ActorId, request, ct), CommitPolicy.OnSuccess, Ct);

        public async Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request) =>
            submissions.ValidateOperation(request) is { } invalid ? invalid
                : await Fixture.Work.ExecuteInTransactionAsync(ct => submissions.AcceptAsync(request, ct), CommitPolicy.OnSuccess, Ct);

        public AiAssistantTurn Turn(Guid requestId) => Fixture.Store.Turns.Single(turn => turn.RequestId == requestId);

        public async Task<Result<AiAssistantExecutionContextResponse>> PrepareAsync()
        {
            var dispatch = new AiAssistantDispatchService(Fixture.Store, Fixture.Work, new(Fixture.Store, Fixture.Store, Fixture.Store, guard, catalog),
                Fixture.Protector, limits, Fixture.Clock, NullLogger<AiAssistantDispatchService>.Instance, new DisabledAssistantOperationCompletionSignal());
            var claimed = await dispatch.ClaimAsync(Ct);
            Assert.True(claimed.IsSuccess);
            return await new AiAssistantContextPolicy(Fixture.Store, Fixture.Store, guard, new AiAssistantResponsePolicy(Fixture.Protector, Fixture.Clock, catalog),
                new AiConnectionCandidatePolicy(new PlainSecretProtector()), catalog, limits, Fixture.Clock).PrepareAsync(claimed.Value!, Ct);
        }

        // A completed Home turn, with a spreadsheet when it names one.
        public (AiConversation Conversation, AiAssistantTurn Turn) SeedTurn(AiConversation? conversation, string? attachmentKey,
            DateTime contentExpiresAtUtc, DateTime? attachmentExpiresAtUtc = null)
        {
            if (conversation is null)
            {
                conversation = AiConversation.Create(ActorId, Now, "home");
                Fixture.Store.Conversations.Add(conversation);
            }
            var ordinal = Fixture.Store.Turns.Count(turn => turn.ConversationId == conversation.Id) + 1;
            var turn = AiAssistantTurn.Create(ActorId, conversation.Id, Guid.NewGuid(), ordinal, "", "es", "UTC", AiProvider.OpenAI, "model", 1,
                Now, contentExpiresAtUtc);
            turn.ProtectQuestion(Fixture.Protector.Protect(ActorId, conversation.Id, turn.Id, "question", turn.PayloadRevision, "Pedido anterior"));
            if (attachmentKey is not null) AttachTo(Fixture, turn, attachmentKey, attachmentExpiresAtUtc ?? contentExpiresAtUtc);
            Assert.True(turn.Claim(Guid.NewGuid(), Now.AddSeconds(90)).IsSuccess);
            var answer = JsonSerializer.Serialize(new AiAssistantAnswer("answer", "Listo.", []), JsonSerializerOptions.Web);
            Assert.True(turn.Complete(turn.Fence, Fixture.Protector.Protect(ActorId, conversation.Id, turn.Id, "answer", turn.PayloadRevision, answer),
                Fixture.Protector.Protect(ActorId, conversation.Id, turn.Id, "evidences", turn.PayloadRevision, "[]"), Now).IsSuccess);
            Fixture.Store.Turns.Add(turn);
            return (conversation, turn);
        }
    }

    private sealed class PlainSecretProtector : IAiSecretProtector
    {
        public string Protect(AiProvider provider, string apiKey) => throw new NotSupportedException();
        public string? Unprotect(AiProvider provider, string protectedApiKey) => "fictional-key";
    }

    // The five core Home queries, as metadata only: accepting a turn never runs or parses a capability.
    private sealed class HomeQuerySource : IAssistantCapabilitySource
    {
        public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } =
            new[] { "users.summary", "roles.list", "roles.permissions", "system.help", "business.information" }.Select(id =>
                new AssistantCapabilityDefinition(id, 1, AssistantCapabilityKind.Read, "fixture",
                    """{"type":"object","properties":{},"required":[],"additionalProperties":false}""",
                    SystemQueryCapabilityCodec.OutputSchemaJson, new([], []), new(4096, 16384, 50, 5))).ToArray();
        public Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Accepting a turn never runs a capability.");
        public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
            throw new InvalidOperationException("Accepting a turn never parses an output.");
    }
}
