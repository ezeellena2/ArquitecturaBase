using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormAssistantClarificationPolicyTests
{
    [Theory]
    [InlineData("es", "¿Qué formulario querés completar?", "Personas")]
    [InlineData("en", "Which form would you like to complete?", "People")]
    public async Task The_owner_returns_a_fixed_question_and_current_published_names_without_taking_send_locks(string culture, string question, string firstName)
    {
        var f = new FormAssistantClarificationFixture();
        if (culture == "en") await f.UseEnglishAsync(Ct);
        var result = await f.Policy.ValidateAsync(new(f.Inner.UserId, culture, f.Proposal()), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(question, result.Value.Question);
        Assert.Equal(2, result.Value.Choices.Count);
        Assert.Equal(firstName, result.Value.Choices[0].Label);
        Assert.Equal(f.Inner.Rows.Select(row => row.DefinitionId), result.Value.Choices.Select(choice => choice.DefinitionId));
        Assert.Equal(f.Inner.Rows.Select(row => row.VersionId), result.Value.Choices.Select(choice => choice.VersionId));
        Assert.Empty(f.Inner.Catalog.Locks);
    }
    [Theory]
    [InlineData("id")]
    [InlineData("turn")]
    [InlineData("capability")]
    [InlineData("ordinal")]
    [InlineData("subject")]
    [InlineData("culture")]
    [InlineData("incomplete")]
    [InlineData("duplicate")]
    [InlineData("one")]
    [InlineData("four")]
    public async Task An_authentic_proposal_cannot_borrow_an_identity_snapshot_or_choice(string attack)
    {
        var f = new FormAssistantClarificationFixture(attack == "four" ? 4 : 2);
        var proposal = f.Proposal(); var payload = Assert.IsType<FormAssistantClarificationPayload>(proposal.Payload);
        var read = proposal.ObservedReads[0]; var snapshot = Assert.IsType<FormAssistantCandidatesPayload>(read.Payload);
        if (attack == "id") proposal = proposal with { Id = Guid.Empty };
        if (attack == "turn") proposal = proposal with { TurnId = Guid.CreateVersion7() };
        if (attack == "capability") proposal = proposal with { Capability = proposal.Capability with { Version = 2 } };
        if (attack == "ordinal") proposal = proposal with { Payload = payload with { ReadOrdinal = 99 } };
        if (attack == "subject") proposal = proposal with { ObservedReads = [read with { Payload = snapshot with { RecipientUserId = Guid.CreateVersion7() } }] };
        if (attack == "culture") proposal = proposal with { ObservedReads = [read with { Payload = snapshot with { Culture = "en" } }] };
        if (attack == "incomplete") proposal = proposal with { ObservedReads = [read with { Payload = snapshot with { IsComplete = false } }] };
        if (attack == "duplicate") proposal = proposal with { Payload = payload with { Forms = [payload.Forms[0], payload.Forms[0]] } };
        if (attack == "one") proposal = proposal with { Payload = payload with { Forms = [payload.Forms[0]] } };
        Assert.True((await f.Policy.ValidateAsync(new(f.Inner.UserId, "es", proposal), Ct)).IsFailure);
    }
    [Fact]
    public async Task A_changed_definition_conflicts_without_replacing_the_observed_revision()
    {
        var f = new FormAssistantClarificationFixture(); var proposal = f.Proposal();
        f.Inner.Rows[1] = f.Inner.Rows[1] with { DefinitionRevision = f.Inner.Rows[1].DefinitionRevision + 1 };
        Assert.Equal(FormDraftErrors.RevisionConflict, (await f.Policy.ValidateAsync(new(f.Inner.UserId, "es", proposal), Ct)).Error);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("catalog")]
    public async Task A_no_longer_sendable_choice_invalidates_the_whole_question(string change)
    {
        var f = new FormAssistantClarificationFixture(); var proposal = f.Proposal();
        if (change == "missing") f.Inner.Rows.RemoveAt(1);
        if (change == "disabled") f.Inner.Rows[1] = f.Inner.Rows[1] with { OfferWhen = "" };
        if (change == "catalog") f.Inner.Catalog.Items.Clear();
        Assert.True((await f.Policy.ValidateAsync(new(f.Inner.UserId, "es", proposal), Ct)).IsFailure);
        Assert.Empty(f.Inner.Catalog.Locks);
    }
    [Fact]
    public async Task Inactive_recipients_do_not_receive_form_names()
    {
        var f = new FormAssistantClarificationFixture(); var proposal = f.Proposal();
        await f.Inner.Users.SetActiveAsync(f.Inner.UserId, false, Ct);
        Assert.Equal(FormRecordErrors.AuthorizationChanged, (await f.Policy.ValidateAsync(new(f.Inner.UserId, "es", proposal), Ct)).Error);
    }
    [Fact]
    public async Task Permission_revoked_during_the_current_read_is_checked_before_returning_the_question()
    {
        var f = new FormAssistantClarificationFixture(); var proposal = f.Proposal();
        f.Inner.OnRead = () => f.Inner.Permissions.Permissions.Clear();
        Assert.Equal(FormRecordErrors.AuthorizationChanged, (await f.Policy.ValidateAsync(new(f.Inner.UserId, "es", proposal), Ct)).Error);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_null_collections_and_members_fail_without_throwing(bool nullMember)
    {
        var f = new FormAssistantClarificationFixture(); var proposal = f.Proposal();
        proposal = proposal with { Payload = f.Payload() with { Forms = nullMember ? [null!, null!] : null! } };
        Assert.True((await f.Policy.ValidateAsync(new(f.Inner.UserId, "es", proposal), Ct)).IsFailure);
    }

    [Fact]
    public async Task Cancellation_stops_before_any_definition_read()
    {
        var f = new FormAssistantClarificationFixture();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Policy.ValidateAsync(new(f.Inner.UserId, "es", f.Proposal()), cancelled.Token));
        Assert.Equal(0, f.Inner.Reads);
    }
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
