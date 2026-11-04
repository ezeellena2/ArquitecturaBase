using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// «Cambiar algo» (IA3-A): the next message of a channel continues its previous operation in that engine conversation, like Home.
public sealed class AiAssistantOperationContinuationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AiAssistantTurn Canceled(AssistantOperationsFixture fixture, AiConversation conversation, int ordinal)
    {
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        var turn = AiAssistantTurn.Create(fixture.ActorId, conversation.Id, Guid.NewGuid(), ordinal, "", "en", "UTC",
            AiProvider.OpenAI, "model", 1, now, now.AddHours(1), conversation.ChannelId);
        turn.RequestCancellation(now);
        fixture.Store.Turns.Add(turn);
        fixture.Store.Receipts.Add(AiAssistantRequestReceipt.Create(fixture.ActorId, turn.RequestId, "fixture-fingerprint", "test",
            conversation.Id, turn.Id, now, now.AddDays(30), conversation.ChannelId, false));
        return turn;
    }

    [Fact]
    public async Task A_continuation_adds_the_turn_to_the_conversation_of_the_previous_operation()
    {
        var fixture = new AssistantOperationsFixture();
        var previous = fixture.SeedCompleted().Turn;
        var request = fixture.Request() with { PreviousRequestId = previous.RequestId };
        var accepted = await fixture.Service.SubmitAsync(request, Ct);
        Assert.True(accepted.IsSuccess);
        var turn = fixture.Store.Turns.Single(row => row.RequestId == request.RequestId);
        Assert.Equal(previous.ConversationId, turn.ConversationId);
        Assert.Equal(2, turn.Ordinal);
        Assert.Single(fixture.Store.Conversations);
        Assert.Equal(previous.ConversationId, fixture.Store.Receipts.Single(row => row.RequestId == request.RequestId).ConversationId);
        Assert.Equal(1, fixture.Work.Commits);
        Assert.Equal(0, fixture.Protector.Reads);
    }

    [Fact]
    public async Task A_continuation_replays_by_its_request_and_the_same_request_without_the_previous_operation_is_reused()
    {
        var fixture = new AssistantOperationsFixture();
        var previous = fixture.SeedCompleted().Turn;
        var request = fixture.Request() with { PreviousRequestId = previous.RequestId };
        Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsSuccess);
        Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsSuccess);
        Assert.Equal(2, fixture.Store.Turns.Count);
        Assert.Equal(AiAssistantErrors.RequestReused, (await fixture.Service.SubmitAsync(request with { PreviousRequestId = null }, Ct)).Error);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("other-channel")]
    [InlineData("other-conversation")]
    [InlineData("not-completed")]
    [InlineData("not-latest")]
    [InlineData("expired")]
    [InlineData("blocked")]
    public async Task A_continuation_needs_the_latest_completed_and_readable_operation_of_the_same_channel_conversation(string reason)
    {
        var fixture = new AssistantOperationsFixture();
        var previous = fixture.SeedCompleted().Turn;
        var conversation = fixture.Store.Conversations.Single();
        if (reason == "expired") fixture.Clock.Advance(TimeSpan.FromHours(3));
        var request = fixture.Request() with { PreviousRequestId = previous.RequestId };
        switch (reason)
        {
            case "unknown": request = request with { PreviousRequestId = Guid.NewGuid() }; break;
            case "other-channel": request = request with { Context = fixture.Context with { ChannelId = "another-operation-channel" } }; break;
            case "other-conversation": request = request with { Context = fixture.Context with { ChannelConversationId = Guid.NewGuid() } }; break;
            case "not-completed": request = request with { PreviousRequestId = Canceled(fixture, conversation, 2).RequestId }; break;
            case "not-latest": Canceled(fixture, conversation, 2); break;
            case "blocked": conversation.RequirePermissions(["fictional.secret.read"]); break;
        }
        var expected = reason switch
        {
            "not-latest" => AiAssistantErrors.RevisionConflict,
            "expired" => AiAssistantErrors.Expired,
            "blocked" => AiAssistantErrors.ContentBlocked,
            _ => AiAssistantErrors.NotFound,
        };
        var result = await fixture.Service.SubmitAsync(request, Ct);
        Assert.Equal(expected, result.Error);
        Assert.DoesNotContain(fixture.Store.Turns, row => row.RequestId == request.RequestId);
        Assert.DoesNotContain(fixture.Store.Receipts, row => row.RequestId == request.RequestId);
        Assert.Single(fixture.Store.Conversations);
        Assert.Equal(0, fixture.Protector.Reads);
    }

    [Fact]
    public async Task A_continuation_names_another_operation_and_never_an_audio()
    {
        var fixture = new AssistantOperationsFixture(audioQualified: true);
        var request = fixture.Request();
        var audio = request with { Text = "", Context = fixture.Context with {
            SubjectUserId = null, RequiredPermissions = new(["fictional.channel.read"], []), AllowedCapabilities = [], AllowedTerminals = [] },
            Audio = new("audio/private-object", "audio.ogg", "audio/ogg; codecs=opus", 42, new string('a', 64), request.ContentExpiresAtUtc) };
        Assert.True((await fixture.Service.SubmitAsync(audio with { RequestId = Guid.NewGuid() }, Ct)).IsSuccess);
        Assert.Equal(AiAssistantErrors.Invalid, (await fixture.Service.SubmitAsync(request with { PreviousRequestId = Guid.Empty }, Ct)).Error);
        Assert.Equal(AiAssistantErrors.Invalid, (await fixture.Service.SubmitAsync(request with { PreviousRequestId = request.RequestId }, Ct)).Error);
        Assert.Equal(AiAssistantErrors.Invalid, (await fixture.Service.SubmitAsync(audio with { PreviousRequestId = Guid.NewGuid() }, Ct)).Error);
        Assert.Equal(1, fixture.Work.Transactions);
    }
}
