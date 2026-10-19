using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Files;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Files;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData(false, false, false, "en")]
    [InlineData(true, false, false, "es")]
    [InlineData(false, true, false, "en")]
    [InlineData(false, false, true, "es")]
    public async Task Capture_approval_pdf_photo_and_catalog_notices_keep_one_complete_language(
        bool pendingFooter, bool untranslatedCatalogItem, bool pendingApprovedNotice, string expectedCulture)
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsCreate]);
        using var manager = await OperatorAsync([BusinessFormsPermissions.RulesManage, BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage]);
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var deciderId = Guid.Parse(decider.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        Assert.NotEqual(authorId, deciderId);
        var catalog = BusinessFormCatalog.CreateManual(
            new Dictionary<string, string> { ["es"] = "Sucursales", ["en"] = "Branches" },
            new Dictionary<string, string> { ["es"] = "Sucursales disponibles", ["en"] = "Available branches" });
        var item = BusinessFormCatalogItem.Create(catalog.Id, "NORTH", "Sucursal norte", untranslatedCatalogItem ? null : "North branch", null, null, 0);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormCatalog>().Add(catalog);
            db.Set<BusinessFormCatalogItem>().Add(item);
            await db.SaveChangesAsync(Ct);
        });
        var photo = Field("photo", false) with { Files = new(0, 2, "cameraOrGallery") };
        var branch = Field("choice", true) with
        {
            Label = new Dictionary<string, string> { ["es"] = "Sucursal", ["en"] = "Branch" }, Source = new(catalog.Id)
        };
        var sheet = await SeedSheetAsync(author, 0, photo, branch);
        var seeded = await factory.ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(user => user.Id == authorId, Ct);
            db.Entry(user).Property(user => user.Culture).CurrentValue = "en";
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(definition => definition.Id == sheet.Id, Ct);
            var versionId = definition.ActiveVersionId!.Value;
            var now = factory.Clock.GetUtcNow().UtcDateTime;
            var published = BusinessFormPublicationOperation.Accept(definition.Id, versionId, authorId, Guid.NewGuid(),
                definition.Revision, 1, null, [1], now);
            var fence = published.Reserve(now, TimeSpan.FromMinutes(1)).Value;
            Assert.True(published.RecordChannelResult(fence, now, "Published", "Verify").IsSuccess);
            Assert.True(published.Complete(fence, now).IsSuccess);
            db.Set<BusinessFormPublicationOperation>().Add(published);
            await db.SaveChangesAsync(Ct);
            var roleId = await db.UserRoles.Where(link => link.UserId == deciderId).Select(link => link.RoleId).SingleAsync(Ct);
            return (VersionId: versionId, RoleId: roleId);
        });
        await using var emulator = new AzuriteFixture();
        await emulator.StartAsync(Ct);
        var store = new AzureBlobFileStore(Options.Create(new FileStorageOptions
        {
            Enabled = true, CredentialMode = "ConnectionString", ConnectionString = emulator.ConnectionString
        }));
        var channel = new CapturingFormActionChannel();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(store);
            services.RemoveAll<IStructuredRecordActionChannel>();
            services.AddSingleton<IStructuredRecordActionChannel>(channel);
        }));
        HttpClient InHost(HttpClient source)
        {
            var client = host.CreateClient();
            foreach (var header in source.DefaultRequestHeaders)
                client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            return client;
        }
        using var designer = InHost(manager);
        using var approver = InHost(decider);
        using var downloader = InHost(author);
        var design = new FormPdfDesign(
            new Dictionary<string, string> { ["es"] = "Comprobante final", ["en"] = "Final receipt" },
            [sheet.Fields[1].FieldId, photo.FieldId, branch.FieldId], true,
            pendingFooter ? new Dictionary<string, string> { ["es"] = "Pie original" }
                : new Dictionary<string, string> { ["es"] = "Pie original", ["en"] = "English footer" }, true);
        using var designSaved = await designer.PostAsJsonAsync($"/api/forms/definitions/{sheet.Id}/pdf-design", new { expectedRevision = 0, design }, Ct);
        Assert.Equal(HttpStatusCode.Created, designSaved.StatusCode);
        var received = new FormRuleAction(Guid.NewGuid(), "NotifyAuthor", null,
            new Dictionary<string, string> { ["es"] = "Recibimos {número}: {Sucursal}", ["en"] = "Received {number}: {Branch}" }, false);
        var approval = new FormRuleAction(Guid.NewGuid(), "RequestApproval", seeded.RoleId, null, false);
        var generate = new FormRuleAction(Guid.NewGuid(), "GeneratePdf", null, null, false);
        var notice = new FormRuleAction(Guid.NewGuid(), "NotifyAuthor", null,
            pendingApprovedNotice ? new Dictionary<string, string> { ["es"] = "Aprobada {número}: {Sucursal}" }
                : new Dictionary<string, string> { ["es"] = "Aprobada {número}: {Sucursal}", ["en"] = "Approved {number}: {Branch}" }, true);
        var rules = new[]
        {
            new FormRule(Guid.NewGuid(), "Received", [], [received, approval]),
            new FormRule(Guid.NewGuid(), "Approved", [], [generate, notice])
        };
        using var rulesSaved = await designer.PostAsJsonAsync($"/api/forms/definitions/{sheet.Id}/rules", new { expectedRevision = 0, rules }, Ct);
        Assert.Equal(HttpStatusCode.Created, rulesSaved.StatusCode);
        var prepared = await InActionScopeAsync(host.Services, services => services.GetRequiredService<IStructuredFormSource>()
            .PrepareAsync(authorId, "en", sheet.Id, seeded.VersionId, false, Ct));
        Assert.True(prepared.CanSend);
        Assert.Equal(expectedCulture, prepared.Culture);
        var offer = Assert.Single(prepared.Catalogs);
        Assert.Equal(expectedCulture == "en" ? "Branch" : "Sucursal", offer.Label);
        Assert.Equal(expectedCulture == "en" ? "Branches" : "Sucursales", offer.CatalogName);
        var branchText = expectedCulture == "en" && !untranslatedCatalogItem ? "North branch" : "Sucursal norte";
        Assert.Equal(branchText, Assert.Single(offer.Options).Text);
        using var changedCatalog = await designer.PutAsJsonAsync($"/api/forms/catalogs/{catalog.Id}", new
        {
            expectedRevision = 1, name = new { es = "Sucursales nuevas", en = "Changed branches" }, description = new { es = "Sucursales renombradas", en = "Renamed branches" },
            items = new[] { new { localId = "existing", itemId = item.Id, code = "NORTH", text = new { es = "Nombre nuevo", en = "Changed label" }, isActive = true } }
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, changedCatalog.StatusCode);
        var image = new FormPdfRendererFixture().PhotoContent;
        const string privateAnswer = "Visit observed by the employee";
        var request = new StructuredRecordCaptureRequest(Guid.NewGuid(), sheet.Id, seeded.VersionId, authorId, factory.Clock.GetUtcNow().UtcDateTime,
            new Dictionary<string, string> { [sheet.Fields[1].Key] = privateAnswer, [branch.Key] = item.Id.ToString("D") }, [offer],
            [new(photo.FieldId, 0, "workflow-photo", "photo", "site-photo.jpg", "image/jpeg", Convert.ToHexStringLower(SHA256.HashData(image)), image)])
        { PresentationCulture = prepared.Culture };
        var captured = await InActionScopeAsync(host.Services, services => services.GetRequiredService<IBusinessFormRecordCaptureService>().CaptureAsync(request, Ct));
        Assert.Equal("Completed", captured.State);
        var recordId = captured.RecordId!.Value;
        Assert.Equal(expectedCulture, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>()
            .Where(record => record.Id == recordId).Select(record => record.PresentationCulture).SingleAsync(Ct)));
        var number = sheet.Prefix + "-000001";
        var receivedRuns = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>()
            .Where(run => run.RecordId == recordId && run.Event == "Received").OrderBy(run => run.Order).Select(run => run.Id).ToArrayAsync(Ct));
        Assert.Equal(2, receivedRuns.Length);
        var receivedSnapshots = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>()
            .Where(run => run.RecordId == recordId && run.Event == "Received").Select(run => run.SnapshotJson).ToArrayAsync(Ct));
        foreach (var json in receivedSnapshots)
        {
            using var snapshot = JsonDocument.Parse(json);
            Assert.False(snapshot.RootElement.TryGetProperty("record", out _));
            Assert.False(snapshot.RootElement.TryGetProperty("ruleQuestions", out _));
            Assert.DoesNotContain(privateAnswer, json, StringComparison.Ordinal);
        }
        await InActionScopeAsync(host.Services, services =>
        {
            Assert.IsType<SkiaFormPdfRenderer>(services.GetRequiredService<IFormPdfRenderer>());
            return Task.FromResult(true);
        });
        var worker = host.Services.GetRequiredService<FormActionBackgroundService>();
        foreach (var run in receivedRuns) await worker.ProcessNextAsync(run, Ct);
        using var pending = await downloader.GetAsync($"/api/forms/records/{recordId}/workflow", Ct);
        Assert.Equal("PendingApproval", (await pending.ReadJsonAsync()).GetProperty("recordState").GetString());
        var task = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormTask>().Where(task => task.RecordId == recordId)
            .Select(task => new { task.Id, task.Revision, task.AuthorUserId, task.RoleId }).SingleAsync(Ct));
        Assert.Equal(authorId, task.AuthorUserId);
        Assert.Equal(seeded.RoleId, task.RoleId);
        using var changedRules = await designer.PostAsJsonAsync($"/api/forms/definitions/{sheet.Id}/rules", new { expectedRevision = 1, rules = Array.Empty<FormRule>() }, Ct);
        Assert.Equal(HttpStatusCode.Created, changedRules.StatusCode);
        using var approved = await approver.PostAsJsonAsync($"/api/forms/approvals/{task.Id}/decisions", new { expectedRevision = task.Revision, decision = "Approved" }, Ct);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var approvedRuns = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>()
            .Where(run => run.RecordId == recordId && run.Event == "Approved").OrderBy(run => run.Order).Select(run => run.Id).ToArrayAsync(Ct));
        Assert.Equal(2, approvedRuns.Length);
        var fixedPdfJson = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>()
            .Where(run => run.RecordId == recordId && run.Kind == "GeneratePdf").Select(run => run.SnapshotJson).SingleAsync(Ct));
        var fixedPdf = FormDraftPolicy.Read<FormActionSnapshot>(fixedPdfJson).Pdf!;
        Assert.Equal(authorId, fixedPdf.Author.UserId);
        Assert.Equal(deciderId, fixedPdf.Decision!.ActorUserId);
        Assert.Equal("Approved", fixedPdf.Decision.Decision);
        var fallback = pendingFooter || pendingApprovedNotice;
        Assert.Equal(fallback ? new[] { "es" } : new[] { "es", "en" }, fixedPdf.Cultures);
        using (var snapshot = JsonDocument.Parse(fixedPdfJson))
            Assert.False(snapshot.RootElement.GetProperty("pdf").TryGetProperty("record", out _));
        Assert.DoesNotContain(privateAnswer, fixedPdfJson, StringComparison.Ordinal);
        foreach (var run in approvedRuns) await worker.ProcessNextAsync(run, Ct);
        Assert.Equal(2, channel.Messages.Count);
        var executedSnapshots = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>()
            .Where(run => run.RecordId == recordId).Select(run => run.SnapshotJson).ToArrayAsync(Ct));
        foreach (var json in executedSnapshots)
            Assert.All(FormDraftPolicy.Read<FormActionSnapshot>(json).Recipients, recipient => Assert.Equal(expectedCulture, recipient.Culture));
        Assert.Equal((expectedCulture == "en" ? "Received " : "Recibimos ") + number + ": " + branchText,
            Assert.Single(channel.Messages, message => message.Document is null).Body);
        var sent = Assert.Single(channel.Messages, message => message.Document is not null);
        Assert.Equal(authorId, sent.RecipientUserId);
        Assert.Equal((expectedCulture == "en" ? "Approved " : "Aprobada ") + number + ": " + branchText, sent.Body);
        var expectedName = (expectedCulture == "en" ? "Final receipt" : "Comprobante final") + "-" + number + ".pdf";
        Assert.Equal(expectedName, sent.Document!.Name);
        using var workflow = await downloader.GetAsync($"/api/forms/records/{recordId}/workflow", Ct);
        Assert.Equal(HttpStatusCode.OK, workflow.StatusCode);
        var state = await workflow.ReadJsonAsync();
        Assert.Equal(1, state.GetProperty("ruleRevision").GetInt64());
        Assert.Equal("Approved", state.GetProperty("recordState").GetString());
        Assert.Equal("Approved", state.GetProperty("task").GetProperty("state").GetString());
        Assert.All(state.GetProperty("actions").EnumerateArray(), action => Assert.Equal("Completed", action.GetProperty("state").GetString()));
        var noticeRecipients = state.GetProperty("actions").EnumerateArray()
            .SelectMany(action => action.GetProperty("recipients").EnumerateArray()).ToArray();
        Assert.Equal(2, noticeRecipients.Length);
        Assert.All(noticeRecipients, recipient =>
        {
            Assert.Equal("Accepted", recipient.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, recipient.GetProperty("deliveredAtUtc").ValueKind);
            Assert.Equal(JsonValueKind.Null, recipient.GetProperty("readAtUtc").ValueKind);
        });
        var documents = state.GetProperty("documents").EnumerateArray().ToArray();
        Assert.Equal(fallback ? 1 : 2, documents.Length);
        var document = Assert.Single(documents, item => item.GetProperty("culture").GetString() == expectedCulture);
        Assert.Equal(expectedName, document.GetProperty("name").GetString());
        using var downloaded = await downloader.GetAsync($"/api/forms/records/{recordId}/documents/{document.GetProperty("id").GetGuid()}/content?download=true", Ct);
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal(new[] { expectedCulture }, downloaded.Content.Headers.ContentLanguage);
        var bytes = await downloaded.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), sent.Document.Sha256);
        var structure = Encoding.Latin1.GetString(bytes);
        var imageOffset = structure.IndexOf("/Subtype /Image", StringComparison.Ordinal);
        Assert.True(imageOffset >= 0);
        var contentOffset = structure.IndexOf("stream\n", imageOffset, StringComparison.Ordinal) + 7;
        var contentEnd = structure.IndexOf("\nendstream", contentOffset, StringComparison.Ordinal);
        using var embedded = Assert.IsType<SkiaSharp.SKBitmap>(SkiaSharp.SKBitmap.Decode(bytes[contentOffset..contentEnd]));
        var pixel = embedded.GetPixel(10, 10);
        Assert.True(pixel.Green > pixel.Red + 5, "The issued PDF must contain the uploaded photo thumbnail, not a preview placeholder.");
        foreach (var run in receivedRuns.Concat(approvedRuns)) await worker.ProcessNextAsync(run, Ct);
        var replay = await InActionScopeAsync(host.Services, services => services.GetRequiredService<IBusinessFormRecordCaptureService>().CaptureAsync(request, Ct));
        Assert.Equal(recordId, replay.RecordId);
        Assert.Equal(2, channel.Messages.Count);
        Assert.Equal(4, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>().CountAsync(run => run.RecordId == recordId, Ct)));
        Assert.Equal(fallback ? 1 : 2, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDocument>().CountAsync(document => document.RecordId == recordId, Ct)));
        var sample = pendingFooter ? "e2e-footer-fallback-es" : untranslatedCatalogItem ? "e2e-catalog-original-item-en"
            : pendingApprovedNotice ? "e2e-approved-notice-fallback-es" : "e2e-english-en";
        await FormWorkflowContractCapture.WriteAsync(workflow, sample, Ct);
        await FormPdfContractCapture.WriteAsync(downloaded, sample, Ct, "pdf");
    }
}
