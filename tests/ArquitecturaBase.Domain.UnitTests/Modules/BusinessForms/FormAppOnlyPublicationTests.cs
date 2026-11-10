using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

// SA1-A, SA3-A: a form completed only in the app publishes without a channel; nothing outside the system waits for it.
public sealed class FormAppOnlyPublicationTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_publication_without_channels_is_published_as_it_is_accepted()
    {
        var operation = NewOperation();

        Assert.True(operation.PublishWithoutChannels(Now.AddSeconds(1)).IsSuccess);

        Assert.Equal(FormPublicationState.Published, operation.State);
        Assert.Equal("Complete", operation.Stage);
        Assert.False(operation.IsOpen);
        Assert.Equal(Now.AddSeconds(1), operation.UpdatedAtUtc);
        Assert.True(operation.Reserve(Now.AddMinutes(1), TimeSpan.FromMinutes(1)).IsFailure);
        Assert.True(operation.PublishWithoutChannels(Now.AddMinutes(1)).IsFailure);
    }

    [Fact]
    public void Only_an_accepted_operation_publishes_without_channels()
    {
        var operation = NewOperation();
        Assert.True(operation.Reserve(Now, TimeSpan.FromMinutes(1)).IsSuccess);

        Assert.Equal(FormPublicationErrors.StateConflict, operation.PublishWithoutChannels(Now).Error);
        Assert.Equal(FormPublicationState.Preparing, operation.State);
    }

    [Fact]
    public void A_version_published_without_channels_says_so()
    {
        var version = BusinessFormVersion.Create(Guid.CreateVersion7(), 1, "hash");
        Assert.False(version.AppOnly);

        version.MarkAppOnly();
        version.MarkAppOnly();

        Assert.True(version.AppOnly);
    }

    [Fact]
    public void The_draft_keeps_where_it_is_completed_under_its_revision()
    {
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, "Remito", "{}", "REM", "es", "{}", "{}", 0, true).IsSuccess);
        Assert.False(definition.AppOnly);

        Assert.True(definition.SetDraft(1, "Remito", "{}", "REM", "es", "{}", "{}", 0, false, appOnly: true).IsSuccess);
        Assert.True(definition.AppOnly);
        Assert.Equal(2, definition.Revision);

        Assert.True(definition.SetDraft(2, "Remito", "{}", "REM", "es", "{}", "{}", 0, false).IsSuccess);
        Assert.False(definition.AppOnly);
        Assert.Equal(3, definition.Revision);
    }

    private static BusinessFormPublicationOperation NewOperation() => BusinessFormPublicationOperation.Accept(Guid.CreateVersion7(),
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, null, [1], Now);
}
