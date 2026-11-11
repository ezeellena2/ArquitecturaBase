using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// Stage 2 of the assistant authoring: importing into a catalog the spreadsheet a proposal mapped, from the private copy of its turn,
// with the review, the confirmations and the single boundary of an upload.
[Collection(ApiTestGroup.Name)]
public sealed class FormCatalogAssistantImportHttpTests(ApiFactory factory)
{
    private const string Proposals = "/api/forms/catalogs/assistant-proposals";
    private static readonly byte[] Csv = Encoding.UTF8.GetBytes(
        "CUIT,Razón social,Teléfono\n20-11111111-1,Transportes Sur,11 5555-0001\n20-22222222-2,Logística Pampa,\n");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // «Crear catálogo» leaves the import pending with the spreadsheet already there. An empty row on top is skipped, as the model saw it.
    [Fact]
    public async Task A_catalog_created_from_a_proposal_with_a_spreadsheet_opens_reviews_and_imports_it()
    {
        await using var scenario = await FormCatalogAssistantImportScenario.CreateAsync(factory, Ct);
        var workbook = FormSpreadsheetFixture.Workbook(sheet =>
        {
            sheet.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.LightGray;
            sheet.Cell(2, 1).Value = "CUIT";
            sheet.Cell(2, 2).Value = "Razón social";
            sheet.Cell(2, 3).Value = "Teléfono";
            sheet.Cell(3, 1).Value = "20-11111111-1";
            sheet.Cell(3, 2).Value = "Transportes Sur";
            sheet.Cell(3, 3).Value = "11 5555-0001";
            sheet.Cell(4, 1).Value = "20-22222222-2";
            sheet.Cell(4, 2).Value = "Logística Pampa";
        });
        var request = scenario.MappedCatalog("Fleteros " + scenario.Suffix, workbook, "transportistas.xlsx", 2);
        using var author = scenario.Client(scenario.AuthorId);
        author.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        using var created = await author.PostAsJsonAsync(Proposals, request, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalogId = (await created.ReadJsonAsync()).GetProperty("catalogId").GetGuid();
        var path = $"/api/forms/catalogs/{catalogId}";

        using var pending = await author.GetAsync(path + "/assistant-import", Ct);
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        Assert.True(pending.Headers.CacheControl!.NoStore);
        await FormCatalogImportContractCapture.WriteAsync(pending, "es", "assistant-pending", Ct);
        var import = await pending.ReadJsonAsync();
        Assert.Equal(catalogId, import.GetProperty("catalogId").GetGuid());
        Assert.Equal((request.RequestId, request.ProposalId, "transportistas.xlsx", 2), (import.GetProperty("pending").GetProperty("requestId").GetGuid(),
            import.GetProperty("pending").GetProperty("proposalId").GetGuid(), import.GetProperty("pending").GetProperty("fileName").GetString(),
            import.GetProperty("pending").GetProperty("rowCount").GetInt32()));

        var sha = FormCatalogAssistantImportScenario.Sha256(workbook);
        using var preview = await author.PostAsJsonAsync(path + "/assistant-import-previews",
            new { request.RequestId, request.ProposalId, expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.True(preview.Headers.CacheControl!.NoStore);
        await FormCatalogImportContractCapture.WriteAsync(preview, "es", "assistant-preview", Ct);
        var plan = await preview.ReadJsonAsync();
        Assert.Equal(("Replace", sha, "Options", 2, 2), (plan.GetProperty("mode").GetString(), plan.GetProperty("sheetSha256").GetString(),
            plan.GetProperty("sheetName").GetString(), plan.GetProperty("rowsRead").GetInt32(), plan.GetProperty("counts").GetProperty("added").GetInt32()));
        Assert.Empty(plan.GetProperty("issues").EnumerateArray());
        Assert.Equal(["20-11111111-1", "20-22222222-2"], plan.GetProperty("added").EnumerateArray().Select(row => row.GetProperty("code").GetString()));
        Assert.Equal(1, await RevisionAsync(catalogId));

        using var applied = await author.PostAsJsonAsync(path + "/assistant-imports",
            new { request.RequestId, request.ProposalId, expectedRevision = 1, sheetSha256 = sha }, Ct);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        await FormCatalogImportContractCapture.WriteAsync(applied, "es", "assistant-applied", Ct);
        var saved = (await applied.ReadJsonAsync()).GetProperty("catalog");
        Assert.Equal((2L, 2, false), (saved.GetProperty("revision").GetInt64(), saved.GetProperty("itemsCount").GetInt32(), saved.GetProperty("isActive").GetBoolean()));
        var stored = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalog>().AsNoTracking().SingleAsync(catalog => catalog.Id == catalogId, Ct));
        // Importing is not approving: the catalog stays inactive and pending, with the copy's SHA-256 as the last sheet.
        Assert.Equal((sha, BusinessFormCatalogProposalState.Pending, false), (stored.LastImportSha256, stored.ProposalState, stored.IsActive));
        var items = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().AsNoTracking().Where(item => item.CatalogId == catalogId)
            .OrderBy(item => item.Position).Select(item => new { item.Code, item.TextEs, item.FieldValuesJson }).ToListAsync(Ct));
        Assert.Equal([("20-11111111-1", "Transportes Sur"), ("20-22222222-2", "Logística Pampa")], items.Select(item => (item.Code, item.TextEs)));
        Assert.Contains("11 5555-0001", items[0].FieldValuesJson, StringComparison.Ordinal);

        // With options it has nothing pending: 200 without data.
        using var after = await author.GetAsync(path + "/assistant-import", Ct);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        await FormCatalogImportContractCapture.WriteAsync(after, "es", "assistant-none", Ct);
        Assert.Equal(JsonValueKind.Null, (await after.ReadJsonAsync()).GetProperty("pending").ValueKind);
    }

    // Replacing, as uploading a file does: what the spreadsheet does not bring is deactivated, with the confirmation of always.
    [Fact]
    public async Task An_import_proposal_replaces_the_options_of_a_catalog_that_exists_with_the_confirmation_of_always()
    {
        await using var scenario = await FormCatalogAssistantImportScenario.CreateAsync(factory, Ct);
        using var author = scenario.Client(scenario.AuthorId);
        var name = "Transportistas " + scenario.Suffix;
        using var created = await author.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = name },
            items = new object[]
            {
                new { localId = "known", code = "20-11111111-1", text = new { es = "Transporte viejo" }, isActive = true },
                new { localId = "gone", code = "OLD", text = new { es = "Vieja" }, isActive = true },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalogId = (await created.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();
        var path = $"/api/forms/catalogs/{catalogId}";
        var request = scenario.Import(catalogId, name, Csv, 2);
        var sha = FormCatalogAssistantImportScenario.Sha256(Csv);

        using var preview = await author.PostAsJsonAsync(path + "/assistant-import-previews", new { request.RequestId, request.ProposalId, expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var plan = await preview.ReadJsonAsync();
        var counts = plan.GetProperty("counts");
        Assert.Equal((1, 1, 1), (counts.GetProperty("added").GetInt32(), counts.GetProperty("changed").GetInt32(), counts.GetProperty("deactivated").GetInt32()));
        Assert.Equal(1, plan.GetProperty("confirmations").GetProperty("deactivations").GetInt32());
        // A CSV has no sheet: the review names it after its file, as an upload's.
        Assert.Equal(("transportistas", sha), (plan.GetProperty("sheetName").GetString(), plan.GetProperty("sheetSha256").GetString()));
        await AssertProblemAsync(author.PostAsJsonAsync(path + "/assistant-imports",
            new { request.RequestId, request.ProposalId, expectedRevision = 1, sheetSha256 = sha }, Ct), HttpStatusCode.BadRequest,
            "Forms.CatalogImport.ConfirmationRequired");

        using var applied = await author.PostAsJsonAsync(path + "/assistant-imports",
            new { request.RequestId, request.ProposalId, expectedRevision = 1, sheetSha256 = sha, confirmDeactivations = 1 }, Ct);

        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal(2L, (await applied.ReadJsonAsync()).GetProperty("catalog").GetProperty("revision").GetInt64());
        var items = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().AsNoTracking().Where(item => item.CatalogId == catalogId)
            .OrderBy(item => item.Position).Select(item => new { item.Code, item.TextEs, item.IsActive }).ToListAsync(Ct));
        Assert.Equal([("20-11111111-1", "Transportes Sur", true), ("OLD", "Vieja", false), ("20-22222222-2", "Logística Pampa", true)],
            items.Select(item => (item.Code, item.TextEs, item.IsActive)));
        // A catalog that did not come from a proposal has nothing pending.
        using var pending = await author.GetAsync(path + "/assistant-import", Ct);
        Assert.Equal(JsonValueKind.Null, (await pending.ReadJsonAsync()).GetProperty("pending").ValueKind);
    }

    [Fact]
    public async Task An_import_proposal_also_imports_into_a_proposed_catalog_that_is_still_pending()
    {
        await using var scenario = await FormCatalogAssistantImportScenario.CreateAsync(factory, Ct);
        using var author = scenario.Client(scenario.AuthorId);
        var name = "Proveedores " + scenario.Suffix;
        using var created = await author.PostAsJsonAsync(Proposals, scenario.Catalog(name), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalogId = (await created.ReadJsonAsync()).GetProperty("catalogId").GetGuid();
        var path = $"/api/forms/catalogs/{catalogId}";
        // Version 1 has no spreadsheet, so nothing is pending.
        using var none = await author.GetAsync(path + "/assistant-import", Ct);
        Assert.Equal(JsonValueKind.Null, (await none.ReadJsonAsync()).GetProperty("pending").ValueKind);
        var request = scenario.Import(catalogId, name, Csv, 2);

        using var preview = await author.PostAsJsonAsync(path + "/assistant-import-previews", new { request.RequestId, request.ProposalId, expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(2, (await preview.ReadJsonAsync()).GetProperty("counts").GetProperty("added").GetInt32());
        using var applied = await author.PostAsJsonAsync(path + "/assistant-imports",
            new { request.RequestId, request.ProposalId, expectedRevision = 1, sheetSha256 = FormCatalogAssistantImportScenario.Sha256(Csv) }, Ct);

        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal(2, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().CountAsync(item => item.CatalogId == catalogId, Ct)));
        var stored = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalog>().AsNoTracking().SingleAsync(catalog => catalog.Id == catalogId, Ct));
        Assert.Equal((BusinessFormCatalogProposalState.Pending, false), (stored.ProposalState, stored.IsActive));
    }

    [Fact]
    public async Task Someone_elses_a_replaced_or_a_proposal_whose_copy_is_gone_imports_nothing()
    {
        await using var scenario = await FormCatalogAssistantImportScenario.CreateAsync(factory, Ct);
        using var author = scenario.Client(scenario.AuthorId);
        author.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        var name = "Acopios " + scenario.Suffix;
        var request = scenario.MappedCatalog(name, Csv, "transportistas.csv", 2);
        using var created = await author.PostAsJsonAsync(Proposals, request, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalogId = (await created.ReadJsonAsync()).GetProperty("catalogId").GetGuid();
        var path = $"/api/forms/catalogs/{catalogId}";
        var sha = FormCatalogAssistantImportScenario.Sha256(Csv);

        // Another person who manages catalogs: the proposal and the pending import are the author's.
        using var manager = scenario.Client(scenario.ManagerId);
        manager.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        await AssertProblemAsync(manager.PostAsJsonAsync(path + "/assistant-import-previews", new { request.RequestId, request.ProposalId, expectedRevision = 1 }, Ct),
            HttpStatusCode.NotFound, "Forms.Catalog.ProposalUnavailable", "assistant-proposal-unavailable");
        await AssertProblemAsync(manager.PostAsJsonAsync(path + "/assistant-imports",
            new { request.RequestId, request.ProposalId, expectedRevision = 1, sheetSha256 = sha }, Ct), HttpStatusCode.NotFound, "Forms.Catalog.ProposalUnavailable");
        using var foreign = await manager.GetAsync(path + "/assistant-import", Ct);
        Assert.Equal(HttpStatusCode.OK, foreign.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await foreign.ReadJsonAsync()).GetProperty("pending").ValueKind);

        // A later proposal of the conversation replaced this import.
        var replaced = scenario.Import(catalogId, name, Csv, 2, current: false);
        await AssertProblemAsync(author.PostAsJsonAsync(path + "/assistant-import-previews", new { replaced.RequestId, replaced.ProposalId, expectedRevision = 1 }, Ct),
            HttpStatusCode.Conflict, "Forms.Catalog.ProposalReplaced");

        // The copy the assistant saw is gone: the dialog asks for the file again.
        var gone = scenario.Import(catalogId, name, Csv, 2, store: false);
        await AssertProblemAsync(author.PostAsJsonAsync(path + "/assistant-import-previews", new { gone.RequestId, gone.ProposalId, expectedRevision = 1 }, Ct),
            HttpStatusCode.Conflict, "Forms.CatalogImport.SheetUnavailable", "assistant-sheet-unavailable");
        await AssertProblemAsync(author.PostAsJsonAsync(path + "/assistant-imports",
            new { gone.RequestId, gone.ProposalId, expectedRevision = 1, sheetSha256 = sha }, Ct), HttpStatusCode.Conflict, "Forms.CatalogImport.SheetUnavailable");
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().CountAsync(item => item.CatalogId == catalogId, Ct)));
        Assert.Equal(1, await RevisionAsync(catalogId));

        // Asking for the pending import does not open the copy: the review finds it gone and the dialog asks for the file.
        var orphan = scenario.MappedCatalog("Sin copia " + scenario.Suffix, Csv, "transportistas.csv", 2, store: false);
        using var orphaned = await author.PostAsJsonAsync(Proposals, orphan, Ct);
        Assert.Equal(HttpStatusCode.Created, orphaned.StatusCode);
        var orphanPath = $"/api/forms/catalogs/{(await orphaned.ReadJsonAsync()).GetProperty("catalogId").GetGuid()}";
        var reads = scenario.Files.Reads;
        using var stillPending = await author.GetAsync(orphanPath + "/assistant-import", Ct);
        Assert.Equal(orphan.ProposalId, (await stillPending.ReadJsonAsync()).GetProperty("pending").GetProperty("proposalId").GetGuid());
        Assert.Equal(reads, scenario.Files.Reads);
        await AssertProblemAsync(author.PostAsJsonAsync(orphanPath + "/assistant-import-previews", new { orphan.RequestId, orphan.ProposalId, expectedRevision = 1 }, Ct),
            HttpStatusCode.Conflict, "Forms.CatalogImport.SheetUnavailable");
    }

    [Fact]
    public async Task The_routes_need_a_session_their_permissions_and_a_closed_body()
    {
        await using var scenario = await FormCatalogAssistantImportScenario.CreateAsync(factory, Ct);
        using var author = scenario.Client(scenario.AuthorId);
        var request = scenario.MappedCatalog("Permisos " + scenario.Suffix, Csv, "transportistas.csv", 2);
        using var created = await author.PostAsJsonAsync(Proposals, request, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var path = $"/api/forms/catalogs/{(await created.ReadJsonAsync()).GetProperty("catalogId").GetGuid()}";
        var preview = new { request.RequestId, request.ProposalId, expectedRevision = 1 };
        var apply = new { request.RequestId, request.ProposalId, expectedRevision = 1, sheetSha256 = FormCatalogAssistantImportScenario.Sha256(Csv) };
        using var anonymous = scenario.AnonymousClient();
        using var reader = scenario.Client(scenario.ReaderId);
        using var outsider = scenario.Client(scenario.OutsiderId);

        foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (reader, HttpStatusCode.Forbidden) })
        {
            using var reviewed = await client.PostAsJsonAsync(path + "/assistant-import-previews", preview, Ct);
            using var imported = await client.PostAsJsonAsync(path + "/assistant-imports", apply, Ct);
            Assert.Equal((status, status), (reviewed.StatusCode, imported.StatusCode));
        }
        using (var unauthenticated = await anonymous.GetAsync(path + "/assistant-import", Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using (var forbidden = await outsider.GetAsync(path + "/assistant-import", Ct))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        // Reading catalogs is enough to ask; the import is pending only for the person who asked for it.
        using (var other = await reader.GetAsync(path + "/assistant-import", Ct))
            Assert.Equal(JsonValueKind.Null, (await other.ReadJsonAsync()).GetProperty("pending").ValueKind);

        using var unknown = await author.PostAsJsonAsync(path + "/assistant-import-previews",
            new { request.RequestId, request.ProposalId, expectedRevision = 1, sheetSha256 = "" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        using var incomplete = await author.PostAsJsonAsync(path + "/assistant-import-previews", new { request.RequestId, expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        Assert.True((await incomplete.ReadJsonAsync()).GetProperty("errors").TryGetProperty("proposalId", out _));
        Assert.Equal(0, scenario.Files.Reads);
    }

    private Task<long> RevisionAsync(Guid catalogId) => factory.ExecuteDbContextAsync(db =>
        db.Set<BusinessFormCatalog>().Where(catalog => catalog.Id == catalogId).Select(catalog => catalog.Revision).SingleAsync(Ct));

    // A capture names the Spanish body the front reads to ask for the file again.
    private static async Task AssertProblemAsync(Task<HttpResponseMessage> sending, HttpStatusCode status, string code, string? capture = null)
    {
        using var response = await sending;
        Assert.Equal(status, response.StatusCode);
        if (capture is not null) await FormCatalogImportContractCapture.WriteAsync(response, "es", capture, Ct);
        Assert.Equal(code, (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }
}
