using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// DOC1 with PostgreSQL: «Comprobante de transferencia» is in the library, with what it reads, its validations and the standards still
// to come, only with private storage (its receipt is a document question); its copy starts reading documents, brings «Bancos y
// billeteras» from the BCRA table, compiles for WhatsApp and keeps the reading and the fixed validations through saving, checking and
// publishing, which seals them in the version (turned off, the next version reads nothing). An own form looks up a catalog by a field
// until the field is inactive or cannot be compared; only in the app documents are not read, so neither is checked nor sealed. A
// capture or a correction with an account or a CUIT whose digits do not add up is rejected at its question, and «Cómo viene leyendo»
// answers its numbers (examples only with «Ver respuestas»). The intact bodies go to forms-http/document-reading.
public sealed partial class FormDraftEndpointsTests
{
    private const string TransferReceipt = "forms.transfer-receipt";

    [Fact]
    public async Task The_transfer_receipt_copy_reads_documents_and_keeps_its_reading_and_validations_until_the_version_seals_them()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.Read]);
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        author.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        // The first host keeps the app's WhatsApp channel, which compiles the Flow but cannot accept without Meta; the second accepts
        // with the ready test channel. Both have private storage.
        await using var host = StorageHost(ready: false);
        await using var ready = StorageHost(ready: true);
        using var browser = InHost(host, reader);
        using var designer = InHost(host, author);
        using var publisher = InHost(ready, author);

        using var library = await browser.GetAsync("/api/forms/library", Ct);
        var listed = await CaptureContractAsync(library, HttpStatusCode.OK, "document-reading", "library-es");
        var receipt = listed.GetProperty("standards").EnumerateArray().Single(item => item.GetProperty("key").GetString() == TransferReceipt);
        Assert.Equal(("Comprobante de transferencia", "REC"), (receipt.GetProperty("name").GetString(), receipt.GetProperty("prefix").GetString()));
        var reads = receipt.GetProperty("reads");
        Assert.Equal(14, reads.GetArrayLength());
        Assert.Equal(("sourceBank", "Banco de origen"), (reads[0].GetProperty("key").GetString(), reads[0].GetProperty("name").GetString()));
        Assert.Equal(["bankAccount", "taxId", "bankAccount", "taxId"], receipt.GetProperty("validations").EnumerateArray().Select(value => value.GetProperty("kind").GetString()));
        Assert.Equal("Cuenta de origen", receipt.GetProperty("validations")[0].GetProperty("name").GetString());
        // A standard that does not read documents keeps its bytes, and the next ones are listed without a draft.
        Assert.False(listed.GetProperty("standards").EnumerateArray().Single(item => item.GetProperty("key").GetString() == TestFormStandardSource.Key).TryGetProperty("reads", out _));
        var upcoming = listed.GetProperty("upcoming");
        Assert.Equal(11, upcoming.GetArrayLength());
        Assert.Equal(("forms.credit-note", "Nota de crédito o débito"), (upcoming[0].GetProperty("key").GetString(), upcoming[0].GetProperty("name").GetString()));

        var prefix = "R" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        using var added = await designer.PostAsJsonAsync($"/api/forms/library/{TransferReceipt}/copies",
            new { requestId = Guid.NewGuid(), name = new { es = "Pagos " + prefix }, prefix }, Ct);
        var created = await CaptureContractAsync(added, HttpStatusCode.Created, "document-reading", "copy-transfer-receipt");
        var definition = created.GetProperty("definition");
        var id = definition.GetProperty("id").GetGuid();
        var reading = definition.GetProperty("draft").GetProperty("documentReading");
        Assert.True(reading.GetProperty("enabled").GetBoolean());
        Assert.StartsWith("Comprobante de una transferencia", reading.GetProperty("recognition").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, reading.GetProperty("lookups").GetArrayLength());
        var fields = definition.GetProperty("draft").GetProperty("fields");
        Assert.Equal(15, fields.GetArrayLength());
        // What the library says it reads goes by the keys of the copy's bound map, so the editor pairs each datum with its question.
        var bound = definition.GetProperty("standard").GetProperty("bound");
        Assert.Equal(15, bound.EnumerateObject().Count());
        Assert.Equal(fields[0].GetProperty("fieldId").GetGuid(), bound.GetProperty(reads[0].GetProperty("key").GetString()!).GetGuid());
        Assert.All(reads.EnumerateArray(), read => Assert.True(bound.TryGetProperty(read.GetProperty("key").GetString()!, out _)));
        Assert.Equal([(1, "bankAccount"), (3, "taxId"), (5, "bankAccount"), (7, "taxId")], fields.EnumerateArray().Select((field, index) => (field, index))
            .Where(pair => pair.field.TryGetProperty("validation", out _)).Select(pair => (pair.index, pair.field.GetProperty("validation").GetString()!)));
        // Both banks are questions of «Bancos y billeteras», with the entity code as the code and the BCRA denomination as the detail.
        var catalogId = fields[0].GetProperty("source").GetProperty("catalog").GetGuid();
        Assert.Equal(catalogId, fields[4].GetProperty("source").GetProperty("catalog").GetGuid());
        var banks = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().Where(item => item.CatalogId == catalogId)
            .Select(item => new { item.Code, item.TextEs, item.DetailEs }).ToListAsync(Ct));
        Assert.Equal(59, banks.Count);
        Assert.Contains(banks, bank => bank.Code == "285" && bank.TextEs == "Banco Macro" && bank.DetailEs == "BANCO MACRO S.A.");
        Assert.Equal("forms.bank-entities", await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalog>().Where(catalog => catalog.Id == catalogId)
            .Select(catalog => catalog.Key).SingleAsync(Ct)));

        // The editor sends the reading and each validation back as they came; a bound question that loses its validation is refused.
        var save = DraftPayload(created);
        var withoutValidation = save.DeepClone().AsObject();
        withoutValidation["requestId"] = Guid.NewGuid().ToString();
        withoutValidation["draft"]!["fields"]![1]!.AsObject().Remove("validation");
        using var refused = await designer.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", withoutValidation, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var problem = await refused.ReadJsonAsync();
        Assert.Equal("Forms.Draft.BoundQuestionChanged", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("draft.fields[1]", out _));
        using var saved = await designer.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", save, Ct);
        var current = await CaptureContractAsync(saved, HttpStatusCode.OK, "document-reading", "draft-saved");
        var savedReading = current.GetProperty("definition").GetProperty("draft").GetProperty("documentReading");
        Assert.Equal((true, reading.GetProperty("recognition").GetString(), 0), (savedReading.GetProperty("enabled").GetBoolean(),
            savedReading.GetProperty("recognition").GetString(), savedReading.GetProperty("lookups").GetArrayLength()));
        Assert.Equal("bankAccount", current.GetProperty("definition").GetProperty("draft").GetProperty("fields")[1].GetProperty("validation").GetString());

        // Completed also by WhatsApp: the app's channel compiles the copy's Flow without an issue (accepting needs Meta).
        var whatsAppCheck = CheckPayload(current); whatsAppCheck.Remove("standard");
        using var compiled = await designer.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", whatsAppCheck, Ct);
        Assert.Equal(HttpStatusCode.OK, compiled.StatusCode);
        var compilation = await compiled.ReadJsonAsync();
        Assert.Empty(compilation.GetProperty("issues").EnumerateArray());
        Assert.Equal(("LocallyValidated", "Unavailable"), (compilation.GetProperty("compatibility").GetString(),
            compilation.GetProperty("acceptanceAvailability").GetString()));
        Assert.NotEmpty(compilation.GetProperty("channels").EnumerateArray());

        var versionId = await PublishWithChannelAsync(publisher, ready, id, current);
        using var detail = await designer.GetAsync($"/api/forms/definitions/{id}", Ct);
        var loaded = await CaptureContractAsync(detail, HttpStatusCode.OK, "document-reading", "detail-es");
        Assert.True(loaded.GetProperty("publication").GetProperty("activeVersion").GetProperty("documentReading").GetBoolean());
        var version = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().AsNoTracking().SingleAsync(value => value.Id == versionId, Ct));
        Assert.False(version.AppOnly);
        using (var sealedReading = JsonDocument.Parse(version.DocumentReadingJson!))
            Assert.Equal(reading.GetProperty("recognition").GetString(), sealedReading.RootElement.GetProperty("recognition").GetString());
        using (var schema = JsonDocument.Parse(version.SchemaJson))
            Assert.Equal(["bankAccount", "taxId", "bankAccount", "taxId"], schema.RootElement.GetProperty("questions").EnumerateArray()
                .Where(question => question.TryGetProperty("validation", out _)).Select(question => question.GetProperty("validation").GetString()));

        // Turned off, the draft keeps what was written and the next version does not read documents.
        var off = DraftPayload(loaded);
        off["draft"]!["documentReading"]!["enabled"] = false;
        using var turnedOff = await designer.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", off, Ct);
        Assert.Equal(HttpStatusCode.OK, turnedOff.StatusCode);
        var withoutReading = await turnedOff.ReadJsonAsync();
        var kept = withoutReading.GetProperty("definition").GetProperty("draft").GetProperty("documentReading");
        Assert.False(kept.GetProperty("enabled").GetBoolean());
        Assert.Equal(reading.GetProperty("recognition").GetString(), kept.GetProperty("recognition").GetString());
        var nextVersionId = await PublishWithChannelAsync(publisher, ready, id, withoutReading);
        Assert.NotEqual(versionId, nextVersionId);
        Assert.Null(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().Where(value => value.Id == nextVersionId)
            .Select(value => value.DocumentReadingJson).SingleAsync(Ct)));
        using var nextDetail = await designer.GetAsync($"/api/forms/definitions/{id}", Ct);
        var active = (await nextDetail.ReadJsonAsync()).GetProperty("publication").GetProperty("activeVersion");
        Assert.Equal(nextVersionId, active.GetProperty("versionId").GetGuid());
        Assert.False(active.TryGetProperty("documentReading", out _));
    }

    [Fact]
    public async Task Without_private_storage_the_library_neither_offers_nor_adds_the_transfer_receipt()
    {
        using var designer = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);

        using var library = await designer.GetAsync("/api/forms/library", Ct);
        Assert.Equal(HttpStatusCode.OK, library.StatusCode);
        var listed = await library.ReadJsonAsync();
        Assert.DoesNotContain(listed.GetProperty("standards").EnumerateArray(), item => item.GetProperty("key").GetString() == TransferReceipt);
        Assert.Contains(listed.GetProperty("standards").EnumerateArray(), item => item.GetProperty("key").GetString() == TestFormStandardSource.Key);
        Assert.Equal(11, listed.GetProperty("upcoming").GetArrayLength());
        var prefix = "R" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        using var added = await designer.PostAsJsonAsync($"/api/forms/library/{TransferReceipt}/copies",
            new { requestId = Guid.NewGuid(), name = new { es = "Pagos " + prefix }, prefix }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, added.StatusCode);
        Assert.Equal(FormLibraryErrors.StandardNotFound.Code, (await added.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_own_form_looks_up_a_catalog_by_a_field_until_the_field_is_inactive_or_cannot_be_compared_except_only_in_the_app()
    {
        using var operatorClient = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish,
            BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage]);
        operatorClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        // Completed also by a channel (the ready test channel), where documents are read.
        await using var ready = StorageHost(ready: true);
        using var author = InHost(ready, operatorClient);
        var names = new { es = "Clientes " + Guid.NewGuid().ToString("N")[..8], en = "Customers" };
        using var createdCatalog = await author.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = names, description = names,
            fields = new[] { CatalogField(null, "tax_id", "text", "CUIT", true, 0), CatalogField(null, "limit", "decimal", "Límite", true, 1) },
            items = new[] { CatalogItem(null) },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, createdCatalog.StatusCode);
        var catalog = await createdCatalog.ReadJsonAsync();
        var catalogId = catalog.GetProperty("catalog").GetProperty("id").GetGuid();
        var (taxIdField, limitField) = (catalog.GetProperty("fieldIdentityMap").GetProperty("tax_id").GetGuid(),
            catalog.GetProperty("fieldIdentityMap").GetProperty("limit").GetGuid());
        var itemId = catalog.GetProperty("itemIdentityMap").GetProperty("acme").GetGuid();

        // The questions are new, so the lookup names them by their localId, as a filter does.
        var (customer, customerTaxId) = (Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var payload = Payload("Facturas " + Guid.NewGuid().ToString("N")[..8]);
        payload["draft"]!["fields"] = new JsonArray(
            new JsonObject
            {
                ["localId"] = customer, ["type"] = "choice", ["required"] = false, ["label"] = new JsonObject { ["es"] = "Cliente", ["en"] = "Customer" },
                ["source"] = new JsonObject { ["catalog"] = catalogId },
            },
            new JsonObject
            {
                ["localId"] = customerTaxId, ["type"] = "shortText", ["required"] = false, ["maxLength"] = 13,
                ["label"] = new JsonObject { ["es"] = "CUIT del cliente", ["en"] = "Customer CUIT" },
            });
        payload["draft"]!["documentReading"] = new JsonObject
        {
            ["enabled"] = true, ["recognition"] = "Una factura de un cliente.",
            ["lookups"] = new JsonArray(new JsonObject { ["questionId"] = customer, ["sourceQuestionId"] = customerTaxId, ["field"] = taxIdField.ToString("D") }),
        };
        var saved = await CreateAsync(author, payload);
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var fields = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields");
        var (question, source) = (fields[0].GetProperty("fieldId").GetGuid(), fields[1].GetProperty("fieldId").GetGuid());
        var lookup = Assert.Single(saved.GetProperty("definition").GetProperty("draft").GetProperty("documentReading").GetProperty("lookups").EnumerateArray());
        Assert.Equal((question, source, taxIdField.ToString("D")),
            (lookup.GetProperty("questionId").GetGuid(), lookup.GetProperty("sourceQuestionId").GetGuid(), lookup.GetProperty("field").GetString()));

        var versionId = await PublishWithChannelAsync(author, ready, id, saved);
        using (var sealedReading = JsonDocument.Parse((await ReadingOfAsync(versionId))!))
        {
            Assert.Equal("Una factura de un cliente.", sealedReading.RootElement.GetProperty("recognition").GetString());
            var sealedLookup = Assert.Single(sealedReading.RootElement.GetProperty("lookups").EnumerateArray());
            Assert.Equal((question, source, taxIdField.ToString("D")), (sealedLookup.GetProperty("questionId").GetGuid(),
                sealedLookup.GetProperty("sourceQuestionId").GetGuid(), sealedLookup.GetProperty("field").GetString()));
        }

        // The field turned inactive in the catalog stops the next publication at the lookup.
        using var deactivated = await author.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", new
        {
            expectedRevision = 1L, name = names, description = names,
            fields = new[] { CatalogField(taxIdField, "tax_id", "text", "CUIT", false, 0), CatalogField(limitField, "limit", "decimal", "Límite", true, 1) },
            items = new[] { CatalogItem(itemId) },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        using var detail = await author.GetAsync($"/api/forms/definitions/{id}", Ct);
        await AssertLookupRefusedAsync(await detail.ReadJsonAsync(), null);

        // A short text compared with a decimal field never matches, so it is refused too.
        var edit = EditPayload(await (await author.GetAsync($"/api/forms/definitions/{id}", Ct)).ReadJsonAsync());
        edit["draft"]!["documentReading"]!["lookups"]![0]!["field"] = limitField.ToString("D");
        using var edited = await author.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        await AssertLookupRefusedAsync(await edited.ReadJsonAsync(), "check-lookup-invalid-es");

        // Only in the app documents are not read (the editor hides the reading): the same lookup does not stop the publication, the
        // version reads nothing and the draft keeps what was saved.
        var appOnly = EditPayload(await (await author.GetAsync($"/api/forms/definitions/{id}", Ct)).ReadJsonAsync());
        appOnly["draft"]!["appOnly"] = true;
        using var movedToApp = await author.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", appOnly, Ct);
        Assert.Equal(HttpStatusCode.OK, movedToApp.StatusCode);
        var check = CheckPayload(await movedToApp.ReadJsonAsync());
        using var checkedResponse = await author.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", check, Ct);
        Assert.Equal(HttpStatusCode.OK, checkedResponse.StatusCode);
        var checkedResult = await checkedResponse.ReadJsonAsync();
        Assert.True(checkedResult.GetProperty("canAccept").GetBoolean(), checkedResult.GetRawText());
        Assert.Empty(checkedResult.GetProperty("issues").EnumerateArray());
        var attempt = check.DeepClone().AsObject();
        attempt["requestId"] = Guid.NewGuid().ToString();
        attempt["checkToken"] = checkedResult.GetProperty("checkToken").GetString();
        using var accepted = await author.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        Assert.Equal("Published", receipt.GetProperty("state").GetString());
        var appVersionId = receipt.GetProperty("versionId").GetGuid();
        Assert.NotEqual(versionId, appVersionId);
        Assert.Null(await ReadingOfAsync(appVersionId));
        using var appDetail = await author.GetAsync($"/api/forms/definitions/{id}", Ct);
        var appLoaded = await appDetail.ReadJsonAsync();
        Assert.False(appLoaded.GetProperty("publication").GetProperty("activeVersion").TryGetProperty("documentReading", out _));
        var kept = appLoaded.GetProperty("definition").GetProperty("draft").GetProperty("documentReading");
        Assert.Equal((true, limitField.ToString("D")), (kept.GetProperty("enabled").GetBoolean(), kept.GetProperty("lookups")[0].GetProperty("field").GetString()));

        Task<string?> ReadingOfAsync(Guid version) => factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().Where(value => value.Id == version)
            .Select(value => value.DocumentReadingJson).SingleAsync(Ct));

        async Task AssertLookupRefusedAsync(JsonElement current, string? capture)
        {
            using var response = await author.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(current), Ct);
            var body = capture is null ? await response.ReadJsonAsync() : await CaptureContractAsync(response, HttpStatusCode.OK, "document-reading", capture);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(body.GetProperty("canAccept").GetBoolean());
            var issue = Assert.Single(body.GetProperty("issues").EnumerateArray());
            Assert.Equal((FormPublicationErrors.DocumentLookupInvalid.Code, "draft.documentReading.lookups[0].field", question),
                (issue.GetProperty("code").GetString(), issue.GetProperty("path").GetString(), issue.GetProperty("fieldId").GetGuid()));
        }
    }

    [Fact]
    public async Task How_the_reading_did_needs_read_and_its_examples_also_records_read()
    {
        using var designer = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        using var examiner = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.RecordsRead]);
        using var stranger = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        await using var host = StorageHost(ready: false);
        using var copier = InHost(host, designer);
        var prefix = "R" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        using var added = await copier.PostAsJsonAsync($"/api/forms/library/{TransferReceipt}/copies",
            new { requestId = Guid.NewGuid(), name = new { es = "Cobros " + prefix }, prefix }, Ct);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var id = (await added.ReadJsonAsync()).GetProperty("definition").GetProperty("id").GetGuid();

        // Until a response says which document it came from (task 9), there is nothing to count, but the route answers, and the
        // duplicates come from the standard: the operation number, the amount and the date.
        var fields = (await added.ReadJsonAsync()).GetProperty("definition").GetProperty("draft").GetProperty("fields");
        using var numbers = await designer.GetAsync($"/api/forms/definitions/{id}/document-reading", Ct);
        var body = await CaptureContractAsync(numbers, HttpStatusCode.OK, "document-reading", "stats-es");
        Assert.Equal((0, 0), (body.GetProperty("documents").GetInt32(), body.GetProperty("questions").GetArrayLength()));
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime.AddDays(-30), body.GetProperty("sinceUtc").GetDateTime(), TimeSpan.FromMinutes(5));
        Assert.False(body.TryGetProperty("since", out _));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("mostCorrected").ValueKind);
        Assert.Equal([fields[12].GetProperty("fieldId").GetGuid(), fields[8].GetProperty("fieldId").GetGuid(), fields[10].GetProperty("fieldId").GetGuid()],
            body.GetProperty("duplicateQuestionIds").EnumerateArray().Select(value => value.GetGuid()));
        Assert.False(body.TryGetProperty("examples", out _));
        Assert.True(numbers.Headers.CacheControl?.NoStore);

        using var examples = await examiner.GetAsync($"/api/forms/definitions/{id}/document-reading", Ct);
        Assert.Equal(HttpStatusCode.OK, examples.StatusCode);
        Assert.Equal(0, (await examples.ReadJsonAsync()).GetProperty("examples").GetArrayLength());

        using var forbidden = await stranger.GetAsync($"/api/forms/definitions/{id}/document-reading", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var missing = await designer.GetAsync($"/api/forms/definitions/{Guid.NewGuid()}/document-reading", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task A_capture_whose_account_or_cuit_does_not_add_up_is_rejected_at_its_question_and_a_valid_one_is_saved()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish,
            BusinessFormsPermissions.RecordsCreate]);
        author.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        // Published with the ready test channel, as a form completed also by WhatsApp, so its responses are read and corrected like any.
        await using var ready = StorageHost(ready: true);
        using var designer = InHost(ready, author);
        var prefix = "P" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        using var added = await designer.PostAsJsonAsync($"/api/forms/library/{TestFormStandardSource.AccountKey}/copies",
            new { requestId = Guid.NewGuid(), name = new { es = "Pagos " + prefix }, prefix }, Ct);
        var created = await added.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var id = created.GetProperty("definition").GetProperty("id").GetGuid();
        var fields = created.GetProperty("definition").GetProperty("draft").GetProperty("fields");
        var (account, taxId) = (fields[0].GetProperty("fieldId").GetGuid(), fields[1].GetProperty("fieldId").GetGuid());
        var versionId = await PublishWithChannelAsync(designer, ready, id, created);

        async Task<HttpResponseMessage> SendAsync(string accountValue, string taxIdValue) => await designer.PostAsync($"/api/forms/entries/{id}", Entry(versionId,
            new { fieldId = account, value = accountValue }, new { fieldId = taxId, value = taxIdValue }), Ct);

        using var badAccount = await SendAsync("2850590940090418135202", "20-12345678-6");
        var accountProblem = await CaptureContractAsync(badAccount, HttpStatusCode.BadRequest, "document-reading", "entry-bank-account-invalid-es");
        Assert.Equal(FormRecordErrors.BankAccountInvalid.Code, accountProblem.GetProperty("code").GetString());
        Assert.Equal("Revisá la cuenta: los números no dan. Copiala del comprobante.",
            accountProblem.GetProperty("errors").GetProperty("answers." + account.ToString("D"))[0].GetString());
        using var badTaxId = await SendAsync("2850590940090418135201", "20-12345678-7");
        var taxProblem = await CaptureContractAsync(badTaxId, HttpStatusCode.BadRequest, "document-reading", "entry-tax-id-invalid-es");
        Assert.Equal(FormRecordErrors.TaxIdInvalid.Code, taxProblem.GetProperty("code").GetString());
        Assert.Equal("Revisá la CUIT o CUIL: los números no dan. Copiala del comprobante.",
            taxProblem.GetProperty("errors").GetProperty("answers." + taxId.ToString("D"))[0].GetString());
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().CountAsync(record => record.DefinitionId == id, Ct)));
        // The rejection keeps only the receipt of the attempt, with its code and question.
        Assert.Equal(2, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCaptureReceipt>().CountAsync(receipt => receipt.DefinitionId == id
            && (receipt.ErrorCode == FormRecordErrors.BankAccountInvalid.Code && receipt.RejectedFieldId == account
                || receipt.ErrorCode == FormRecordErrors.TaxIdInvalid.Code && receipt.RejectedFieldId == taxId), Ct)));

        using var valid = await SendAsync("0170 0992 2000 0067 7973 70", "27400001230");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        var recordId = (await valid.ReadJsonAsync()).GetProperty("recordId").GetGuid();
        Assert.Equal("0170 0992 2000 0067 7973 70", await AccountValueAsync());

        // A correction goes through the same validations: a CBU with its last digit wrong is refused and the response stays as it was.
        var correction = Entry(versionId, new { fieldId = account, value = "0170099220000067797371" }, new { fieldId = taxId, value = "27400001230" });
        correction.Add(new StringContent("La cuenta estaba mal copiada."), "reason");
        using var corrected = await designer.PostAsync($"/api/forms/entries/{id}/records/{recordId}", correction, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, corrected.StatusCode);
        Assert.Equal(FormRecordErrors.BankAccountInvalid.Code, (await corrected.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().CountAsync(record => record.DefinitionId == id, Ct)));
        Assert.Equal("0170 0992 2000 0067 7973 70", await AccountValueAsync());

        Task<string?> AccountValueAsync() => factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordValue>()
            .Where(value => value.DefinitionId == id && value.FieldId == account).Select(value => value.TextValue).SingleAsync(Ct));
    }

    // Private storage (the receipt is a document question) and, when ready, the test channel that accepts without transport instead of
    // the app's WhatsApp channel, which compiles the Flow but cannot accept without Meta.
    private WebApplicationFactory<Program> StorageHost(bool ready) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IPrivateFileStore>();
        services.AddSingleton<IPrivateFileStore>(new AssistantCopyFiles());
        if (!ready) return;
        services.RemoveAll<IStructuredFormChannel>();
        services.AddSingleton<IStructuredFormChannel, ReadyStructuredFormChannel>();
    }));

    // Checks and accepts a form as it was saved (without a copy's standard, which never travels back) through the ready channel and
    // completes the publication as its worker would; returns the version.
    private static async Task<Guid> PublishWithChannelAsync(HttpClient client, WebApplicationFactory<Program> host, Guid id, JsonElement saved)
    {
        var check = CheckPayload(saved); check.Remove("standard");
        using var checkedResponse = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", check, Ct);
        Assert.Equal(HttpStatusCode.OK, checkedResponse.StatusCode);
        var result = await checkedResponse.ReadJsonAsync();
        Assert.True(result.GetProperty("canAccept").GetBoolean(), result.GetRawText());
        var attempt = check.DeepClone().AsObject();
        attempt["requestId"] = Guid.NewGuid().ToString();
        attempt["checkToken"] = result.GetProperty("checkToken").GetString();
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        return await CompletePublicationAsync(host, (await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid(), id);
    }

    // A saved definition (a commit or a detail) as the editor sends it back: the draft as it came, with its reading and its
    // validations, without the standard, which never travels back.
    private static JsonObject DraftPayload(JsonElement saved)
    {
        var payload = EditPayload(saved);
        payload.Remove("standard");
        return payload;
    }

    private static MultipartFormDataContent Entry(Guid versionId, params object[] answers)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString("D")), "requestId" },
            { new StringContent(versionId.ToString("D")), "versionId" },
            { new StringContent(JsonSerializer.Serialize(answers, JsonSerializerOptions.Web)), "answers" },
        };
        return content;
    }

    // A field of a catalog as its editor sends it: new by its key, or by its id.
    private static object CatalogField(Guid? id, string key, string type, string name, bool active, int position) => new
    {
        localId = id is null ? key : null, id, key, name = new { es = name, en = name }, type, sourceCatalogId = (Guid?)null, required = false,
        unique = false, min = (string?)null, max = (string?)null, maxLength = type == "text" ? 300 : (int?)null, role = "data", position, isActive = active,
    };

    private static object CatalogItem(Guid? itemId) => new
    {
        localId = "acme", itemId, code = "ACME", text = new { es = "Acme", en = "Acme" }, detail = new { es = (string?)null, en = (string?)null }, isActive = true,
    };
}
