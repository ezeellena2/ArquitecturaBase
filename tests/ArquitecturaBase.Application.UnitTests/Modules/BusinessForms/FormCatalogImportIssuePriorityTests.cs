using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogImportIssuePriorityTests
{
    [Fact]
    public void Preview_keeps_errors_visible_when_early_warnings_exceed_the_display_limit()
    {
        var rows = new[] { new[] { "Code", "Text", "Extra header" } }
            .Concat(Enumerable.Range(1, 150).Select(index => new[] { "A" + index, "Repeated", "Ignored" }))
            .Append(["BAD", new string('x', 31), "Ignored"]);
        var preview = Preview(rows);

        Assert.Equal(1, preview.Counts.ErrorRows);
        Assert.Equal(150, preview.Counts.Warnings);
        Assert.Equal(100, preview.Issues.Count);
        var error = Assert.Single(preview.Issues, issue => issue.Severity == "Error");
        Assert.Same(error, preview.Issues[0]);
        Assert.Equal(152, error.Row);
        Assert.Equal("text", error.Column);
        Assert.Equal(FormCatalogImportErrors.TextTooLong.Code, error.Code);
        Assert.Contains(preview.Issues, issue => issue.Severity == "Warning" && issue.Column == "Extra header");
        Assert.True(preview.ListsTruncated);
    }

    [Fact]
    public void A_duplicate_text_warning_identifies_the_other_file_row()
    {
        var preview = Preview([["Code", "Text"], ["A", "Repeated"], ["B", "REPEATED"], ["C", "Repeated"]]);

        Assert.Equal(0, preview.Counts.ErrorRows);
        Assert.Equal(2, preview.Counts.Warnings);
        Assert.All(preview.Issues, issue =>
        {
            Assert.Equal("Warning", issue.Severity);
            Assert.Equal(FormCatalogImportErrors.DuplicateText.Code, issue.Code);
            Assert.Equal(2, issue.RelatedRow);
        });
    }

    [Fact]
    public void A_warning_against_a_catalog_item_does_not_invent_a_file_row()
    {
        var catalogId = Guid.NewGuid();
        var item = new FormCatalogStoredItemRow(Guid.NewGuid(), catalogId, "OLD", "Repeated", null, null, null, 1, true, null);
        var sheet = new FormSpreadsheet("Options", new string('a', 64),
            [new(1, [new("Code"), new("Text")]), new(2, [new("NEW"), new("Repeated")])]);
        var result = FormCatalogImportPolicy.Build(new(catalogId, 1, BusinessFormCatalogValueType.Text, "es", [item]),
            sheet, FormCatalogImportMode.Append, "en");

        Assert.True(result.IsSuccess);
        var warning = Assert.Single(result.Value.Preview.Issues);
        Assert.Equal(FormCatalogImportErrors.DuplicateText.Code, warning.Code);
        Assert.Null(warning.RelatedRow);
    }

    private static FormCatalogImportPreviewResponse Preview(IEnumerable<string[]> rows)
    {
        var sheet = new FormSpreadsheet("Options", new string('a', 64), rows.Select((cells, index) =>
            new FormSpreadsheetRow(index + 1, cells.Select(text => new FormSpreadsheetCell(text)).ToArray())).ToArray());
        var source = new FormCatalogImportSource(Guid.NewGuid(), 1, BusinessFormCatalogValueType.Text, "es", []);
        var result = FormCatalogImportPolicy.Build(source, sheet, FormCatalogImportMode.Append, "en");
        Assert.True(result.IsSuccess);
        return result.Value.Preview;
    }
}
