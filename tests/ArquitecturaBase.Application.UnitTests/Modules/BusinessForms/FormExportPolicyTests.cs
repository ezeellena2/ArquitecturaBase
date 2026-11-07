using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormExportPolicyTests
{
    [Fact]
    public void Missing_questions_empty_values_numbers_and_booleans_keep_distinct_excel_types()
    {
        Assert.Equal("—", FormExportPolicy.Cell(Cell("shortText", "NotAsked", null)).Value);
        Assert.Null(FormExportPolicy.Cell(Cell("shortText", "Unanswered", null)).Value);
        Assert.Equal(12.50m, FormExportPolicy.Cell(Cell("decimal", "Answered", "12.50")).Value);
        Assert.Equal(0m, FormExportPolicy.Cell(Cell("decimal", "Answered", "0")).Value);
        Assert.Equal(false, FormExportPolicy.Cell(Cell("boolean", "Answered", false)).Value);
        Assert.IsType<DateTime>(FormExportPolicy.Cell(Cell("date", "Answered", "2026-10-16")).Value);
    }
    [Fact]
    public void Catalogs_keep_the_captured_text_and_files_only_export_count_and_names()
    {
        Assert.Equal("Retired label", FormExportPolicy.Cell(Cell("choice", "Answered", new StructuredRecordChoiceValue("id", "Retired label"))).Value);
        Assert.Equal("2 · a.pdf; b.pdf", FormExportPolicy.Cell(Cell("document", "Answered", new StructuredRecordFileValue[] {
            new(Guid.NewGuid(), "a.pdf", "application/pdf", 10, false), new(Guid.NewGuid(), "b.pdf", "application/pdf", 20, false) })).Value);
        Assert.Equal("=SUM(A1)", FormExportPolicy.Cell(Cell("shortText", "Answered", "=SUM(A1)")).Value);
    }
    [Fact]
    public void The_block_exports_how_many_items_it_has()
    {
        var cell = FormExportPolicy.Cell(Cell("group", "Answered", new FormResponseRowCells[] { new([]), new([]), new([]) }));

        Assert.Equal(("integer", (object?)3L), (cell.Kind, cell.Value));
        Assert.Null(FormExportPolicy.Cell(Cell("group", "Unanswered", Array.Empty<FormResponseRowCells>())).Value);
        Assert.Equal("—", FormExportPolicy.Cell(Cell("group", "NotAsked", null)).Value);
    }
    [Theory]
    [InlineData("Artículos", "es", "Artículos")]
    [InlineData("Insumos: semillas/fertilizantes [lote]*?\\", "es", "Insumos semillasfertilizantes l")]
    [InlineData("'Artículos'", "es", "Artículos")]
    [InlineData("Respuestas", "es", "Ítems")]
    [InlineData("responses", "en", "Items")]
    [InlineData(" :/?* ", "en", "Items")]
    public void The_sheet_of_a_block_takes_its_name_without_what_excel_rejects(string label, string culture, string expected) =>
        Assert.Equal(expected, FormExportPolicy.SheetName(label, [], culture));
    [Fact]
    public void A_sheet_name_already_taken_gets_its_number_within_31_characters()
    {
        Assert.Equal("Artículos (2)", FormExportPolicy.SheetName("Artículos", ["Respuestas", "artículos"], "es"));
        Assert.Equal("Artículos (3)", FormExportPolicy.SheetName("Artículos", ["Respuestas", "Artículos", "Artículos (2)"], "es"));
        Assert.Equal(new string('a', 27) + " (2)", FormExportPolicy.SheetName(new string('a', 40), [new string('a', 31)], "es"));
        Assert.Equal("Ítems (2)", FormExportPolicy.SheetName("", ["Respuestas", "Ítems"], "es"));
    }
    private static FormResponseCell Cell(string type, string state, object? value) => new(Guid.NewGuid(), type, state, value);
}
