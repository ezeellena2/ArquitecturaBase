using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Files;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Files;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Pdf_routes_require_rule_management_for_designs_and_record_read_for_fixed_documents()
    {
        using var anonymous = factory.CreateClient();
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var manager = await OperatorAsync([BusinessFormsPermissions.RulesManage]);
        var route = "/api/forms/definitions/" + Guid.NewGuid() + "/pdf-design";
        using var getAnonymous = await anonymous.GetAsync(route, Ct); Assert.Equal(HttpStatusCode.Unauthorized, getAnonymous.StatusCode);
        using var getDenied = await reader.GetAsync(route, Ct); Assert.Equal(HttpStatusCode.Forbidden, getDenied.StatusCode);
        using var getAllowed = await manager.GetAsync(route, Ct); Assert.Equal(HttpStatusCode.NotFound, getAllowed.StatusCode);
        foreach (var suffix in new[] { "", "/preview" })
        {
            object body = suffix.Length == 0 ? new { expectedRevision = 0, design = OriginalPdfDesign([]) } : new { design = OriginalPdfDesign([]), culture = "es" };
            using var missing = await anonymous.PostAsJsonAsync(route + suffix, body, Ct); Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
            using var denied = await reader.PostAsJsonAsync(route + suffix, body, Ct); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var allowed = await manager.PostAsJsonAsync(route + suffix, body, Ct); Assert.Equal(HttpStatusCode.NotFound, allowed.StatusCode);
        }
        var document = "/api/forms/records/" + Guid.NewGuid() + "/documents/" + Guid.NewGuid() + "/content";
        using var contentAnonymous = await anonymous.GetAsync(document, Ct); Assert.Equal(HttpStatusCode.Unauthorized, contentAnonymous.StatusCode);
        using var contentDenied = await manager.GetAsync(document, Ct); Assert.Equal(HttpStatusCode.Forbidden, contentDenied.StatusCode);
        using var contentAllowed = await reader.GetAsync(document, Ct); Assert.Equal(HttpStatusCode.NotFound, contentAllowed.StatusCode);
    }
    [Fact]
    public async Task Original_only_pdf_designs_have_their_own_revision_without_changing_drafts_or_published_versions()
    {
        using var manager = await OperatorAsync([BusinessFormsPermissions.RulesManage]); var seeded = await SeedSheetAsync(manager, 0);
        var before = await PdfDefinitionBaselineAsync(seeded.Id); var route = "/api/forms/definitions/" + seeded.Id + "/pdf-design";
        using var defaults = await manager.GetAsync(route, Ct); Assert.Equal(HttpStatusCode.OK, defaults.StatusCode);
        var initial = await defaults.ReadJsonAsync(); Assert.Equal(0, initial.GetProperty("revision").GetInt64()); Assert.Equal(System.Text.Json.JsonValueKind.Null, initial.GetProperty("id").ValueKind);
        Assert.True(initial.GetProperty("design").GetProperty("includePhotos").GetBoolean());
        await FormPdfContractCapture.WriteAsync(defaults, "default-es", Ct);
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPdfDesign>().CountAsync(design => design.DefinitionId == seeded.Id, Ct)));
        var design = OriginalPdfDesign([seeded.Fields[1].FieldId]);
        using var saved = await manager.PostAsJsonAsync(route, new { expectedRevision = 0, design }, Ct); Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        Assert.Equal(1, (await saved.ReadJsonAsync()).GetProperty("revision").GetInt64()); await FormPdfContractCapture.WriteAsync(saved, "saved-es", Ct);
        using var stale = await manager.PostAsJsonAsync(route, new { expectedRevision = 0, design }, Ct); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(FormPdfErrors.RevisionConflict.Code, (await stale.ReadJsonAsync()).GetProperty("code").GetString()); await FormPdfContractCapture.WriteAsync(stale, "revision-conflict-es", Ct);
        foreach (var culture in new[] { "es", "en" })
        {
            manager.DefaultRequestHeaders.AcceptLanguage.Clear(); manager.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
            using var current = await manager.GetAsync(route, Ct); Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            Assert.Single((await current.ReadJsonAsync()).GetProperty("design").GetProperty("name").EnumerateObject());
            Assert.Equal("Constancia original", (await current.ReadJsonAsync()).GetProperty("design").GetProperty("name").GetProperty("es").GetString());
            await FormPdfContractCapture.WriteAsync(current, "design-" + culture, Ct);
        }
        Assert.Equal(before, await PdfDefinitionBaselineAsync(seeded.Id));
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPdfDesign>().CountAsync(design => design.DefinitionId == seeded.Id, Ct)));
    }
    [Fact]
    public async Task Pdf_preview_falls_back_to_the_original_language_and_creates_no_design_documents_or_runs()
    {
        using var manager = await OperatorAsync([BusinessFormsPermissions.RulesManage]); var seeded = await SeedSheetAsync(manager, 0);
        var route = "/api/forms/definitions/" + seeded.Id + "/pdf-design/preview"; var before = await PdfDefinitionBaselineAsync(seeded.Id);
        foreach (var culture in new[] { "es", "en" })
        {
            using var response = await manager.PostAsJsonAsync(route, new { design = OriginalPdfDesign([seeded.Fields[1].FieldId]), culture }, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal(new[] { "es" }, response.Content.Headers.ContentLanguage); Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            var bytes = await response.Content.ReadAsByteArrayAsync(Ct); Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
            await FormPdfContractCapture.WriteAsync(response, culture == "es" ? "preview-es" : "preview-en-fallback", Ct, "pdf");
        }
        Assert.Equal(before, await PdfDefinitionBaselineAsync(seeded.Id));
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(0, await db.Set<BusinessFormPdfDesign>().CountAsync(design => design.DefinitionId == seeded.Id, Ct));
            Assert.Equal(0, await db.Set<BusinessFormDocument>().CountAsync(document => document.DefinitionId == seeded.Id, Ct));
            Assert.Equal(0, await db.Set<BusinessFormActionRun>().CountAsync(run => run.DefinitionId == seeded.Id, Ct));
        });
    }
    [Fact]
    public async Task Approval_issues_a_fixed_pdf_from_the_saved_run_and_record_read_downloads_only_its_own_copy()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsCreate]);
        using var manager = await OperatorAsync([BusinessFormsPermissions.RulesManage]); using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        var sheet = await SeedSheetAsync(author, 0); var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var deciderId = Guid.Parse(decider.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var roleId = await factory.ExecuteDbContextAsync(db => db.UserRoles.Where(link => link.UserId == deciderId).Select(link => link.RoleId).SingleAsync(Ct));
        var versionId = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(definition => definition.Id == sheet.Id).Select(definition => definition.ActiveVersionId!.Value).SingleAsync(Ct));
        await using var emulator = new AzuriteFixture(); await emulator.StartAsync(Ct);
        var store = new AzureBlobFileStore(Options.Create(new FileStorageOptions { Enabled = true, CredentialMode = "ConnectionString", ConnectionString = emulator.ConnectionString }));
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IPrivateFileStore>(); services.AddSingleton<IPrivateFileStore>(store); }));
        HttpClient InHost(HttpClient source)
        {
            var client = host.CreateClient(); foreach (var header in source.DefaultRequestHeaders) client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value); return client;
        }
        using var designer = InHost(manager); using var approver = InHost(decider); using var downloader = InHost(author);
        var designRoute = "/api/forms/definitions/" + sheet.Id + "/pdf-design"; var original = OriginalPdfDesign([sheet.Fields[1].FieldId]);
        using var designSaved = await designer.PostAsJsonAsync(designRoute, new { expectedRevision = 0, design = original }, Ct); Assert.Equal(HttpStatusCode.Created, designSaved.StatusCode);
        var requestApproval = new FormRuleAction(Guid.NewGuid(), "RequestApproval", roleId, null, false);
        var generate = new FormRuleAction(Guid.NewGuid(), "GeneratePdf", null, null, false);
        var rules = new[] { new FormRule(Guid.NewGuid(), "Received", [], [requestApproval]), new FormRule(Guid.NewGuid(), "Approved", [], [generate]) };
        using var rulesSaved = await designer.PostAsJsonAsync("/api/forms/definitions/" + sheet.Id + "/rules", new { expectedRevision = 0, rules }, Ct); Assert.Equal(HttpStatusCode.Created, rulesSaved.StatusCode);
        var captured = await InActionScopeAsync(host.Services, services => services.GetRequiredService<IBusinessFormRecordCaptureService>().CaptureAsync(
            new StructuredRecordCaptureRequest(Guid.NewGuid(), sheet.Id, versionId, authorId, factory.Clock.GetUtcNow().UtcDateTime,
                new Dictionary<string,string> { [sheet.Fields[1].Key] = "Visita original" }), Ct));
        Assert.Equal("Completed", captured.State); var recordId = captured.RecordId!.Value;
        var receivedRun = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>().Where(run => run.RecordId == recordId && run.Kind == "RequestApproval").Select(run => run.Id).SingleAsync(Ct));
        var requested = await InActionScopeAsync(host.Services, services => services.GetRequiredService<IFormActionDispatchService>().ReserveAsync(receivedRun, Ct));
        Assert.True(requested.IsSuccess); Assert.Null(requested.Value);
        var task = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormTask>().Where(task => task.RecordId == recordId).Select(task => new { task.Id, task.Revision }).SingleAsync(Ct));
        using var approved = await approver.PostAsJsonAsync("/api/forms/approvals/" + task.Id + "/decisions", new { expectedRevision = task.Revision, decision = "Approved" }, Ct); Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var changed = original with { Name = new Dictionary<string,string> { ["es"] = "Constancia nueva" }, IncludePhotos = false };
        using var designChanged = await designer.PostAsJsonAsync(designRoute, new { expectedRevision = 1, design = changed }, Ct); Assert.Equal(HttpStatusCode.Created, designChanged.StatusCode);
        var pdfRun = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>().Where(run => run.RecordId == recordId && run.Kind == "GeneratePdf").Select(run => new { run.Id, run.SnapshotJson }).SingleAsync(Ct));
        var fixedSnapshot = FormDraftPolicy.Read<FormActionSnapshot>(pdfRun.SnapshotJson); Assert.Equal(1, fixedSnapshot.Pdf!.DesignRevision); Assert.Equal("Constancia original", fixedSnapshot.Pdf.Design.Name["es"]);
        var reservation = await InActionScopeAsync(host.Services, services => services.GetRequiredService<IFormActionDispatchService>().ReserveAsync(pdfRun.Id, Ct));
        Assert.True(reservation.IsSuccess); Assert.NotNull(reservation.Value); var work = reservation.Value;
        var outcome = await InActionScopeAsync(host.Services, services => services.GetRequiredService<IFormActionEffectService>().ExecuteAsync(work, Ct)); Assert.Equal(FormActionRunState.Completed, outcome.State);
        Assert.True((await InActionScopeAsync(host.Services, services => services.GetRequiredService<IFormActionDispatchService>().CompleteAsync(work, outcome, Ct))).IsSuccess);
        using var workflow = await downloader.GetAsync("/api/forms/records/" + recordId + "/workflow", Ct); Assert.Equal(HttpStatusCode.OK, workflow.StatusCode);
        var document = Assert.Single((await workflow.ReadJsonAsync()).GetProperty("documents").EnumerateArray()); var documentId = document.GetProperty("id").GetGuid();
        Assert.Equal("es", document.GetProperty("culture").GetString()); Assert.Contains("Constancia original", document.GetProperty("name").GetString()!, StringComparison.Ordinal);
        await FormPdfContractCapture.WriteAsync(workflow, "issued-es", Ct); await FormWorkflowContractCapture.WriteAsync(workflow, "pdf-completed-es", Ct);
        downloader.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        var contentRoute = "/api/forms/records/" + recordId + "/documents/" + documentId + "/content?download=true";
        using var downloaded = await downloader.GetAsync(contentRoute, Ct); Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode); Assert.Equal("application/pdf", downloaded.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new[] { "es" }, downloaded.Content.Headers.ContentLanguage); Assert.Equal("attachment", downloaded.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("no-store", downloaded.Headers.CacheControl!.ToString()); Assert.Equal("nosniff", Assert.Single(downloaded.Headers.GetValues("X-Content-Type-Options")));
        var bytes = await downloaded.Content.ReadAsByteArrayAsync(Ct); Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        await FormPdfContractCapture.WriteAsync(downloaded, "document-es", Ct, "pdf");
        using var newer = await designer.PostAsJsonAsync(designRoute, new { expectedRevision = 2, design = changed with { Footer = new Dictionary<string,string> { ["es"] = "Otro pie" } } }, Ct); Assert.Equal(HttpStatusCode.Created, newer.StatusCode);
        using var same = await downloader.GetAsync(contentRoute, Ct); Assert.Equal(bytes, await same.Content.ReadAsByteArrayAsync(Ct));
        using var mismatch = await downloader.GetAsync("/api/forms/records/" + Guid.NewGuid() + "/documents/" + documentId + "/content", Ct); Assert.Equal(HttpStatusCode.NotFound, mismatch.StatusCode);
        await FormPdfContractCapture.WriteAsync(mismatch, "document-mismatch-en", Ct);
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDocument>().CountAsync(value => value.RunId == pdfRun.Id, Ct)));
    }
    private static FormPdfDesign OriginalPdfDesign(Guid[] questions) => new(new Dictionary<string,string> { ["es"] = "Constancia original" }, questions, true, new Dictionary<string,string> { ["es"] = "Pie original" }, true);
    private Task<(long Revision, Guid? ActiveVersionId, int Versions)> PdfDefinitionBaselineAsync(Guid id) => factory.ExecuteDbContextAsync(async db =>
    {
        var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == id, Ct);
        return (definition.Revision, definition.ActiveVersionId, await db.Set<BusinessFormVersion>().CountAsync(version => version.DefinitionId == id, Ct));
    });
}
