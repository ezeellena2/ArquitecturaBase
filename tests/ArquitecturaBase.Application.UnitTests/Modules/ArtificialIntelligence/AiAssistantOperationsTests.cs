using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantOperationsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("notConfigured")]
    [InlineData("paused")]
    [InlineData("modelNotQualified")]
    [InlineData("available")]
    public async Task Availability_is_local_and_does_not_require_the_Home_permission(string state)
    {
        var fixture = new AssistantOperationsFixture(qualified: state != "modelNotQualified");
        if (state == "notConfigured") fixture.Store.Connection = fixture.Store.Connection with { ProtectedApiKey = null };
        if (state == "paused") fixture.Store.Connection = fixture.Store.Connection with { Enabled = false };
        var response = await fixture.Service.GetAvailabilityAsync(Ct);
        Assert.Equal(state, response.State);
        Assert.DoesNotContain(ArtificialIntelligencePermissions.AssistantUse, fixture.Permissions.Permissions[fixture.ActorId]);
        Assert.Equal(1, fixture.Store.ConnectionReads);
        Assert.Equal(0, fixture.Work.Transactions);
        Assert.Equal(0, fixture.Source.Executions);
    }

    [Fact]
    public async Task A_manual_channel_accepts_separate_actor_and_subject_permissions_without_Home_and_seals_the_owner_expiry()
    {
        var fixture = new AssistantOperationsFixture();
        var request = fixture.Request();
        var result = await fixture.Service.SubmitAsync(request, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(request.RequestId, result.Value.RequestId);
        Assert.Equal("pending", result.Value.State);
        var turn = Assert.Single(fixture.Store.Turns);
        Assert.Equal(request.Context.ActorUserId, turn.OwnerUserId);
        Assert.Equal(request.ContentExpiresAtUtc, turn.ContentExpiresAtUtc);
        Assert.Equal(request.Context.ChannelId, Assert.Single(fixture.Store.Conversations).ChannelId);
        Assert.Equal(1, fixture.Work.Transactions);
        Assert.Equal(1, fixture.Work.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, fixture.Work.LastPolicy);
        Assert.Equal(0, fixture.Source.Executions);
        Assert.DoesNotContain(ArtificialIntelligencePermissions.AssistantUse, fixture.Permissions.Permissions[fixture.ActorId]);
        using var profile = JsonDocument.Parse(turn.ExecutionProfileJson!);
        Assert.Equal(request.OccurredAtUtc, profile.RootElement.GetProperty("occurredAtUtc").GetDateTime());
        var context = profile.RootElement.GetProperty("channelContext");
        Assert.Equal(fixture.ActorId, context.GetProperty("actorUserId").GetGuid());
        Assert.Equal(fixture.SubjectId, context.GetProperty("subjectUserId").GetGuid());
        Assert.Contains(context.GetProperty("requiredPermissions").GetProperty("subject").EnumerateArray(), permission => permission.GetString() == "formrecords.create");
    }

    [Theory]
    [InlineData("actorPermission")]
    [InlineData("subjectPermission")]
    [InlineData("actorInactive")]
    [InlineData("subjectInactive")]
    public async Task Acceptance_revalidates_both_accounts_and_never_borrows_permissions(string change)
    {
        var fixture = new AssistantOperationsFixture();
        if (change == "actorPermission") fixture.Permissions.Permissions[fixture.ActorId] = ["formrecords.create"];
        if (change == "subjectPermission")
        {
            fixture.Permissions.Permissions[fixture.ActorId] = ["fictional.channel.read", "fictional.channel.send", "formrecords.create"];
            fixture.Permissions.Permissions[fixture.SubjectId] = [];
        }
        if (change == "actorInactive") await fixture.Users.SetActiveAsync(fixture.ActorId, false, Ct);
        if (change == "subjectInactive") await fixture.Users.SetActiveAsync(fixture.SubjectId, false, Ct);
        var request = fixture.Request();
        if (change == "subjectPermission") request = request with { Context = request.Context with { RequiredPermissions = new(request.Context.RequiredPermissions.Actor, ["formrecords.create"]) } };
        var result = await fixture.Service.SubmitAsync(request, Ct);
        Assert.True(result.IsFailure);
        Assert.Empty(fixture.Store.Turns);
        Assert.Empty(fixture.Store.Receipts);
        Assert.Equal(0, fixture.Work.Commits);
        Assert.Equal(0, fixture.Source.Executions);
    }

    [Fact]
    public async Task Identical_replay_recovers_the_receipt_before_connection_changes_or_another_quota_check()
    {
        var fixture = new AssistantOperationsFixture();
        var request = fixture.Request();
        var first = await fixture.Service.SubmitAsync(request, Ct);
        Assert.True(first.IsSuccess);
        fixture.Store.Connection = fixture.Store.Connection with { Enabled = false, Revision = 2 };
        fixture.Profiles[0] = fixture.Profiles[0] with { Version = 99 };
        var repeated = await fixture.Service.SubmitAsync(request, Ct);
        Assert.True(repeated.IsSuccess);
        Assert.Equal(first.Value, repeated.Value);
        Assert.Single(fixture.Store.Turns);
        Assert.Single(fixture.Store.Receipts);
        Assert.Equal(1, fixture.Store.BudgetReads);
        Assert.Equal(2, fixture.Work.Transactions);
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("text")]
    [InlineData("expiry")]
    [InlineData("occurredAt")]
    public async Task Reusing_the_request_identifier_with_changed_owner_input_is_a_conflict(string change)
    {
        var fixture = new AssistantOperationsFixture();
        var request = fixture.Request();
        Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsSuccess);
        var other = fixture.Users.AddUser("another-subject-operations@example.test");
        fixture.Permissions.Permissions[other.Id] = ["formrecords.create"];
        var changed = change switch
        {
            "subject" => request with { Context = request.Context with { SubjectUserId = other.Id } },
            "text" => request with { Text = "A different fictitious visit" },
            "expiry" => request with { ContentExpiresAtUtc = request.ContentExpiresAtUtc.AddMinutes(1) },
            _ => request with { OccurredAtUtc = request.OccurredAtUtc.AddSeconds(1) },
        };
        var result = await fixture.Service.SubmitAsync(changed, Ct);
        Assert.Equal(AiAssistantErrors.RequestReused, result.Error);
        Assert.Single(fixture.Store.Turns);
        Assert.Single(fixture.Store.Receipts);
        Assert.Equal(1, fixture.Store.BudgetReads);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("subject")]
    [InlineData("channel")]
    [InlineData("culture")]
    [InlineData("timeZone")]
    [InlineData("capability")]
    [InlineData("terminal")]
    [InlineData("requirements")]
    public async Task A_different_requested_context_is_not_found_before_any_content_decryption(string change)
    {
        var fixture = new AssistantOperationsFixture();
        var completed = fixture.SeedCompleted();
        var context = change switch
        {
            "actor" => fixture.Context with { ActorUserId = Guid.NewGuid() },
            "subject" => fixture.Context with { SubjectUserId = Guid.NewGuid() },
            "channel" => fixture.Context with { ChannelId = "another-owner-channel" },
            "culture" => fixture.Context with { Culture = "es" },
            "timeZone" => fixture.Context with { TimeZoneId = "America/Argentina/Buenos_Aires" },
            "capability" => fixture.Context with { AllowedCapabilities = [new("fixture.proposal", 2)] },
            "terminal" => fixture.Context with { AllowedTerminals = [new("assistant_answer", 1)] },
            _ => fixture.Context with { RequiredPermissions = new(["fictional.unaccepted.permission"], []) },
        };
        var result = await fixture.Service.GetAsync(context, completed.Turn.RequestId, Ct);
        Assert.Equal(AiAssistantErrors.NotFound, result.Error);
        Assert.Equal(0, fixture.Protector.Reads);
        Assert.Equal(context.ActorUserId, fixture.Store.LastReadActor);
        Assert.Equal(completed.Turn.RequestId, fixture.Store.LastReadRequest);
        Assert.Equal(0, fixture.Work.Transactions);
    }

    [Theory]
    [InlineData("actorPermission")]
    [InlineData("subjectPermission")]
    [InlineData("actorInactive")]
    [InlineData("subjectInactive")]
    public async Task Recovery_revalidates_the_captured_identities_before_decryption(string change)
    {
        var fixture = new AssistantOperationsFixture();
        var completed = fixture.SeedCompleted();
        if (change == "actorPermission") fixture.Permissions.Permissions[fixture.ActorId] = [];
        if (change == "subjectPermission") fixture.Permissions.Permissions[fixture.SubjectId] = [];
        if (change == "actorInactive") await fixture.Users.SetActiveAsync(fixture.ActorId, false, Ct);
        if (change == "subjectInactive") await fixture.Users.SetActiveAsync(fixture.SubjectId, false, Ct);
        var result = await fixture.Service.GetAsync(fixture.Context, completed.Turn.RequestId, Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(0, fixture.Protector.Reads);
        Assert.Equal(0, fixture.Source.Executions);
        Assert.Equal(0, fixture.Work.Transactions);
    }

    [Fact]
    public async Task Confirmed_recovery_returns_the_typed_proposal_and_sealed_provenance_without_using_current_profiles()
    {
        var fixture = new AssistantOperationsFixture();
        var completed = fixture.SeedCompleted();
        fixture.Profiles[0] = fixture.Profiles[0] with { Id = "replacement-current-profile", Version = 99 };
        fixture.Store.Connection = fixture.Store.Connection with { Enabled = false, Revision = 2 };
        var result = await fixture.Service.GetAsync(fixture.Context, completed.Turn.RequestId, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("completed", result.Value.State);
        Assert.NotNull(result.Value.Proposal);
        Assert.Equal(completed.Proposal.Id, result.Value.Proposal.Id);
        Assert.Equal(new RoundtripAssistantPayload(fixture.Source.ResourceId, fixture.Source.PrivateText), result.Value.Proposal.Payload);
        Assert.Equal(new AssistantOperationProvenance("fixture-profile", 3, "capabilities-v1", 1, completed.OccurredAtUtc, fixture.Context.ChannelId), result.Value.Provenance);
        Assert.True(fixture.Source.ParseCalls > 0);
        Assert.Equal(0, fixture.Source.Executions);
        Assert.Equal(0, fixture.Work.Transactions);
        Assert.Equal(0, fixture.Store.ConnectionReads);
    }

    [Fact]
    public async Task Owner_content_expiry_hides_the_proposal_before_the_durable_receipt_expires()
    {
        var fixture = new AssistantOperationsFixture();
        var completed = fixture.SeedCompleted();
        fixture.Clock.Advance(TimeSpan.FromHours(2));
        var result = await fixture.Service.GetAsync(fixture.Context, completed.Turn.RequestId, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("completed", result.Value.State);
        Assert.Null(result.Value.Proposal);
        Assert.Equal(0, fixture.Protector.Reads);
        Assert.True(Assert.Single(fixture.Store.Receipts).ExpiresAtUtc > fixture.Clock.GetUtcNow().UtcDateTime);
    }
}
