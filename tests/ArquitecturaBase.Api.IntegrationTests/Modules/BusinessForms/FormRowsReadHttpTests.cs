using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task A_block_of_rows_reads_one_column_with_its_items_in_the_sheet_and_the_detail(string culture)
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage,
            BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead]);
        reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var actorId = Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var rows = await SeedRowsReadAsync(reader, actorId);
        var path = $"/api/forms/definitions/{rows.DefinitionId}/records";

        using var sheet = await reader.PostAsJsonAsync(path + "/search", new { pageSize = 20, sort = "number" }, Ct);

        Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
        await ResponseContractCapture.WriteAsync(sheet, "rows-sheet-" + culture, Ct);
        var body = await sheet.ReadJsonAsync();
        var columns = body.GetProperty("columns").EnumerateArray().ToArray();
        Assert.Equal(new[] { rows.Supplier, rows.Group }, columns.Select(column => column.GetProperty("fieldId").GetGuid()));
        Assert.False(columns[0].TryGetProperty("questions", out _));
        Assert.Equal("group", columns[1].GetProperty("type").GetString());
        Assert.Equal(culture == "en" ? "Items" : "Artículos", columns[1].GetProperty("label").GetString());
        Assert.Equal(0, columns[1].GetProperty("options").GetArrayLength());
        var questions = columns[1].GetProperty("questions").EnumerateArray().ToArray();
        Assert.Equal(new[] { rows.Item, rows.Quantity, rows.Note }, questions.Select(question => question.GetProperty("fieldId").GetGuid()));
        Assert.Equal(new[] { "choice", "decimal", "shortText" }, questions.Select(question => question.GetProperty("type").GetString()));
        var records = body.GetProperty("records").GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, records.Length);
        JsonElement Block(JsonElement record) => record.GetProperty("cells").EnumerateArray().Single(cell => cell.GetProperty("fieldId").GetGuid() == rows.Group);
        Assert.Equal("Answered", Block(records[0]).GetProperty("state").GetString());
        var items = Block(records[0]).GetProperty("value").EnumerateArray().ToArray();
        Assert.Equal(culture == "en" ? ["Seed", "Seed", "Fertilizer"] : ["Semilla", "Semilla", "Fertilizante"],
            items.Select(item => item.GetProperty("cells")[0].GetProperty("value").GetProperty("label").GetString()));
        Assert.Equal(["200.5", "100", "7"], items.Select(item => item.GetProperty("cells")[1].GetProperty("value").GetString()));
        Assert.Equal(["Answered", "Unanswered", "Unanswered"], items.Select(item => item.GetProperty("cells")[2].GetProperty("state").GetString()));
        Assert.Equal("Unanswered", Block(records[1]).GetProperty("state").GetString());

        using var detail = await reader.GetAsync("/api/forms/records/" + rows.Answered, Ct);

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await ResponseContractCapture.WriteAsync(detail, "rows-detail-" + culture, Ct);
        var values = (await detail.ReadJsonAsync()).GetProperty("values").EnumerateArray().ToArray();
        Assert.Equal(new[] { rows.Supplier, rows.Group }, values.Select(value => value.GetProperty("fieldId").GetGuid()));
        Assert.Equal("group", values[1].GetProperty("type").GetString());
        var detailItems = values[1].GetProperty("value").EnumerateArray().ToArray();
        Assert.Equal(3, detailItems.Length);
        Assert.Equal(culture == "en" ? "Quantity" : "Cantidad", detailItems[0].GetProperty("values")[1].GetProperty("label").GetString());
        Assert.Equal("Sin daños", detailItems[0].GetProperty("values")[2].GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, detailItems[1].GetProperty("values")[2].GetProperty("value").ValueKind);

        foreach (var fieldId in new[] { rows.Group, rows.Quantity })
        {
            using var invalid = await reader.PostAsJsonAsync(path + "/search", new { sort = "field", sortFieldId = fieldId }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("Forms.Record.FilterInvalid", (await invalid.ReadJsonAsync()).GetProperty("code").GetString());
        }
    }

    private sealed record RowsReadForm(Guid DefinitionId, Guid Supplier, Guid Group, Guid Item, Guid Quantity, Guid Note, Guid Answered);

    /// <summary>A form with a block of 0 to 3 items and two responses captured for real: three items (the same option in two) and none.</summary>
    private async Task<RowsReadForm> SeedRowsReadAsync(HttpClient reader, Guid actorId)
    {
        static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
        using var created = await reader.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Insumos " + Guid.NewGuid().ToString("N"), en = "Supplies" }, description = new { es = "Lista", en = "List" },
            items = new[]
            {
                new { localId = "seed", code = "SEED", text = new { es = "Semilla", en = "Seed" }, isActive = true },
                new { localId = "fertilizer", code = "FERT", text = new { es = "Fertilizante", en = "Fertilizer" }, isActive = true },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalog = await created.ReadJsonAsync();
        var catalogId = catalog.GetProperty("catalog").GetProperty("id").GetGuid();
        var seed = catalog.GetProperty("itemIdentityMap").GetProperty("seed").GetGuid();
        var fertilizer = catalog.GetProperty("itemIdentityMap").GetProperty("fertilizer").GetGuid();
        var supplier = Field("shortText", true) with { Label = Text("Proveedor", "Supplier") };
        var group = Field("group", false) with { Label = Text("Artículos", "Items"), Rows = new(0, 3) };
        var item = Field("choice", true) with { Label = Text("Artículo", "Item"), Source = new(catalogId), Repeated = true };
        var quantity = Field("decimal", true) with { Label = Text("Cantidad", "Quantity"), Repeated = true };
        var note = Field("shortText", false) with { Label = Text("Observación", "Note"), Repeated = true };
        var form = await SeedVersionAsync(actorId, [supplier, group, item, quantity, note]);
        async Task<Guid> CaptureAsync(params Dictionary<string, string>[] items)
        {
            using var scope = factory.Services.CreateScope();
            var preparation = await scope.ServiceProvider.GetRequiredService<IStructuredFormSource>()
                .PrepareAsync(actorId, "es", form.DefinitionId, form.VersionId, false, Ct);
            var captured = await scope.ServiceProvider.GetRequiredService<IStructuredRecordSink>().CaptureAsync(new(Guid.NewGuid(), form.DefinitionId,
                form.VersionId, actorId, factory.Clock.GetUtcNow().UtcDateTime, new Dictionary<string, string>
                {
                    [supplier.Key] = "Proveedor del norte", [group.Key] = JsonSerializer.Serialize(items),
                }, [Assert.Single(preparation.Catalogs)]), Ct);
            Assert.Equal("Completed", captured.State);
            return captured.RecordId!.Value;
        }
        var answered = await CaptureAsync(
            new() { [item.Key] = seed.ToString("D"), [quantity.Key] = "200.5", [note.Key] = "Sin daños" },
            new() { [item.Key] = seed.ToString("D"), [quantity.Key] = "100" },
            new() { [item.Key] = fertilizer.ToString("D"), [quantity.Key] = "7" });
        await CaptureAsync();
        return new(form.DefinitionId, supplier.FieldId, group.FieldId, item.FieldId, quantity.FieldId, note.FieldId, answered);
    }
}
