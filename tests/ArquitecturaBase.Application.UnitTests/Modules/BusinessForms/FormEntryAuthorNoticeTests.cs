using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormEntryAuthorNoticeTests
{
    [Fact]
    public async Task Notifying_the_author_of_an_app_entry_without_a_channel_address_does_not_apply()
    {
        var fixture = new FormActionFixture();
        var captured = fixture.Capture([new(Guid.NewGuid(), "Received", [], [FormActionFixture.Action()])], enteredInApp: true);

        await fixture.Recorder.RecordAsync(captured.Record, captured.Snapshot, "Received", CancellationToken.None);

        Assert.Equal(FormActionRunState.NotApplicable, Assert.Single(fixture.Runs.Runs).State);
    }

    [Fact]
    public async Task Notifying_the_author_of_an_app_entry_with_a_channel_address_waits_for_its_delivery()
    {
        var fixture = new FormActionFixture();
        var captured = fixture.Capture([new(Guid.NewGuid(), "Received", [], [FormActionFixture.Action()])], enteredInApp: true);
        fixture.Channel.Reachable.Add(captured.Record.ActorUserId);

        await fixture.Recorder.RecordAsync(captured.Record, captured.Snapshot, "Received", CancellationToken.None);

        Assert.Equal(FormActionRunState.Waiting, Assert.Single(fixture.Runs.Runs).State);
    }

    [Fact]
    public async Task A_channel_capture_keeps_notifying_its_author_and_the_role_notice_still_applies_to_app_entries()
    {
        var fixture = new FormActionFixture();
        var role = FormActionFixture.Action("NotifyRole") with { RoleId = fixture.Rules.RoleId };
        var channel = fixture.Capture([new(Guid.NewGuid(), "Received", [], [FormActionFixture.Action()])]);
        await fixture.Recorder.RecordAsync(channel.Record, channel.Snapshot, "Received", CancellationToken.None);
        var entry = fixture.Capture([new(Guid.NewGuid(), "Received", [], [role])], enteredInApp: true);
        await fixture.Recorder.RecordAsync(entry.Record, entry.Snapshot, "Received", CancellationToken.None);

        Assert.All(fixture.Runs.Runs, run => Assert.Equal(FormActionRunState.Waiting, run.State));
    }
}
