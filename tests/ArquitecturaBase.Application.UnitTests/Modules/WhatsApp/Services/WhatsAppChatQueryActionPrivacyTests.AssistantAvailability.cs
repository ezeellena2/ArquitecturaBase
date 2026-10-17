namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed partial class WhatsAppChatQueryActionPrivacyTests
{
    [Theory]
    [InlineData("available")]
    [InlineData("notInstalled")]
    [InlineData("notConfigured")]
    [InlineData("paused")]
    [InlineData("modelNotQualified")]
    public async Task A_thread_projects_current_local_assistant_availability_without_submitting_or_loading_private_operations(string state)
    {
        var host = new Host();
        host.Operations.State = state;
        var result = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(state, result.Value.AssistantAvailability.State);
        Assert.Equal(1, host.Operations.AvailabilityReads);
    }

    [Fact]
    public async Task The_chat_list_does_not_read_global_assistant_availability_for_each_row()
    {
        var host = new Host();
        Assert.True((await host.Service.ListAsync(new(), Guid.NewGuid(), Ct)).IsSuccess);
        Assert.Equal(0, host.Operations.AvailabilityReads);
    }

    [Fact]
    public async Task A_missing_thread_does_not_read_assistant_availability()
    {
        var host = new Host();
        host.Reader.Missing = true;
        Assert.True((await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct)).IsFailure);
        Assert.Equal(0, host.Operations.AvailabilityReads);
    }
}
