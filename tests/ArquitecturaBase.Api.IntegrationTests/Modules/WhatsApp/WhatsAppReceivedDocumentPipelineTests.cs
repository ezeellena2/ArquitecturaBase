using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// Task 8 of the documents: the inbound processor takes a received document in its own scopes, after the spreadsheet and before the
// interpretation, and its reading like an audio's normalization: Get first, Submit only what the engine does not know, Get and Complete.
// They do not use the database.
public sealed class WhatsAppReceivedDocumentPipelineTests
{
    [Fact]
    public async Task A_pending_document_is_scanned_on_its_own_and_its_reading_is_submitted_polled_and_completed_in_their_own_scopes()
    {
        await using var host = new WhatsAppAssistantInboundFixture { HasWork = false, AudioOnlyContact = true, HasDocumentWork = true };

        await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["access", "document-prepare", "get-document", "submit-document", "get-document", "document-complete", "prepare"], host.Events);
        Assert.Equal("pending", host.DocumentCompleted!.Value.State);
        Assert.NotEqual(host.Scopes[1], host.Scopes[2]);
        Assert.NotEqual(host.Scopes[2], host.Scopes[5]);
    }

    [Fact]
    public async Task A_reading_the_engine_already_has_is_only_polled()
    {
        await using var host = new WhatsAppAssistantInboundFixture { HasWork = false, AudioOnlyContact = true, HasDocumentWork = true, ExistingDocumentState = "running" };

        await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["access", "document-prepare", "get-document", "document-complete", "prepare"], host.Events);
        Assert.Equal("running", host.DocumentCompleted!.Value.State);
    }

    [Fact]
    public async Task A_document_that_is_not_read_goes_on_to_the_interpretation_without_the_engine()
    {
        await using var host = new WhatsAppAssistantInboundFixture { HasWork = true, HasDocumentWork = true, DocumentReads = false, HasSpreadsheetWork = true };

        await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["access", "spreadsheet", "document-prepare", "prepare", "get", "submit", "get", "complete"], host.Events);
        Assert.Null(host.DocumentCompleted);
    }

    // A document that waits to be downloaded or read makes the processor poll every two seconds, as a pending spreadsheet does.
    [Fact]
    public async Task A_document_that_waits_is_polled_every_two_seconds()
    {
        await using var host = new WhatsAppAssistantInboundFixture(processInBackground: true) { HasWork = false, AudioOnlyContact = true, HasPendingDocument = true };
        var ct = TestContext.Current.CancellationToken;
        await host.Processor.StartAsync(ct);
        Assert.Equal(TimeSpan.FromSeconds(2), await host.WaitForPollAsync(ct));

        host.Clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(2), await host.WaitForPollAsync(ct));
        Assert.Equal(2, host.Rounds);
    }

    [Fact]
    public async Task A_contact_taken_by_another_instance_leaves_its_document_for_the_next_round()
    {
        await using var host = new WhatsAppAssistantInboundFixture { HasWork = false, AudioOnlyContact = true, HasDocumentWork = true, ContactTaken = true };

        await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["access"], host.Events);
        Assert.True(host.HasDocumentWork);
    }
}
