using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public async Task An_unpublished_secondary_button_keeps_navigation_sheet_and_detail_entirely_original(string original, string ui)
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd(ui);
        var option = new CanonicalFormChoice(Guid.NewGuid(), new Dictionary<string, string> { [original] = "Zulu", [ui] = "Alpha" });
        var choice = Field("choice", false) with { Label = new Dictionary<string, string> { [original] = "Pregunta", [ui] = "Question" }, Options = [option] };
        var seeded = await SeedSheetCoreAsync(reader, 1, null, [choice], original, secondaryButtonMissing: true);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var record = await db.Set<BusinessFormRecord>().SingleAsync(row => row.Id == seeded.RecordIds[0], Ct);
            var values = JsonSerializer.Deserialize<List<JsonElement>>(record.ValuesJson, SerializerOptions)!;
            values.Add(JsonSerializer.SerializeToElement(new { fieldId = choice.FieldId, value = option.ChoiceId }, SerializerOptions));
            db.Entry(record).Property(row => row.ValuesJson).CurrentValue = JsonSerializer.Serialize(values, SerializerOptions);
            db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(record.Id, seeded.Id, choice.FieldId, "choice", choiceId: option.ChoiceId));
            await db.SaveChangesAsync(Ct);
        });
        var name = seeded.Prefix + (original == "es" ? " Planilla" : " Sheet");
        using var navigation = await reader.GetAsync("/api/forms/navigation?search=" + Uri.EscapeDataString(name) + "&pageSize=1", Ct);
        Assert.Equal(HttpStatusCode.OK, navigation.StatusCode);
        Assert.Equal(name, Assert.Single((await navigation.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        var untranslatedName = seeded.Prefix + (original == "es" ? " Sheet" : " Planilla");
        using var unpublishedSearch = await reader.GetAsync("/api/forms/navigation?search=" + Uri.EscapeDataString(untranslatedName), Ct);
        Assert.Equal(0, (await unpublishedSearch.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
        using var sheet = await reader.GetAsync($"/api/forms/definitions/{seeded.Id}/records", Ct);
        Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
        var body = await sheet.ReadJsonAsync();
        Assert.Equal(name, body.GetProperty("name").GetString());
        Assert.Equal("Pregunta", body.GetProperty("columns")[4].GetProperty("label").GetString());
        Assert.Equal("Zulu", body.GetProperty("records").GetProperty("items")[0].GetProperty("cells")[4].GetProperty("value").GetProperty("label").GetString());
        using var detail = await reader.GetAsync("/api/forms/records/" + seeded.RecordIds[0], Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(name, (await detail.ReadJsonAsync()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Choice_sort_uses_the_record_version_and_facets_use_the_latest_published_label()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        var option = new CanonicalFormChoice(Guid.NewGuid(), new Dictionary<string, string> { ["es"] = "Zulu", ["en"] = "Ignored secondary" });
        var choice = Field("choice", false) with { Options = [option] };
        var seeded = await SeedSheetCoreAsync(reader, 1, null, [choice], "es", secondaryButtonMissing: true);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(row => row.Id == seeded.Id, Ct);
            var old = await db.Set<BusinessFormRecord>().SingleAsync(row => row.Id == seeded.RecordIds[0], Ct);
            db.Entry(old).Property(row => row.ValuesJson).CurrentValue = JsonSerializer.Serialize(new[] { new { fieldId = choice.FieldId, value = option.ChoiceId } }, SerializerOptions);
            db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(old.Id, seeded.Id, choice.FieldId, "choice", choiceId: option.ChoiceId));
            var currentChoice = choice with { Options = [option with { Label = new Dictionary<string, string> { ["es"] = "Nueva", ["en"] = "Alpha" } }] };
            var version = BusinessFormVersion.Create(definition.Id, 2, "test-version-labels");
            version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, [currentChoice]), SerializerOptions));
            version.SealPresentation(definition.NameJson, "{\"es\":\"Enviar\",\"en\":\"Submit\"}", "es", 1);
            db.Set<BusinessFormVersion>().Add(version);
            var actor = Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
            db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(factory, definition, version, actor, ["es", "en"]));
            Assert.True(definition.ActivateVersion(definition.ActiveVersionId, version.Id).IsSuccess);
            var receipt = Guid.NewGuid();
            var now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
            var record = BusinessFormRecord.Create(definition.Id, version.Id, definition.AllocateRecordNumber(), actor, receipt, now,
                JsonSerializer.Serialize(new[] { new { fieldId = choice.FieldId, value = option.ChoiceId } }, SerializerOptions));
            db.Set<BusinessFormRecord>().Add(record);
            db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(receipt, definition.Id, version.Id, "test", record.Id, record.Number, now));
            db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(record.Id, seeded.Id, choice.FieldId, "choice", choiceId: option.ChoiceId));
            await db.SaveChangesAsync(Ct);
        });
        foreach (var (sort, numbers, labels) in new[] {
            ("field", new[] { 2, 1 }, new[] { "Alpha", "Zulu" }),
            ("-field", new[] { 1, 2 }, new[] { "Zulu", "Alpha" }) })
        {
            using var sorted = await reader.PostAsJsonAsync($"/api/forms/definitions/{seeded.Id}/records/search", new { sort, sortFieldId = choice.FieldId, pageSize = 1 }, Ct);
            Assert.Equal(HttpStatusCode.OK, sorted.StatusCode);
            var body = await sorted.ReadJsonAsync();
            Assert.Equal(2, body.GetProperty("records").GetProperty("totalCount").GetInt32());
            var row = Assert.Single(body.GetProperty("records").GetProperty("items").EnumerateArray());
            Assert.Equal(seeded.Prefix + "-" + numbers[0].ToString("D6", System.Globalization.CultureInfo.InvariantCulture), row.GetProperty("number").GetString());
            Assert.Equal(labels[0], row.GetProperty("cells")[0].GetProperty("value").GetProperty("label").GetString());
            using var second = await reader.PostAsJsonAsync($"/api/forms/definitions/{seeded.Id}/records/search", new { sort, sortFieldId = choice.FieldId, pageSize = 1, page = 2 }, Ct);
            var next = Assert.Single((await second.ReadJsonAsync()).GetProperty("records").GetProperty("items").EnumerateArray());
            Assert.Equal(labels[1], next.GetProperty("cells")[0].GetProperty("value").GetProperty("label").GetString());
            var navigationFilter = new { sort, sortFieldId = choice.FieldId, pageSize = 1, page = 20 };
            var firstNavigation = await ReadRecordNavigationAsync(reader, seeded.Id, row.GetProperty("id").GetGuid(), navigationFilter);
            AssertRecordNavigation(firstNavigation, row.GetProperty("id").GetGuid(), null, next.GetProperty("id").GetGuid(), 1, 2);
            var secondNavigation = await ReadRecordNavigationAsync(reader, seeded.Id, next.GetProperty("id").GetGuid(), navigationFilter);
            AssertRecordNavigation(secondNavigation, next.GetProperty("id").GetGuid(), row.GetProperty("id").GetGuid(), null, 2, 2);
        }
        using var facets = await reader.PostAsJsonAsync($"/api/forms/definitions/{seeded.Id}/records/filter-counts/search", new { dimension = "choice", fieldId = choice.FieldId, filter = new { } }, Ct);
        Assert.Equal(HttpStatusCode.OK, facets.StatusCode);
        var facet = Assert.Single((await facets.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal("Alpha", facet.GetProperty("label").GetString());
        Assert.Equal(2, facet.GetProperty("count").GetInt32());
    }

    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public async Task Approval_names_use_published_cultures_when_only_the_secondary_button_is_missing(string original, string ui)
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        decider.DefaultRequestHeaders.AcceptLanguage.ParseAdd(ui);
        var seeded = await SeedApprovalAsync(author, decider, 12, originalCulture: original, secondaryButtonMissing: true);
        var prefix = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(row => row.Id == seeded.DefinitionId).Select(row => row.Prefix).SingleAsync(Ct));
        var expected = prefix + (original == "es" ? " Planilla" : " Sheet");
        using var inbox = await decider.GetAsync("/api/forms/approvals?definitionId=" + seeded.DefinitionId, Ct);
        Assert.Equal(HttpStatusCode.OK, inbox.StatusCode);
        Assert.Equal(expected, Assert.Single((await inbox.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("formName").GetString());
        using var detail = await decider.GetAsync("/api/forms/approvals/" + seeded.TaskId, Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(expected, (await detail.ReadJsonAsync()).GetProperty("formName").GetString());
        using var options = await decider.GetAsync("/api/forms/approvals/options", Ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        Assert.Equal(expected, Assert.Single((await options.ReadJsonAsync()).GetProperty("forms").EnumerateArray()).GetProperty("name").GetString());
    }
}
