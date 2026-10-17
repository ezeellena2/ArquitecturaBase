using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
public sealed class WhatsAppAssistantInboundProcessorTests
{
    [Fact]
    public async Task A_manual_request_is_processed_even_when_the_access_inbound_is_already_classified()
    {
        await using var host=new WhatsAppAssistantInboundFixture();await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[]{"access","prepare","submit","get","complete"},host.Events);
        Assert.NotNull(host.Completed);Assert.True(host.Completed.IsSuccess);Assert.Equal("pending",host.Completed.Value.State);
        Assert.NotEqual(host.Scopes[1],host.Scopes[2]);Assert.NotEqual(host.Scopes[2],host.Scopes[4]);
    }
    [Fact]
    public async Task An_empty_preparation_never_accepts_a_model_operation()
    {
        await using var host=new WhatsAppAssistantInboundFixture{HasWork=false};await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[]{"access","prepare"},host.Events);Assert.Null(host.Completed);
    }
    [Fact]
    public async Task A_rejected_submission_is_completed_without_trying_to_read_a_nonexistent_operation()
    {
        await using var host=new WhatsAppAssistantInboundFixture{RejectSubmit=true};await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[]{"access","prepare","submit","complete"},host.Events);Assert.NotNull(host.Completed);Assert.True(host.Completed.IsFailure);
    }
    [Fact]
    public async Task Choosing_an_existing_question_only_reads_its_result_without_submitting_another_model_operation()
    {
        await using var host=new WhatsAppAssistantInboundFixture{ReuseExistingOperation=true};
        await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[]{"access","prepare","get","complete"},host.Events);
        Assert.NotNull(host.Completed);
    }
    [Fact]
    public async Task A_configured_followup_uses_the_existing_processor_without_resolving_an_AI_operation()
    {
        await using var host=new WhatsAppAssistantInboundFixture{ConfiguredWork=true};
        await host.Processor.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new[]{"access","prepare","configured-complete"},host.Events);
        Assert.Null(host.Completed);Assert.NotEqual(host.Scopes[1],host.Scopes[2]);
    }
}
