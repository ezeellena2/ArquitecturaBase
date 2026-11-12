using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// ReadDocument with PostgreSQL, the real worker and the real OpenAI document client against a fictitious HTTP handler: acceptance and
// receipts, the reading and Get, an uncertain POST and the quotas of readings. Fictitious bytes: nothing here qualifies a real model.
[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantDocumentReadingTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly byte[] Pdf = "%PDF-1.7 fictitious transfer receipt"u8.ToArray();
    private const string Key = "fixture/documents/comprobante.pdf";

    [Fact]
    public async Task A_document_is_accepted_read_once_by_the_vision_model_and_returned_while_its_content_lasts()
    {
        var files = new HomeAudioFiles();
        // The model may answer with a locale: the reading keeps its primary language code.
        using var provider = DocumentReadingHandler.Json(DocumentReadingHandler.Completed(
            DocumentReadingHandler.Reading("| Monto | $ 1.000 |\n| Fecha | 11/11/2026 |", "partial", "cut", "es-AR")));
        await using var scenario = await ScenarioAsync(files, provider);
        var actor = Actor(scenario);
        var request = await DocumentAsync(files, actor, "fixture-documents");

        var accepted = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(request, Ct));
        Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Error.Code : null);
        Assert.True(await scenario.ProcessNextAsync());

        Assert.Equal(1, provider.Calls);
        Assert.Equal("https://api.openai.com/v1/responses", Assert.Single(provider.Urls));
        Assert.Equal("fictional-assistant-key", Assert.Single(provider.Credentials));
        using (var body = JsonDocument.Parse(Assert.Single(provider.Bodies)))
        {
            Assert.Equal("fictional-vision", body.RootElement.GetProperty("model").GetString());
            Assert.False(body.RootElement.GetProperty("store").GetBoolean());
            Assert.Contains("[ilegible]", body.RootElement.GetProperty("instructions").GetString(), StringComparison.Ordinal);
            var file = Assert.Single(Assert.Single(body.RootElement.GetProperty("input").EnumerateArray()).GetProperty("content").EnumerateArray());
            Assert.Equal("input_file", file.GetProperty("type").GetString());
            Assert.Equal("data:application/pdf;base64," + Convert.ToBase64String(Pdf), file.GetProperty("file_data").GetString());
        }
        Assert.Empty(scenario.Handler.Requests);
        var result = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().GetAsync(request.Context, request.RequestId, Ct));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal("completed", result.Value.State);
        Assert.Equal(new StructuredDocumentReading(StructuredDocumentReadability.Partial, StructuredDocumentProblem.Cut, "es",
            "| Monto | $ 1.000 |\n| Fecha | 11/11/2026 |", false), result.Value.DocumentReading);
        Assert.Equal(("document-read-v1", "fixture-document"), (result.Value.Provenance!.InstructionsId, result.Value.Provenance.ModelProfileId));
        await scenario.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var turn = await db.Set<AiAssistantTurn>().IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.RequestId == request.RequestId, Ct);
            Assert.Equal((AiAssistantTurnState.Completed, "fictional-vision", 1200L, 300L), (turn.State, turn.Model, turn.InputTokens!.Value, turn.OutputTokens!.Value));
            Assert.DoesNotContain("11/11/2026", turn.ProtectedAnswer!, StringComparison.Ordinal);
            Assert.DoesNotContain(Key, turn.ProtectedQuestion!, StringComparison.Ordinal);
            var receipt = await db.Set<AiAssistantRequestReceipt>().AsNoTracking().SingleAsync(row => row.RequestId == request.RequestId, Ct);
            Assert.Equal((true, (bool?)false, "fixture-documents"), (receipt.IsDocument, receipt.IsAudio, receipt.ChannelId));
            return true;
        });

        var replay = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(request, Ct));
        Assert.Equal(accepted.Value.RequestId, replay.Value.RequestId);
        Assert.False(await scenario.ProcessNextAsync());
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task A_reading_whose_post_may_have_reached_the_provider_is_uncertain_and_never_repeated()
    {
        var files = new HomeAudioFiles();
        using var provider = DocumentReadingHandler.Json("{}", HttpStatusCode.ServiceUnavailable);
        await using var scenario = await ScenarioAsync(files, provider);
        var request = await DocumentAsync(files, Actor(scenario), "fixture-documents");
        Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(request, Ct))).IsSuccess);

        Assert.True(await scenario.ProcessNextAsync());
        Assert.False(await scenario.ProcessNextAsync());

        Assert.Equal(1, provider.Calls);
        var result = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().GetAsync(request.Context, request.RequestId, Ct));
        Assert.Equal(("uncertain", AiAssistantErrors.ProviderUncertain.Code), (result.Value.State, result.Value.FailureCode));
        Assert.Null(result.Value.DocumentReading);
    }

    [Fact]
    public async Task A_copy_that_is_not_the_one_accepted_never_reaches_the_provider()
    {
        var files = new HomeAudioFiles();
        using var provider = DocumentReadingHandler.Json(DocumentReadingHandler.Completed(DocumentReadingHandler.Reading()));
        await using var scenario = await ScenarioAsync(files, provider);
        var request = await DocumentAsync(files, Actor(scenario), "fixture-documents");
        Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(request, Ct))).IsSuccess);
        await files.WriteAsync(Key, new MemoryStream("%PDF-1.7 another document"u8.ToArray()), "application/pdf", Ct);

        Assert.True(await scenario.ProcessNextAsync());

        Assert.Equal(0, provider.Calls);
        var result = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().GetAsync(request.Context, request.RequestId, Ct));
        Assert.Equal(("failed", AiAssistantErrors.ContentUnavailable.Code), (result.Value.State, result.Value.FailureCode));
    }

    [Fact]
    public async Task Real_acceptance_counts_readings_per_person_across_channels_and_apart_from_interpretations()
    {
        var files = new HomeAudioFiles();
        using var provider = DocumentReadingHandler.Json(DocumentReadingHandler.Completed(DocumentReadingHandler.Reading()));
        await using var scenario = await ScenarioAsync(files, provider, limits => limits with { DocumentActorDailyQuota = 1, ActorDailyQuota = 1 });
        var actor = Actor(scenario);
        var first = await DocumentAsync(files, actor, "fixture-documents");
        var operations = (Func<IServiceProvider, IAssistantOperations>)(services => services.GetRequiredService<IAssistantOperations>());
        Assert.True((await scenario.ScopeAsync(services => operations(services).SubmitAsync(first, Ct))).IsSuccess);
        Assert.True((await scenario.ScopeAsync(services => operations(services).CancelAsync(first.Context, first.RequestId, Ct))).IsSuccess);

        var anotherChannel = await DocumentAsync(files, actor, "fixture-other-documents");
        var blocked = await scenario.ScopeAsync(services => operations(services).SubmitAsync(anotherChannel, Ct));
        Assert.Equal(AiAssistantErrors.QuotaExceeded(1).Code, blocked.Error.Code);

        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var context = new AssistantChannelContext("fixture-documents", actor, actor, "es", "UTC", new([], []),
            [new("fixture.proposal", 1)], [new("assistant_proposal", 1)]);
        var interpretation = new AssistantOperationRequest(Guid.NewGuid(), context, "Fictitious visit", now, now.AddHours(1));
        Assert.True((await scenario.ScopeAsync(services => operations(services).SubmitAsync(interpretation, Ct))).IsSuccess);
        Assert.True((await scenario.ScopeAsync(services => operations(services).CancelAsync(context, interpretation.RequestId, Ct))).IsSuccess);
        await scenario.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.Set<AiConversation>().RemoveRange(await db.Set<AiConversation>().IgnoreQueryFilters()
                .Where(conversation => conversation.OwnerUserId == actor && conversation.ChannelId != "home").ToArrayAsync(Ct));
            await db.SaveChangesAsync(Ct);
            return true;
        });

        Assert.Equal(AiAssistantErrors.QuotaExceeded(1).Code,
            (await scenario.ScopeAsync(services => operations(services).SubmitAsync(anotherChannel with { RequestId = Guid.NewGuid() }, Ct))).Error.Code);
        Assert.Equal(AiAssistantErrors.QuotaExceeded(1).Code,
            (await scenario.ScopeAsync(services => operations(services).SubmitAsync(interpretation with { RequestId = Guid.NewGuid() }, Ct))).Error.Code);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task The_repository_counts_readings_apart_and_the_installation_caps_them_outside_its_global_quota()
    {
        var files = new HomeAudioFiles();
        using var provider = DocumentReadingHandler.Json(DocumentReadingHandler.Completed(DocumentReadingHandler.Reading()));
        await using var scenario = await ScenarioAsync(files, provider, limits => limits with { DocumentInstallationDailyQuota = 12, InstallationDailyQuota = 12 });
        var actor = Actor(scenario);
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var day = DateOnly.FromDateTime(now);
        await scenario.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            for (var i = 0; i < 12; i++) db.Set<AiAssistantRequestReceipt>().Add(AiAssistantRequestReceipt.Create(Guid.CreateVersion7(), Guid.NewGuid(),
                "fictional", "test", Guid.NewGuid(), Guid.NewGuid(), now, now.AddDays(1), "channel-" + i, false, isDocument: true));
            foreach (var (channel, audio, document) in new[] { ("channel-a", false, true), ("channel-b", false, true), ("channel-a", false, false), ("channel-a", true, false) })
                db.Set<AiAssistantRequestReceipt>().Add(AiAssistantRequestReceipt.Create(actor, Guid.NewGuid(), "fictional", "test", Guid.NewGuid(), Guid.NewGuid(),
                    now, now.AddDays(1), channel, audio, document));
            await db.SaveChangesAsync(Ct);
            var repository = services.GetRequiredService<IAiAssistantRepository>();
            var readings = await repository.CountBudgetAsync(actor, day, "channel-a", false, true, Ct);
            Assert.Equal((2, 14), (readings.ActorAccepted, readings.InstallationAccepted));
            var interpretations = await repository.CountBudgetAsync(actor, day, "channel-a", false, false, Ct);
            Assert.Equal((1, 2), (interpretations.ActorAccepted, interpretations.InstallationAccepted));
            Assert.Equal(1, (await repository.CountBudgetAsync(actor, day, "channel-a", true, false, Ct)).ActorAccepted);
            Assert.Equal(0, (await repository.CountBudgetAsync(actor, day, "channel-b", false, false, Ct)).ActorAccepted);
            return true;
        });
        var document = await DocumentAsync(files, actor, "fixture-documents");

        var reading = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(document, Ct));

        Assert.Equal(AiAssistantErrors.QuotaExceeded(1).Code, reading.Error.Code);
        var context = new AssistantChannelContext("fixture-documents", actor, actor, "es", "UTC", new([], []),
            [new("fixture.proposal", 1)], [new("assistant_proposal", 1)]);
        var interpretation = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>()
            .SubmitAsync(new AssistantOperationRequest(Guid.NewGuid(), context, "Fictitious visit", now, now.AddHours(1)), Ct));
        Assert.True(interpretation.IsSuccess, interpretation.IsFailure ? interpretation.Error.Code : null);
    }

    [Fact]
    public async Task The_check_never_stores_a_receipt_that_is_an_audio_and_a_document()
    {
        var actor = Guid.CreateVersion7();
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var receipt = AiAssistantRequestReceipt.Create(actor, Guid.NewGuid(), "fictional", "test", Guid.NewGuid(), Guid.NewGuid(), now, now.AddDays(1),
            "fixture-documents", false, isDocument: true);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<AiAssistantRequestReceipt>().Add(receipt);
            await db.SaveChangesAsync(Ct);
        });
        try
        {
            var rejected = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => factory.ExecuteDbContextAsync(db =>
                db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "AiAssistantRequestReceipts" SET "IsAudio" = TRUE WHERE "Id" = {receipt.Id}""", Ct)));
            Assert.Equal(("23514", "CK_AiAssistantRequestReceipts_AudioOrDocument"), (rejected.SqlState, rejected.ConstraintName));
        }
        finally
        {
            await factory.ExecuteDbContextAsync(async db =>
            {
                db.Set<AiAssistantRequestReceipt>().RemoveRange(await db.Set<AiAssistantRequestReceipt>().Where(row => row.OwnerUserId == actor).ToArrayAsync(Ct));
                await db.SaveChangesAsync(Ct);
            });
        }
    }

    private Task<AiAssistantExecutionScenario> ScenarioAsync(HomeAudioFiles files, DocumentReadingHandler provider,
        Func<AiAssistantLimits, AiAssistantLimits>? limits = null) => AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "fixture.proposal", services =>
        {
            services.AddSingleton<IAssistantCapabilitySource>(new RoundtripAssistantSource());
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(files);
            services.AddHttpClient(AiAssistantProvidersRegistration.OpenAiDocumentHttpClientName).ConfigurePrimaryHttpMessageHandler(() => provider);
            services.PostConfigure<AiAssistantOptions>(options =>
            {
                options.Limits = limits?.Invoke(options.Limits) ?? options.Limits;
                options.ModelProfiles = options.ModelProfiles.Where(profile => profile.Provider == AiProvider.OpenAI && profile.Model == "fictional-model")
                    .Select(profile => profile with { Id = "fixture-model", Version = 1 }).ToArray();
                options.DocumentModelProfiles = [new(AiProvider.OpenAI, "fictional-vision", "fixture-document", 1,
                    ["application/pdf", "image/jpeg", "image/png"], StructuredAttachmentLimits.MaximumBytes, StructuredAttachmentLimits.MaximumPages)];
            });
        });

    private static Guid Actor(AiAssistantExecutionScenario scenario) =>
        Guid.Parse(scenario.Client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());

    private async Task<AssistantOperationRequest> DocumentAsync(HomeAudioFiles files, Guid actor, string channel)
    {
        await files.WriteOnceAsync(Key, new MemoryStream(Pdf), "application/pdf", Ct);
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        return new AssistantOperationRequest(Guid.NewGuid(), new(channel, actor, null, "es", "UTC", new([], []), [], []), "", now, now.AddHours(1))
        {
            Document = new(Key, "Comprobante.pdf", "application/pdf", Pdf.Length, Convert.ToHexStringLower(SHA256.HashData(Pdf)), 1, now.AddHours(1)),
        };
    }
}
