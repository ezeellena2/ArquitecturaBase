using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// Stage 2 of the assistant authoring: the inbound processor gets a received spreadsheet downloaded in its own scope, after the
// assistant and the audio, so nothing changes for a text, an audio or a tap. They do not use the database.
public sealed class WhatsAppSpreadsheetPipelineTests
{
    [Fact]
    public async Task A_pending_spreadsheet_is_scanned_on_its_own_and_prepared_after_the_rest_of_the_round()
    {
        await using var host = new WhatsAppAssistantInboundFixture { HasWork = false, AudioOnlyContact = true, HasSpreadsheetWork = true };

        await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["access", "prepare", "spreadsheet"], host.Events);
        Assert.NotEqual(host.Scopes[1], host.Scopes[2]);
    }

    [Fact]
    public async Task The_spreadsheet_comes_after_the_interpretation_without_changing_it()
    {
        await using var host = new WhatsAppAssistantInboundFixture { HasWork = true, HasSpreadsheetWork = true };

        await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["access", "prepare", "get", "submit", "get", "complete", "spreadsheet"], host.Events);
        Assert.NotNull(host.Completed);
    }

    [Fact]
    public async Task A_contact_taken_by_another_instance_leaves_its_spreadsheet_for_the_next_round()
    {
        await using var host = new WhatsAppAssistantInboundFixture { HasWork = false, AudioOnlyContact = true, HasSpreadsheetWork = true, ContactTaken = true };

        await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["access"], host.Events);
        Assert.True(host.HasSpreadsheetWork);
    }
}
