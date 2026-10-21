using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantConversationReservationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_operations_from_one_actor_can_be_pending_in_two_channel_conversations(bool withSubject)
    {
        var fixture = new AssistantOperationsFixture();
        var first = Request(fixture, Guid.NewGuid(), withSubject);
        var second = Request(fixture, Guid.NewGuid(), withSubject);
        Assert.True((await fixture.Service.SubmitAsync(first, Ct)).IsSuccess);
        var accepted = await fixture.Service.SubmitAsync(second, Ct);
        Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Error.Code : null);
        Assert.Equal(2, fixture.Store.Turns.Count);
        Assert.Equal(2, fixture.Store.Receipts.Count);
        Assert.All(fixture.Store.Turns, turn => Assert.Equal(fixture.ActorId, turn.OwnerUserId));
        Assert.Equal(2, fixture.Store.Turns.Select(turn => turn.ConversationId).Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_same_channel_conversation_keeps_one_active_interpretation_even_without_a_subject(bool withSubject)
    {
        var fixture = new AssistantOperationsFixture();
        var first = Request(fixture, Guid.NewGuid(), withSubject);
        Assert.True((await fixture.Service.SubmitAsync(first, Ct)).IsSuccess);
        var rejected = await fixture.Service.SubmitAsync(first with { RequestId = Guid.NewGuid() }, Ct);
        Assert.Equal(AiAssistantErrors.ActiveTurn, rejected.Error);
        Assert.Single(fixture.Store.Receipts);
    }

    [Fact]
    public async Task A_scoped_receipt_replays_before_budget_checks_and_another_conversation_cannot_reuse_its_uuid()
    {
        var fixture = new AssistantOperationsFixture();
        var request = Request(fixture, Guid.NewGuid(), false);
        var first = await fixture.Service.SubmitAsync(request, Ct);
        Assert.True(first.IsSuccess);
        var budgetReads = fixture.Store.BudgetReads;
        var repeated = await fixture.Service.SubmitAsync(request, Ct);
        Assert.True(repeated.IsSuccess);
        Assert.Equal(first.Value, repeated.Value);
        Assert.Equal(budgetReads, fixture.Store.BudgetReads);
        var reused = await fixture.Service.SubmitAsync(request with { Context = request.Context with { ChannelConversationId = Guid.NewGuid() } }, Ct);
        Assert.Equal(AiAssistantErrors.RequestReused, reused.Error);
        Assert.Single(fixture.Store.Receipts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Another_channel_conversation_cannot_read_or_cancel_the_captured_operation(bool cancel)
    {
        var fixture = new AssistantOperationsFixture();
        var request = Request(fixture, Guid.NewGuid(), false);
        Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsSuccess);
        var other = request.Context with { ChannelConversationId = Guid.NewGuid() };
        var error = cancel
            ? (await fixture.Service.CancelAsync(other, request.RequestId, Ct)).Error
            : (await fixture.Service.GetAsync(other, request.RequestId, Ct)).Error;
        Assert.Equal(AiAssistantErrors.NotFound, error);
        Assert.Null(Assert.Single(fixture.Store.Turns).CancelRequestedAtUtc);
    }

    [Fact]
    public async Task Different_conversations_share_the_actor_channel_interpretation_quota()
    {
        var fixture = new AssistantOperationsFixture();
        for (var index = 0; index < 10; index++)
            Assert.True((await fixture.Service.SubmitAsync(Request(fixture, Guid.NewGuid(), false), Ct)).IsSuccess);
        var rejected = await fixture.Service.SubmitAsync(Request(fixture, Guid.NewGuid(), false), Ct);
        Assert.Equal(AiAssistantErrors.QuotaExceeded(1).Code, rejected.Error.Code);
        Assert.Equal(10, fixture.Store.Receipts.Count);
        Assert.Equal(10, fixture.Store.Turns.Count);
    }

    [Fact]
    public async Task A_scoped_receipt_still_replays_after_the_actor_channel_quota_is_exhausted()
    {
        var fixture = new AssistantOperationsFixture();
        var original = Request(fixture, Guid.NewGuid(), false);
        Assert.True((await fixture.Service.SubmitAsync(original, Ct)).IsSuccess);
        Assert.True((await fixture.Service.CancelAsync(original.Context, original.RequestId, Ct)).IsSuccess);
        for (var index = 1; index < 10; index++)
        {
            var request = Request(fixture, Guid.NewGuid(), false);
            Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsSuccess);
            Assert.True((await fixture.Service.CancelAsync(request.Context, request.RequestId, Ct)).IsSuccess);
        }
        var budgetReads = fixture.Store.BudgetReads;
        Assert.True((await fixture.Service.SubmitAsync(original, Ct)).IsSuccess);
        Assert.Equal(budgetReads, fixture.Store.BudgetReads);
        Assert.Equal(10, fixture.Store.Receipts.Count);
    }

    [Fact]
    public async Task Consumers_without_a_channel_conversation_keep_the_existing_exclusive_fallback()
    {
        var fixture = new AssistantOperationsFixture();
        Assert.True((await fixture.Service.SubmitAsync(fixture.Request(), Ct)).IsSuccess);
        Assert.Equal(AiAssistantErrors.ActiveTurn, (await fixture.Service.SubmitAsync(fixture.Request(), Ct)).Error);
        Assert.Single(fixture.Store.Receipts);
    }

    private static AssistantOperationRequest Request(AssistantOperationsFixture fixture, Guid conversationId, bool withSubject) =>
        fixture.Request() with { Context = fixture.Context with {
            InteractionMode = AssistantInteractionMode.Manual, ChannelConversationId = conversationId,
            SubjectUserId = withSubject ? fixture.SubjectId : null } };
}
