using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormResponseEndpointsTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task A_block_of_rows_reads_one_column_with_its_items_in_the_sheet_and_the_detail(string culture)
    {
        using var reader = await RowsReaderAsync(culture);
        var rows = await SeedRowsReadAsync(reader, [new("seed", "200.5", "Sin daños"), new("seed", "100"), new("fertilizer", "7")], []);
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
        Assert.Equal(new[] { "choice", "decimal", "longText" }, questions.Select(question => question.GetProperty("type").GetString()));
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

        using var detail = await reader.GetAsync("/api/forms/records/" + rows.Records[0], Ct);

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

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Filters_on_the_block_look_for_one_item_that_meets_every_condition_and_count_responses(string culture)
    {
        using var reader = await RowsReaderAsync(culture);
        var rows = await SeedRowsReadAsync(reader, [new("seed", "200.5", "Sin daños"), new("seed", "100"), new("fertilizer", "7")], [],
            [new("fertilizer", "200.5"), new("seed", "5", "Con daños")]);
        var path = $"/api/forms/definitions/{rows.DefinitionId}/records";
        var (seed, fertilizer) = (rows.Seed.ToString("D"), rows.Fertilizer.ToString("D"));
        static object Condition(Guid fieldId, string operation, params string[]? values) => new { fieldId, @operator = operation, values };
        async Task<Guid[]> FoundAsync(object[] conditions, string? search = null)
        {
            using var response = await reader.PostAsJsonAsync(path + "/search", new { sort = "number", search, conditions }, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = (await response.ReadJsonAsync()).GetProperty("records");
            var found = page.GetProperty("items").EnumerateArray().Select(record => record.GetProperty("id").GetGuid()).ToArray();
            Assert.Equal(found.Length, page.GetProperty("totalCount").GetInt32());
            return found;
        }
        async Task<Dictionary<string, int>> CountsAsync(object[] conditions, Guid fieldId, string dimension = "choice", string? capture = null)
        {
            using var response = await reader.PostAsJsonAsync(path + "/filter-counts/search", new { filter = new { conditions }, dimension, fieldId }, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            if (capture is not null) await ResponseContractCapture.WriteAsync(response, capture, Ct);
            return (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray()
                .ToDictionary(option => option.GetProperty("id").GetString()!, option => option.GetProperty("count").GetInt32());
        }

        // One condition is met by any item, and a response with the option in two items counts once.
        Assert.Equal([rows.Records[0], rows.Records[2]], await FoundAsync([Condition(rows.Item, "in", seed)]));
        // Several conditions of the block are met by one same item: the third response has the seed and 200.5 in different items.
        Assert.Equal([rows.Records[0]], await FoundAsync([Condition(rows.Item, "in", seed), Condition(rows.Quantity, "gte", "200")]));
        Assert.Equal([rows.Records[2]], await FoundAsync([Condition(rows.Item, "in", fertilizer), Condition(rows.Quantity, "gte", "200")]));
        // Presence looks at the whole response, not at the item of the other conditions.
        Assert.Equal([rows.Records[0], rows.Records[2]], await FoundAsync([Condition(rows.Item, "in", fertilizer), Condition(rows.Note, "answered", null)]));
        Assert.Equal([rows.Records[1]], await FoundAsync([Condition(rows.Quantity, "unanswered", null)]));
        // «Buscar en textos largos» also reads the long texts of the items.
        Assert.Equal([rows.Records[0], rows.Records[2]], await FoundAsync([], "daños"));

        Assert.Equal(new Dictionary<string, int> { [seed] = 2, [fertilizer] = 2 }, await CountsAsync([], rows.Item));
        // Each option counts the responses with an item that has it and meets, in that same item, the other conditions of the block.
        Assert.Equal(new Dictionary<string, int> { [seed] = 1, [fertilizer] = 1 },
            await CountsAsync([Condition(rows.Quantity, "gte", "200")], rows.Item, capture: "rows-counts-" + culture));
        Assert.Equal(new Dictionary<string, int> { ["answered"] = 2, ["unanswered"] = 1, ["notAsked"] = 0 }, await CountsAsync([], rows.Note, "presence"));

        using var onTheBlock = await reader.PostAsJsonAsync(path + "/search", new { conditions = new[] { Condition(rows.Group, "answered", null) } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, onTheBlock.StatusCode);
        using var countsOfTheBlock = await reader.PostAsJsonAsync(path + "/filter-counts/search", new { filter = new { }, dimension = "presence", fieldId = rows.Group }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, countsOfTheBlock.StatusCode);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task The_excel_puts_the_block_in_the_same_sheet_with_one_row_per_item(string culture)
    {
        using var reader = await RowsReaderAsync(culture);
        var rows = await SeedRowsReadAsync(reader, [new("seed", "200.5", "Sin daños"), new("seed", "100"), new("fertilizer", "7")], []);

        using var export = await reader.PostAsJsonAsync($"/api/forms/definitions/{rows.DefinitionId}/records/export", new { filter = new { sort = "number" } }, Ct);

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        await ResponseContractCapture.WriteAsync(export, "rows-export-" + culture, Ct, "xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(await export.Content.ReadAsByteArrayAsync(Ct)));
        var sheet = Assert.Single(workbook.Worksheets);
        Assert.Equal(culture == "en" ? "Responses" : "Respuestas", sheet.Name);
        // «Ítem» and the questions of the block take its place (XL1-A).
        Assert.Equal(culture == "en" ? ["Item", "Product", "Quantity", "Note"] : ["Ítem", "Artículo", "Cantidad", "Observación"],
            sheet.Row(1).Cells(7, 10).Select(cell => cell.GetString()));
        // The three items of the first response repeat its number; the second one has no items and takes a row.
        var first = sheet.Cell(2, 1).GetString();
        Assert.Equal([first, first, first], sheet.Column(1).Cells(2, 4).Select(cell => cell.GetString()));
        Assert.NotEqual(first, sheet.Cell(5, 1).GetString());
        Assert.Equal(XLDataType.Number, sheet.Cell(2, 7).DataType);
        Assert.Equal([1, 2, 3], sheet.Column(7).Cells(2, 4).Select(cell => cell.GetValue<int>()));
        Assert.Equal(culture == "en" ? ["Seed", "Seed", "Fertilizer"] : ["Semilla", "Semilla", "Fertilizante"], sheet.Column(8).Cells(2, 4).Select(cell => cell.GetString()));
        Assert.Equal([200.5m, 100m, 7m], sheet.Column(9).Cells(2, 4).Select(cell => cell.GetValue<decimal>()));
        Assert.Equal("Sin daños", sheet.Cell(2, 10).GetString());
        Assert.True(sheet.Cell(3, 10).IsEmpty());
        Assert.All(sheet.Row(5).Cells(7, 10), cell => Assert.True(cell.IsEmpty()));
        // The second response starts with a line.
        Assert.Equal(XLBorderStyleValues.Thin, sheet.Cell(5, 1).Style.Border.TopBorder);
        Assert.True(sheet.Row(6).IsEmpty());
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task The_preview_of_the_pdf_takes_the_block_with_two_example_items(string culture)
    {
        using var reader = await RowsReaderAsync(culture);
        var rows = await SeedRowsReadAsync(reader);

        using var pdf = await reader.PostAsJsonAsync($"/api/forms/definitions/{rows.DefinitionId}/pdf-design/preview", new
        {
            design = new FormPdfDesign(new Dictionary<string, string> { ["es"] = "Constancia de recepción", ["en"] = "Receipt" }, [rows.Supplier, rows.Group], false, null, true),
            culture,
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        await FormPdfContractCapture.WriteAsync(pdf, "rows-preview-" + culture, Ct, "pdf");
        using var invalid = await reader.PostAsJsonAsync($"/api/forms/definitions/{rows.DefinitionId}/pdf-design/preview", new
        {
            design = new FormPdfDesign(new Dictionary<string, string> { ["es"] = "Constancia", ["en"] = "Receipt" }, [rows.Quantity], false, null, true), culture,
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
