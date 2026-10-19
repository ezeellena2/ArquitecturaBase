using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Files;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Files;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Serve_stage_five_app_with_one_real_database_private_documents_and_controlled_channels()
    {
        var webRoot = Environment.GetEnvironmentVariable("STAGE5_APP_PREVIEW_WEB_ROOT");
        if (string.IsNullOrWhiteSpace(webRoot) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STAGE5_APP_PREVIEW_STATE"))) return;
        var initialClock = factory.Clock.GetUtcNow();
        try { await ServeStageFivePreviewAsync(webRoot); }
        finally { factory.Clock.AdjustTime(initialClock); }
    }

    private async Task ServeStageFivePreviewAsync(string webRoot)
    {

        await using var emulator = new AzuriteFixture();
        await emulator.StartAsync(Ct);
        var store = new AzureBlobFileStore(Options.Create(new FileStorageOptions
        { Enabled = true, CredentialMode = "ConnectionString", ConnectionString = emulator.ConnectionString }));
        var actionChannel = new CapturingFormActionChannel();
        var audio = await File.ReadAllBytesAsync(Path.GetFullPath(Path.Combine(webRoot,
            "../../ArquitecturaBase/harnesses/assistant-qualification/fixtures/audio/visit-es.ogg")), Ct);
        var driver = factory.Services.GetRequiredService<IStructuredFormAppPreviewDriver>();
        var preview = await driver.PrepareAsync(factory, builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(store);
            services.RemoveAll<IStructuredRecordActionChannel>();
            services.AddSingleton<IStructuredRecordActionChannel>(actionChannel);
        }), [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish,
            BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RulesManage,
            BusinessFormsPermissions.ApprovalsDecide, BusinessFormsPermissions.SuggestionsManage,
            BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage], audio, Ct);
        await using var lifetime = preview.Lifetime;
        using var manager = PreviewClient(preview.Host, preview.ManagerId);
        using var author = PreviewClient(preview.Host, preview.AuthorId);
        var languagePayload = Payload("Visita de idioma");
        languagePayload["name"] = new JsonObject { ["es"] = "Visita de idioma" };
        languagePayload["draft"]!["submitLabel"] = new JsonObject { ["es"] = "Enviar visita" };
        languagePayload["draft"]!["fields"]![0]!["label"] = new JsonObject { ["es"] = "Lugar de la visita", ["en"] = "Visit location" };
        languagePayload["draft"]!["fields"]![0]!["help"] = new JsonObject { ["es"] = "Contanos dónde fue" };
        var languageDraft = await CreateAsync(manager, languagePayload);
        var languageDefinitionId = languageDraft.GetProperty("definition").GetProperty("id").GetGuid();
        var approval = await PreviewApprovalRoleAsync(preview.ManagerId);
        var photo = Field("photo", false) with { Files = new(0, 2, "cameraOrGallery") };
        var source = await SeedSheetAsync(author, 0, photo);
        var target = await SeedSheetAsync(author, 0);
        var sourceVersion = await PreviewPublishedVersionAsync(source.Id, preview.ManagerId);
        var targetVersion = await PreviewPublishedVersionAsync(target.Id, preview.ManagerId);
        var suggestionId = Guid.NewGuid();
        var suggestionsRoute = $"/api/forms/definitions/{source.Id}/suggestions";
        var suggestions = new[] { new { id = suggestionId, destinationDefinitionId = target.Id, mode = "Always", when = (string?)null,
            isActive = true, conditions = Array.Empty<object>(), mappings = new[] {
                new { source = new { kind = "RecordNumber" }, destinationFieldId = target.Fields[1].FieldId } } } };
        using var suggestionSaved = await manager.PostAsJsonAsync(suggestionsRoute, new { expectedRevision = 0, suggestions }, Ct);
        Assert.Equal(HttpStatusCode.Created, suggestionSaved.StatusCode);
        var setId = (await suggestionSaved.ReadJsonAsync()).GetProperty("id").GetGuid();
        var rules = new[] {
            new FormRule(Guid.NewGuid(), "Received", [], [new(Guid.NewGuid(), "RequestApproval", approval.RoleId, null, false)]),
            new FormRule(Guid.NewGuid(), "Approved", [], [new(Guid.NewGuid(), "GeneratePdf", null, null, false)]) };
        using var rulesSaved = await manager.PostAsJsonAsync($"/api/forms/definitions/{source.Id}/rules", new { expectedRevision = 0, rules }, Ct);
        Assert.Equal(HttpStatusCode.Created, rulesSaved.StatusCode);
        var design = new FormPdfDesign(new Dictionary<string, string> { ["es"] = "Visita ficticia con foto" },
            [source.Fields[1].FieldId, photo.FieldId], true, new Dictionary<string, string> { ["es"] = "Comprobante de prueba" }, true);
        using var designSaved = await manager.PostAsJsonAsync($"/api/forms/definitions/{source.Id}/pdf-design", new { expectedRevision = 0, design }, Ct);
        Assert.Equal(HttpStatusCode.Created, designSaved.StatusCode);
        using var englishPreview = await manager.PostAsJsonAsync($"/api/forms/definitions/{source.Id}/pdf-design/preview",
            new { design, culture = "en" }, Ct);
        Assert.Equal(HttpStatusCode.OK, englishPreview.StatusCode);
        Assert.Equal("application/pdf", englishPreview.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new[] { "es" }, englishPreview.Content.Headers.ContentLanguage);
        Assert.True((await englishPreview.Content.ReadAsByteArrayAsync(Ct)).Length > 1000);

        var pending = await PreviewCaptureAsync(preview.Host, source.Id, sourceVersion, preview.AuthorId, source.Fields[1], photo, "Visita pendiente");
        var secondPending = await PreviewCaptureAsync(preview.Host, source.Id, sourceVersion, preview.AuthorId, source.Fields[1], photo, "Segunda visita pendiente");
        var approved = await PreviewCaptureAsync(preview.Host, source.Id, sourceVersion, preview.AuthorId, source.Fields[1], photo, "Visita aprobada");
        var pendingRecords = new[] { pending, secondPending };
        var owned = new[] { pending, secondPending, approved };
        await AdvanceFormsAsync(Ct);
        foreach (var record in pendingRecords)
        {
            using var pendingWorkflow = await manager.GetAsync($"/api/forms/records/{record}/workflow", Ct);
            Assert.Equal(HttpStatusCode.OK, pendingWorkflow.StatusCode);
            var pendingState = await pendingWorkflow.ReadJsonAsync();
            Assert.Equal("PendingApproval", pendingState.GetProperty("recordState").GetString());
            Assert.Equal("Open", pendingState.GetProperty("task").GetProperty("state").GetString());
        }
        using var workflowBefore = await manager.GetAsync($"/api/forms/records/{approved}/workflow", Ct);
        Assert.Equal(HttpStatusCode.OK, workflowBefore.StatusCode);
        var task = (await workflowBefore.ReadJsonAsync()).GetProperty("task");
        using var decision = await manager.PostAsJsonAsync($"/api/forms/approvals/{task.GetProperty("id").GetGuid()}/decisions",
            new { expectedRevision = task.GetProperty("revision").GetInt64(), decision = "Approved" }, Ct);
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        await AdvanceFormsAsync(Ct);
        using var workflowAfter = await manager.GetAsync($"/api/forms/records/{approved}/workflow", Ct);
        var document = Assert.Single((await workflowAfter.ReadJsonAsync()).GetProperty("documents").EnumerateArray());
        using var pdf = await manager.GetAsync($"/api/forms/records/{approved}/documents/{document.GetProperty("id").GetGuid()}/content", Ct);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.True((await pdf.Content.ReadAsByteArrayAsync(Ct)).Length > 1000);

        var child = await PreviewSuggestionChildAsync(preview.Host, preview.AuthorId, preview.ManagerId,
            approved, setId, suggestionId, target.Id, targetVersion, target.Fields[1].Key);
        var proposal = await FormCatalogProposalScenario.CreateAsync(factory, "es", Ct);
        var catalog = await proposal.AcceptAsync(Ct);
        await using var missing = await FormMissingRequestScenario.CreateAsync(factory, "es", Ct, isolated: false);
        var missingRequest = await missing.PrepareAsync(ct: Ct);
        var missingCaptured = await missing.CaptureAsync(missingRequest, Ct);
        Assert.True(missingCaptured.IsSuccess);
        Assert.True((await missing.CaptureAsync(await missing.PrepareAsync(existing: missingCaptured.Value.TopicId, ct: Ct), Ct)).IsSuccess);
        var fixtures = new {
            languageDefinitionId,
            sourceDefinitionId = source.Id, targetDefinitionId = target.Id, pendingRecordId = pending, pendingRecordIds = pendingRecords,
            approvedRecordId = approved, childRecordId = child, pendingCatalogId = catalog.CatalogId,
            contactId = preview.ContactId, audioContactId = preview.AudioContactId, alwaysContactId = preview.AlwaysContactId,
            assistantContactId = preview.AssistantContactId,
            fixedDocumentId = document.GetProperty("id").GetGuid(), pdfPreviewRequestedCulture = "en", pdfActualCulture = "es" };
        await preview.HoldAsync([("decider-only", approval.DeciderId), ("missing-manager-only", missing.ManagerId)], fixtures, AdvanceFormsAsync, Ct);

        async Task AdvanceFormsAsync(CancellationToken token)
        {
            var publicationIds = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPublicationOperation>().AsNoTracking()
                .Where(operation => operation.DefinitionId == languageDefinitionId
                    && operation.State != FormPublicationState.Published && operation.State != FormPublicationState.Failed)
                .OrderBy(operation => operation.AcceptedAtUtc).Select(operation => operation.Id).ToArrayAsync(token));
            foreach (var operation in publicationIds)
                await preview.Host.Services.GetRequiredService<FormPublicationBackgroundService>().ProcessNextAsync(operation, token);
            var runs = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>().AsNoTracking()
                .Where(run => owned.Contains(run.RecordId) && (run.State == FormActionRunState.Waiting || run.State == FormActionRunState.Running))
                .OrderBy(run => run.CreatedAtUtc).ThenBy(run => run.Id).Select(run => run.Id).ToArrayAsync(token));
            foreach (var run in runs)
                await preview.Host.Services.GetRequiredService<FormActionBackgroundService>().ProcessNextAsync(run, token);
        }
    }

    private static HttpClient PreviewClient(WebApplicationFactory<Program> host, Guid actor)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.ToString());
        return client;
    }

    private async Task<(Guid RoleId, Guid DeciderId)> PreviewApprovalRoleAsync(Guid managerId) =>
        await factory.InTransactionAsync(async services =>
        {
            var name = "preview-approval-" + Guid.NewGuid().ToString("N");
            var roleId = await services.GetRequiredService<IRoleRepository>().CreateAsync(name, null, [BusinessFormsPermissions.ApprovalsDecide], Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var decider = await users.CreateAsync(Email.Create(TestEmails.Unique("preview-decider")).Value, null, false, "Aprobador ficticio", "es", Ct);
            await users.SetRolesAsync(decider.Id, [name], Ct);
            var db = services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>();
            var managerRoles = await db.UserRoles.Where(link => link.UserId == managerId)
                .Join(db.Roles, link => link.RoleId, role => role.Id, (_, role) => role.Name!).ToArrayAsync(Ct);
            await users.SetRolesAsync(managerId, [.. managerRoles, name], Ct);
            return (roleId, decider.Id);
        });

    private async Task<Guid> PreviewPublishedVersionAsync(Guid definitionId, Guid actor) =>
        await factory.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>();
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(row => row.Id == definitionId, Ct);
            var version = definition.ActiveVersionId!.Value;
            var now = factory.Clock.GetUtcNow().UtcDateTime;
            var operation = BusinessFormPublicationOperation.Accept(definition.Id, version, actor, Guid.NewGuid(), definition.Revision, 1, null, [1], now);
            var fence = operation.Reserve(now, TimeSpan.FromMinutes(1)).Value;
            Assert.True(operation.RecordChannelResult(fence, now, "Published", "Verify").IsSuccess);
            Assert.True(operation.Complete(fence, now).IsSuccess);
            db.Set<BusinessFormPublicationOperation>().Add(operation);
            return version;
        });

    private async Task<Guid> PreviewCaptureAsync(WebApplicationFactory<Program> host, Guid definitionId, Guid versionId, Guid author,
        CanonicalFormField answer, CanonicalFormField photo, string text)
    {
        var image = new FormPdfRendererFixture().PhotoContent;
        var result = await InActionScopeAsync(host.Services, services => services.GetRequiredService<IBusinessFormRecordCaptureService>().CaptureAsync(
            new StructuredRecordCaptureRequest(Guid.NewGuid(), definitionId, versionId, author, factory.Clock.GetUtcNow().UtcDateTime,
                new Dictionary<string, string> { [answer.Key] = text }, Files: [
                    new(photo.FieldId, 0, "preview-photo", "photo", "visita-ficticia.jpg", "image/jpeg", Convert.ToHexStringLower(SHA256.HashData(image)), image)]), Ct));
        Assert.Equal("Completed", result.State);
        return result.RecordId!.Value;
    }

    private async Task<Guid> PreviewSuggestionChildAsync(WebApplicationFactory<Program> host, Guid author, Guid manager,
        Guid sourceRecord, Guid set, Guid suggestion, Guid target, Guid version, string key)
    {
        var prepared = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IStructuredFormSource>()
            .PrepareSuggestionAsync(new(author, "es", sourceRecord, set, suggestion) { ActorUserId = manager }, Ct));
        Assert.True(prepared.IsSuccess);
        var proposed = Assert.Single(prepared.Value.ProposedFields);
        var origin = new StructuredRecordCaptureOrigin(StructuredRecordSenderKind.Manual, manager, prepared.Value.Prefill.Reason, null,
            [new(proposed.FieldId, proposed.Type, proposed.Value)], [proposed.FieldId], prepared.Value.OriginSuggestion);
        var captured = await InActionScopeAsync(host.Services, services => services.GetRequiredService<IBusinessFormRecordCaptureService>().CaptureAsync(
            new StructuredRecordCaptureRequest(Guid.NewGuid(), target, version, author, factory.Clock.GetUtcNow().UtcDateTime.AddMinutes(1),
                new Dictionary<string, string> { [key] = "Número corregido por la persona" }, Origin: origin), Ct));
        Assert.Equal("Completed", captured.State);
        return captured.RecordId!.Value;
    }

}
