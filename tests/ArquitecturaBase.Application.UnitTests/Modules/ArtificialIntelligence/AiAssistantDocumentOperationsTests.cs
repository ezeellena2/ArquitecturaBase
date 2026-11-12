using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// ReadDocument is accepted like NormalizeAudio: the same receipts, quotas, capacity and replay, with a profile of its own and no tools.
public sealed class AiAssistantDocumentOperationsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static AssistantOperationRequest DocumentRequest(AssistantOperationsFixture fixture, string? channel = null)
    {
        var request = fixture.Request();
        return request with { Text = "", Context = request.Context with { ChannelId = channel ?? request.Context.ChannelId,
            SubjectUserId = null, RequiredPermissions = new(["fictional.channel.read"], []), AllowedCapabilities = [], AllowedTerminals = [] },
            Document = new("documents/private-object.pdf", "Comprobante.pdf", "application/pdf", 2048, new string('a', 64), 2, request.ContentExpiresAtUtc) };
    }

    [Fact]
    public async Task A_reading_accepts_Read_without_Send_Home_tools_or_text_model_qualification()
    {
        var fixture = new AssistantOperationsFixture(qualified: false, documentQualified: true);
        fixture.Permissions.Permissions[fixture.ActorId] = ["fictional.channel.read"];
        var request = DocumentRequest(fixture);

        var result = await fixture.Service.SubmitAsync(request, Ct);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var turn = Assert.Single(fixture.Store.Turns);
        Assert.Equal("vision-model", turn.Model);
        Assert.Equal("model", fixture.Store.Connection.Model);
        Assert.Equal(request.ContentExpiresAtUtc, turn.ContentExpiresAtUtc);
        Assert.Equal(1, fixture.Work.Commits);
        Assert.Equal(1, fixture.Store.BudgetReads);
        Assert.Equal(0, fixture.Source.Executions);
        var profile = AiAssistantChannelPolicy.Read(turn.ExecutionProfileJson);
        Assert.True(profile.IsSuccess);
        Assert.Equal(AiAssistantPurpose.ReadDocument, profile.Value.Purpose);
        Assert.Equal(("document-read-v1", 1), (profile.Value.InstructionsId, profile.Value.InstructionsVersion));
        Assert.Equal(("fixture-document", 4), (profile.Value.ModelProfileId, profile.Value.ModelProfileVersion));
        Assert.Empty(profile.Value.ChannelContext.AllowedCapabilities);
        Assert.Empty(profile.Value.ChannelContext.AllowedTerminals);
        Assert.Contains("\"purpose\":2", turn.ExecutionProfileJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("documents/private-object.pdf", turn.ExecutionProfileJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("documents/private-object.pdf", turn.ProtectedQuestion!, StringComparison.Ordinal);
        var receipt = Assert.Single(fixture.Store.Receipts);
        Assert.True(receipt.IsDocument);
        Assert.False(receipt.IsAudio);
        Assert.Equal(request.Context.ChannelId, receipt.ChannelId);
    }

    [Fact]
    public async Task The_copy_bounds_the_turn_and_a_replay_does_not_accept_or_count_again()
    {
        var fixture = new AssistantOperationsFixture(documentQualified: true);
        var request = DocumentRequest(fixture);
        request = request with { Document = request.Document! with { ExpiresAtUtc = request.ContentExpiresAtUtc.AddMinutes(-15) } };
        var first = await fixture.Service.SubmitAsync(request, Ct);
        Assert.True(first.IsSuccess);
        Assert.Equal(request.Document!.ExpiresAtUtc, Assert.Single(fixture.Store.Turns).ContentExpiresAtUtc);
        fixture.Store.Connection = fixture.Store.Connection with { Enabled = false, Revision = 2 };

        var repeated = await fixture.Service.SubmitAsync(request, Ct);

        Assert.True(repeated.IsSuccess);
        Assert.Equal(first.Value, repeated.Value);
        Assert.Single(fixture.Store.Receipts);
        Assert.Equal(1, fixture.Store.BudgetReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Independent_readings_share_an_actor_and_channel_in_conversations_of_their_own(bool running)
    {
        var fixture = new AssistantOperationsFixture(documentQualified: true);
        var first = DocumentRequest(fixture);
        Assert.True((await fixture.Service.SubmitAsync(first, Ct)).IsSuccess);
        var original = Assert.Single(fixture.Store.Turns);
        if (running) Assert.True(original.Claim(Guid.NewGuid(), fixture.Clock.GetUtcNow().UtcDateTime.AddMinutes(1)).IsSuccess);
        var second = DocumentRequest(fixture) with { Document = first.Document! with { StorageKey = "documents/another-private-object.pdf" } };

        var accepted = await fixture.Service.SubmitAsync(second, Ct);

        Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Error.Code : null);
        Assert.Equal(2, fixture.Store.Conversations.Count);
        Assert.True((await fixture.Service.CancelAsync(second.Context, second.RequestId, Ct)).IsSuccess);
        Assert.Equal(running ? AiAssistantTurnState.Running : AiAssistantTurnState.Pending, original.State);
        Assert.Null(original.CancelRequestedAtUtc);
        Assert.Equal(AiAssistantTurnState.Canceled, fixture.Store.Turns.Single(turn => turn.RequestId == second.RequestId).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_reading_never_takes_or_waits_for_the_interpretation_slot(bool readingFirst)
    {
        var fixture = new AssistantOperationsFixture(audioQualified: true, documentQualified: true);
        var text = fixture.Request();
        var reading = DocumentRequest(fixture);
        foreach (var request in readingFirst ? new[] { reading, text } : new[] { text, reading })
        {
            var result = await fixture.Service.SubmitAsync(request, Ct);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }
        Assert.True((await fixture.Service.SubmitAsync(AiAssistantAudioOperationsRequests.Audio(fixture), Ct)).IsSuccess);

        var blocked = await fixture.Service.SubmitAsync(fixture.Request(), Ct);

        Assert.Equal(AiAssistantErrors.ActiveTurn, blocked.Error);
        Assert.Equal(3, fixture.Store.Turns.Count);
    }

    [Fact]
    public async Task A_reading_still_obeys_the_global_pending_capacity()
    {
        var fixture = new AssistantOperationsFixture(documentQualified: true);
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        for (var i = 0; i < 50; i++)
        {
            var conversation = AiConversation.Create(fixture.ActorId, now);
            fixture.Store.Conversations.Add(conversation);
            fixture.Store.Turns.Add(AiAssistantTurn.Create(fixture.ActorId, conversation.Id, Guid.NewGuid(), 1, "",
                "en", "UTC", AiProvider.OpenAI, "model", 1, now, now.AddHours(1)));
        }

        var blocked = await fixture.Service.SubmitAsync(DocumentRequest(fixture), Ct);

        Assert.Equal(AiAssistantErrors.CapacityLimited(2).Code, blocked.Error.Code);
        Assert.Empty(fixture.Store.Receipts);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("audio")]
    [InlineData("attachment")]
    [InlineData("previous")]
    [InlineData("tools")]
    [InlineData("terminal")]
    [InlineData("expiry")]
    [InlineData("type")]
    [InlineData("size")]
    [InlineData("pages")]
    [InlineData("hash")]
    [InlineData("name")]
    public async Task Invalid_document_inputs_are_rejected_before_any_acceptance(string change)
    {
        var fixture = new AssistantOperationsFixture(audioQualified: true, documentQualified: true);
        var request = DocumentRequest(fixture);
        var document = request.Document!;
        request = change switch
        {
            "text" => request with { Text = "Another input" },
            "audio" => request with { Audio = AiAssistantAudioOperationsRequests.Audio(fixture).Audio },
            "attachment" => request with { Attachment = FictionalAttachment.Input(fixture.Clock.GetUtcNow().UtcDateTime.AddHours(2)) },
            "previous" => request with { PreviousRequestId = Guid.NewGuid() },
            "tools" => request with { Context = request.Context with { AllowedCapabilities = [new("fixture.proposal", 1)] } },
            "terminal" => request with { Context = request.Context with { AllowedTerminals = [new("assistant_proposal", 1)] } },
            "expiry" => request with { Document = document with { ExpiresAtUtc = request.OccurredAtUtc } },
            "type" => request with { Document = document with { ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document" } },
            "size" => request with { Document = document with { Size = StructuredAttachmentLimits.MaximumBytes + 1 } },
            "pages" => request with { Document = document with { Pages = 11 } },
            "hash" => request with { Document = document with { Sha256 = "bad" } },
            _ => request with { Document = document with { Name = "../comprobante.pdf" } },
        };

        Assert.Equal(AiAssistantErrors.Invalid, (await fixture.Service.SubmitAsync(request, Ct)).Error);
        Assert.Empty(fixture.Store.Turns);
        Assert.Equal(0, fixture.Work.Transactions);
    }

    [Fact]
    public async Task Reusing_a_reading_identifier_with_another_private_object_conflicts()
    {
        var fixture = new AssistantOperationsFixture(documentQualified: true);
        var request = DocumentRequest(fixture);
        Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsSuccess);
        var changed = request with { Document = request.Document! with { Pages = 3 } };

        var result = await fixture.Service.SubmitAsync(changed, Ct);

        Assert.Equal(AiAssistantErrors.RequestReused, result.Error);
        Assert.Single(fixture.Store.Turns);
    }

    [Theory]
    [InlineData(false, "image/png", 1)]
    [InlineData(true, "image/png", 1)]
    [InlineData(true, "application/pdf", 10)]
    public async Task A_document_that_no_qualified_profile_reads_is_unavailable_without_accepting(bool profile, string type, int pages)
    {
        var fixture = new AssistantOperationsFixture(documentQualified: profile);
        if (profile) fixture.DocumentProfiles[0] = fixture.DocumentProfiles[0] with { ContentTypes = ["application/pdf"], MaximumPages = 5 };
        var request = DocumentRequest(fixture);
        request = request with { Document = request.Document! with { ContentType = type, Name = "Comprobante.bin", Pages = pages } };

        var result = await fixture.Service.SubmitAsync(request, Ct);

        Assert.Equal(AiAssistantErrors.Unavailable, result.Error);
        Assert.Empty(fixture.Store.Turns);
        Assert.Empty(fixture.Store.Receipts);
    }

    [Fact]
    public void Reading_diagnostics_and_the_profile_do_not_reveal_content_or_private_paths()
    {
        var fixture = new AssistantOperationsFixture(documentQualified: true);
        var request = DocumentRequest(fixture);
        Assert.DoesNotContain(request.Document!.StorageKey, request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(request.Document.StorageKey, request.Document.ToString(), StringComparison.Ordinal);
        var response = new AssistantOperationResponse(request.RequestId, "completed", null, null, null)
            { DocumentReading = new(StructuredDocumentReadability.Readable, null, "es", "private-reading", false) };
        Assert.DoesNotContain("private-reading", response.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-reading", response.DocumentReading.ToString(), StringComparison.Ordinal);
        var json = JsonSerializer.Serialize(new AiAssistantExecutionProfileSnapshot("text", 1, "capabilities-v1", 1, fixture.Context));
        Assert.DoesNotContain("Purpose", json, StringComparison.Ordinal);
    }
}

internal static class AiAssistantAudioOperationsRequests
{
    public static AssistantOperationRequest Audio(AssistantOperationsFixture fixture)
    {
        var request = fixture.Request();
        return request with { Text = "", Context = request.Context with {
            SubjectUserId = null, RequiredPermissions = new(["fictional.channel.read"], []), AllowedCapabilities = [], AllowedTerminals = [] },
            Audio = new("audio/private-object", "audio.ogg", "audio/ogg; codecs=opus", 42, new string('a', 64), request.ContentExpiresAtUtc) };
    }
}
