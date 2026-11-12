using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// ReadDocument runs through the same Prepare, Advance, Complete and Get as any operation, with only the sealed instructions and the
// private document: no history, tools or terminals, and a reading that is valid whole or not at all.
public sealed class AiAssistantDocumentExecutionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("en", "[illegible]")]
    [InlineData("es", "[ilegible]")]
    public async Task Prepare_sends_the_vision_model_the_sealed_instructions_and_the_private_document_without_history_or_tools(string culture, string marker)
    {
        var f = new AssistantDocumentExecutionFixture(culture);

        var prepared = await f.ContextPolicy.PrepareAsync(f.Claim, Ct);

        Assert.True(prepared.IsSuccess, prepared.IsFailure ? prepared.Error.Code : null);
        var request = prepared.Value.ProviderRequest;
        Assert.Equal("vision-model", request.Model);
        Assert.Equal("fixture-document-key", request.ApiKey);
        Assert.Equal(f.Document, request.Document);
        Assert.Equal(10 * 1024 * 1024, request.DocumentMaximumBytes);
        Assert.Equal(AssistantTexts.Get("Prompt.DocumentReadV1", culture), request.SystemPrompt);
        Assert.Contains(marker, request.SystemPrompt, StringComparison.Ordinal);
        Assert.Equal(8192, request.MaxOutputTokens);
        Assert.False(request.LowReasoningEffort);
        Assert.Null(request.Audio);
        Assert.Empty(request.Messages);
        Assert.Empty(request.Tools);
        Assert.Empty(request.ToolResults);
        Assert.Equal(0, f.Inner.Source.Executions);
    }

    [Fact]
    public async Task A_profile_that_takes_a_reasoning_effort_asks_for_a_low_one()
    {
        var f = new AssistantDocumentExecutionFixture(reasoning: true);

        var prepared = await f.ContextPolicy.PrepareAsync(f.Claim, Ct);

        Assert.True(prepared.Value.ProviderRequest.LowReasoningEffort);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("pause")]
    [InlineData("key")]
    [InlineData("profile")]
    [InlineData("profileBytes")]
    [InlineData("profilePages")]
    [InlineData("profileType")]
    [InlineData("permission")]
    [InlineData("expired")]
    [InlineData("copyExpired")]
    [InlineData("hash")]
    [InlineData("tools")]
    public async Task Prepare_rejects_invalid_current_access_connection_profile_or_source_before_transport(string change)
    {
        var f = new AssistantDocumentExecutionFixture();
        var profile = f.Inner.DocumentProfiles[0];
        if (change == "revision") f.Inner.Store.Connection = f.Inner.Store.Connection with { Revision = 2 };
        if (change == "pause") f.Inner.Store.Connection = f.Inner.Store.Connection with { Enabled = false };
        if (change == "key") f.Inner.Store.Connection = f.Inner.Store.Connection with { ProtectedApiKey = null };
        if (change == "profile") f.Inner.DocumentProfiles[0] = profile with { Version = 5 };
        if (change == "profileBytes") f.Inner.DocumentProfiles[0] = profile with { MaximumDocumentBytes = 1024 };
        if (change == "profilePages") f.Inner.DocumentProfiles[0] = profile with { MaximumPages = 1 };
        if (change == "profileType") f.Inner.DocumentProfiles[0] = profile with { ContentTypes = ["image/jpeg"] };
        if (change == "permission") f.Inner.Permissions.Permissions[f.Inner.ActorId] = [];
        if (change == "expired") f.Inner.Clock.Advance(TimeSpan.FromHours(2));
        if (change == "copyExpired") f.ProtectDescriptor(f.Document with { ExpiresAtUtc = f.Turn.ContentExpiresAtUtc.AddMinutes(-1) });
        if (change == "hash") f.ProtectDescriptor(f.Document with { Sha256 = "bad" });
        if (change == "tools")
        {
            var tools = f.Profile with { ChannelContext = f.Profile.ChannelContext with { AllowedCapabilities = [new("fixture.proposal", 1)] } };
            Assert.True(AiAssistantChannelPolicy.Read(JsonSerializer.Serialize(tools, AiAssistantResponsePolicy.JsonOptions)).IsFailure);
            return;
        }

        Assert.True((await f.ContextPolicy.PrepareAsync(f.Claim, Ct)).IsFailure);
        Assert.Equal(0, f.Client.Calls);
    }

    [Fact]
    public async Task A_reading_gets_the_document_timeout_instead_of_a_conversation_step()
    {
        var f = new AssistantDocumentExecutionFixture();
        var client = new WaitingConversationClient();
        var execution = new AiAssistantExecutionService(f.ContextPolicy, f.Guard, f.Catalog, null!, client, f.Limits, f.Inner.Clock,
            NullLogger<AiAssistantExecutionService>.Instance);

        var pending = execution.CallProviderAsync(await f.ContextAsync(Ct), Ct);
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        f.Inner.Clock.Advance(TimeSpan.FromSeconds(f.Limits.ProviderTimeoutSeconds));

        Assert.False(client.Token.IsCancellationRequested);
        f.Inner.Clock.Advance(TimeSpan.FromSeconds(f.Limits.DocumentTimeoutSeconds - f.Limits.ProviderTimeoutSeconds));
        Assert.True(client.Token.IsCancellationRequested);
        Assert.Equal(AiAssistantErrors.ProviderUncertain, (await pending.WaitAsync(TimeSpan.FromSeconds(5), Ct)).Error);
    }

    [Fact]
    public async Task A_reading_is_a_complete_technical_terminal_without_a_model_tool_call()
    {
        var f = new AssistantDocumentExecutionFixture();
        var response = new LlmConversationResponse(LlmDocumentReadingResponse.Read, [], null, new(1200, 300))
            { DocumentReading = AssistantDocumentExecutionFixture.Reading() };

        var result = await f.Execution.AdvanceAsync(await f.ContextAsync(Ct), response, Ct);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.True(result.Value.IsComplete);
        Assert.Equal(response.DocumentReading, result.Value.DocumentReading);
        Assert.Equal(new LlmConversationUsage(1200, 300), result.Value.Usage);
        Assert.Empty(result.Value.NewTools);
        Assert.Null(result.Value.Answer);
        Assert.Null(result.Value.Proposal);
        Assert.Null(result.Value.NormalizedAudio);
        Assert.Equal(0, f.Inner.Source.Executions);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("es-AR", "es")]
    [InlineData("EN", "en")]
    public async Task A_reading_keeps_the_lowercase_primary_subtag_of_its_language_or_none(string language, string? expected)
    {
        var f = new AssistantDocumentExecutionFixture();
        var response = new LlmConversationResponse(LlmDocumentReadingResponse.Read, [], null, null)
            { DocumentReading = AssistantDocumentExecutionFixture.Reading() with { Language = language } };

        var result = await f.Execution.AdvanceAsync(await f.ContextAsync(Ct), response, Ct);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(expected, result.Value.DocumentReading!.Language);
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("incomplete")]
    [InlineData("tool")]
    [InlineData("audio")]
    [InlineData("tooLong")]
    [InlineData("nul")]
    [InlineData("language")]
    [InlineData("emptyReadable")]
    public async Task A_reading_never_accepts_actions_refusals_or_partial_output(string change)
    {
        var f = new AssistantDocumentExecutionFixture(limits: new AiAssistantLimits { MaximumReadingCharacters = 8_000 });
        var reading = AssistantDocumentExecutionFixture.Reading();
        if (change == "tooLong") reading = reading with { Text = new string('a', 8_001) };
        if (change == "nul") reading = reading with { Text = "invalid\0text" };
        if (change == "language") reading = reading with { Language = "Spanish" };
        if (change == "emptyReadable") reading = reading with { Text = "" };
        var response = change is "refusal" or "incomplete"
            ? new LlmConversationResponse(change, [], null, new(10, 2))
            : new LlmConversationResponse(LlmDocumentReadingResponse.Read, change == "tool" ? [new("call", "fixture.proposal", "{}")] : [], null, null)
                { DocumentReading = reading, NormalizedAudio = change == "audio" ? new("Visit", ["en"]) : null };

        var result = await f.Execution.AdvanceAsync(await f.ContextAsync(Ct), response, Ct);

        Assert.Equal(AiAssistantErrors.InvalidOutput, result.Error);
        Assert.Equal(0, f.Inner.Source.Executions);
    }

    [Fact]
    public async Task An_interpretation_never_accepts_a_reading()
    {
        var f = new AssistantDocumentExecutionFixture();
        var context = await f.ContextAsync(Ct);
        var interpretation = context with { ExecutionProfile = f.Profile with { Purpose = AiAssistantPurpose.Interpret, InstructionsId = "capabilities-v1" } };
        var response = new LlmConversationResponse(LlmDocumentReadingResponse.Read, [], null, null) { DocumentReading = AssistantDocumentExecutionFixture.Reading() };

        Assert.Equal(AiAssistantErrors.InvalidOutput, (await f.Execution.AdvanceAsync(interpretation, response, Ct)).Error);
    }

    [Fact]
    public async Task A_known_reading_is_protected_and_recovered_by_the_same_operation_and_scope()
    {
        var f = new AssistantDocumentExecutionFixture();
        Assert.True((await f.Dispatch.BeginProviderStepAsync(f.Claim, Ct)).Value);
        var reading = new StructuredDocumentReading(StructuredDocumentReadability.Partial, StructuredDocumentProblem.Cut, "es",
            "| Fecha | 11/11/2026 |\n| Monto | [ilegible] |", true);
        var complete = new AiAssistantAdvanceResponse(await f.ContextAsync(Ct), null, [], new(1200, 300)) { DocumentReading = reading };

        Assert.True((await f.Dispatch.CompleteAsync(f.Claim, complete, Ct)).IsSuccess);

        Assert.Equal(AiAssistantTurnState.Completed, f.Turn.State);
        Assert.Equal((1200L, 300L), (f.Turn.InputTokens, f.Turn.OutputTokens));
        Assert.False(f.Turn.UsageUnknown);
        Assert.Null(f.Turn.AudioSeconds);
        Assert.DoesNotContain("11/11/2026", f.Turn.ProtectedAnswer!, StringComparison.Ordinal);
        var result = await f.Inner.Service.GetAsync(f.Profile.ChannelContext, f.Turn.RequestId, Ct);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(reading, result.Value.DocumentReading);
        Assert.Null(result.Value.NormalizedAudio);
        Assert.Null(result.Value.Proposal);
        Assert.Equal(("document-read-v1", 1), (result.Value.Provenance!.InstructionsId, result.Value.Provenance.InstructionsVersion));
        Assert.Equal(("fixture-document", 4), (result.Value.Provenance.ModelProfileId, result.Value.Provenance.ModelProfileVersion));
        Assert.Equal(2, f.Inner.Work.Commits);
        Assert.Empty(f.Inner.Store.Tools);
        var mapped = new AiAssistantResponsePolicy(f.Inner.Protector, f.Inner.Clock, f.Catalog).Map((await f.Inner.Store.FindTurnAsync(f.Turn.Id, Ct))!, true, 0);
        Assert.True(mapped.IsSuccess);
        Assert.Null(mapped.Value.Question);
        Assert.DoesNotContain("11/11/2026", JsonSerializer.Serialize(mapped.Value), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("audio")]
    [InlineData("answer")]
    [InlineData("tooLong")]
    public async Task Complete_closes_a_reading_only_with_a_whole_reading_and_nothing_else(string change)
    {
        var f = new AssistantDocumentExecutionFixture(limits: new AiAssistantLimits { MaximumReadingCharacters = 8_000 });
        Assert.True((await f.Dispatch.BeginProviderStepAsync(f.Claim, Ct)).Value);
        var context = await f.ContextAsync(Ct);
        var completion = new AiAssistantAdvanceResponse(context, change == "answer" ? new("answer", "text", []) : null, [], null)
        {
            DocumentReading = AssistantDocumentExecutionFixture.Reading(change == "tooLong" ? new string('a', 8_001) : "Monto"),
            NormalizedAudio = change == "audio" ? new("Visit", ["en"]) : null,
        };

        Assert.True((await f.Dispatch.CompleteAsync(f.Claim, completion, Ct)).IsSuccess);

        Assert.Equal(AiAssistantTurnState.Failed, f.Turn.State);
        Assert.Equal(AiAssistantErrors.InvalidOutput.Code, f.Turn.FailureCode);
        Assert.Null(f.Turn.ProtectedAnswer);
    }

    [Fact]
    public async Task Another_purpose_never_closes_on_a_reading()
    {
        var f = new AssistantAudioExecutionFixture();
        Assert.True((await f.Dispatch.BeginProviderStepAsync(f.Claim, Ct)).Value);
        var completion = new AiAssistantAdvanceResponse(await f.ContextAsync(Ct), null, [], null) { DocumentReading = AssistantDocumentExecutionFixture.Reading() };

        Assert.True((await f.Dispatch.CompleteAsync(f.Claim, completion, Ct)).IsSuccess);

        Assert.Equal(AiAssistantTurnState.Failed, f.Turn.State);
        Assert.Equal(AiAssistantErrors.InvalidOutput.Code, f.Turn.FailureCode);
    }

    [Fact]
    public async Task A_late_reading_records_its_usage_but_never_repopulates_expired_content()
    {
        var f = new AssistantDocumentExecutionFixture();
        Assert.True((await f.Dispatch.BeginProviderStepAsync(f.Claim, Ct)).Value);
        var context = await f.ContextAsync(Ct);
        f.Inner.Clock.Advance(TimeSpan.FromHours(2));
        f.Turn.PurgeContent();

        var result = await f.Dispatch.CompleteAsync(f.Claim, new(context, null, [], new(1200, 300))
            { DocumentReading = AssistantDocumentExecutionFixture.Reading("Late private reading") }, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(AiAssistantTurnState.Failed, f.Turn.State);
        Assert.Equal(AiAssistantErrors.Expired.Code, f.Turn.FailureCode);
        Assert.Equal((1200L, 300L), (f.Turn.InputTokens, f.Turn.OutputTokens));
        Assert.Null(f.Turn.ProtectedQuestion);
        Assert.Null(f.Turn.ProtectedAnswer);
        var response = await f.Inner.Service.GetAsync(f.Profile.ChannelContext, f.Turn.RequestId, Ct);
        Assert.True(response.IsSuccess);
        Assert.Null(response.Value.DocumentReading);
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("type", false)]
    [InlineData("bytes", false)]
    [InlineData("pages", false)]
    [InlineData("storage", false)]
    [InlineData("pause", false)]
    [InlineData("key", false)]
    public void The_installation_reads_documents_only_with_a_profile_for_the_three_types_at_the_limits_and_private_storage(string change, bool reads)
    {
        var profile = new AiAssistantDocumentModelProfile(AiProvider.OpenAI, "vision-model", "fixture-document", 1,
            change == "type" ? ["application/pdf", "image/jpeg"] : ["application/pdf", "image/jpeg", "image/png"],
            change == "bytes" ? StructuredAttachmentLimits.MaximumBytes - 1 : StructuredAttachmentLimits.MaximumBytes,
            change == "pages" ? StructuredAttachmentLimits.MaximumPages - 1 : StructuredAttachmentLimits.MaximumPages);
        var technical = new AiAssistantTechnicalProfilePolicy([], [profile], new TestFeatures.Files.MemoryPrivateFileStore(change != "storage"));
        var connection = new AiConnectionRow(AiProvider.OpenAI, "model", change == "key" ? null : "cipher-key", change != "pause", 1);

        Assert.Equal(reads, technical.ReadsDocuments(connection));
    }

    // Waits on its token until the timeout cancels it, like a provider that never answers.
    private sealed class WaitingConversationClient : ILlmConversationClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public async Task<Result<LlmConversationResponse>> SendAsync(LlmConversationRequest request, CancellationToken ct)
        {
            Token = ct;
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) { return AiAssistantErrors.ProviderUncertain; }
            return AiAssistantErrors.InvalidOutput;
        }
    }
}
