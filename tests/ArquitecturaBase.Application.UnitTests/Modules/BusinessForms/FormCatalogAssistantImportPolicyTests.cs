using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Stage 2 of the assistant authoring: the spreadsheet a proposal mapped, read like an upload with the import's headers.
public sealed class FormCatalogAssistantImportPolicyTests
{
    private static readonly string Hash = new('c', 64);

    private static FormSpreadsheetRow Row(int number, params string?[] cells) => new(number, cells.Select(text => new FormSpreadsheetCell(text)).ToArray());
    private static FormSpreadsheet Sheet(params FormSpreadsheetRow[] rows) => new("Hoja1", Hash, rows);
    private static FormAuthoringSpreadsheetSummary Summary(params FormAuthoringMappedColumn[] columns) =>
        new("transportistas.xlsx", "Hoja1", 2, true, columns, []);
    private static BusinessFormCatalogField Field(string key, string name, string type, bool active = true, int position = 0) =>
        new(BusinessFormCatalogField.CreateIdentity(), key, new Dictionary<string, string> { ["es"] = name }, type, null, false, false, null, null,
            null, "data", position, active);

    [Fact]
    public void The_mapped_sheet_has_the_import_headers_in_the_proposal_order_and_leaves_out_the_header_row()
    {
        var sheet = Sheet(Row(1, "Teléfono", "CUIT", "Razón social"), Row(2, "11 5555-0001", "20-1", "Transportes Sur"), Row(3, null, "20-2"));

        var mapped = FormCatalogAssistantImportPolicy.Sheet(sheet, true, [new("B", "Código"), new("C", "Texto"), new("A", "telefono")]);

        // The SHA-256 is the copy's: the review and the import marker name the same file.
        Assert.Equal(("Hoja1", Hash), (mapped.Name, mapped.Sha256));
        Assert.Equal(1, mapped.Rows[0].Number);
        Assert.Equal(["Código", "Texto", "telefono"], mapped.Rows[0].Cells.Select(cell => cell.Text));
        Assert.Equal([2, 3], mapped.Rows.Skip(1).Select(row => row.Number));
        Assert.Equal(["20-1", "Transportes Sur", "11 5555-0001"], mapped.Rows[1].Cells.Select(cell => cell.Text));
        // A short row has nothing in its missing columns.
        Assert.Equal(["20-2", null, null], mapped.Rows[2].Cells.Select(cell => cell.Text));
    }

    // Row 1 for the model is the first row with content of the prepared sheet: an empty row on top is not the header.
    [Fact]
    public void With_headers_the_header_is_the_first_row_with_content_as_the_model_saw_it()
    {
        var sheet = Sheet(Row(1, null, "  "), Row(2, "CUIT", "Razón social"), Row(3), Row(4, "20-1", "Transportes Sur"));

        var mapped = FormCatalogAssistantImportPolicy.Sheet(sheet, true, [new("A", "Código"), new("B", "Texto")]);

        Assert.Equal(2, mapped.Rows[0].Number);
        Assert.Equal([4], mapped.Rows.Skip(1).Select(row => row.Number));
        Assert.Equal(["20-1", "Transportes Sur"], mapped.Rows[1].Cells.Select(cell => cell.Text));
    }

    // A simple list: the code and the option come from one column, and without headers row 1 is data.
    [Fact]
    public void Without_headers_row_1_is_data_and_the_code_and_the_option_can_share_a_column()
    {
        var sheet = Sheet(Row(1, "Rojo"), Row(2, "Verde"));

        var mapped = FormCatalogAssistantImportPolicy.Sheet(sheet, false, [new("A", "Código"), new("A", "Texto")]);

        Assert.Equal(["Código", "Texto"], mapped.Rows[0].Cells.Select(cell => cell.Text));
        Assert.Equal([1, 2], mapped.Rows.Skip(1).Select(row => row.Number));
        Assert.Equal([["Rojo", "Rojo"], ["Verde", "Verde"]], mapped.Rows.Skip(1).Select(row => row.Cells.Select(cell => cell.Text).ToArray()));
    }

    [Fact]
    public void Each_cell_keeps_its_text_its_error_and_whether_it_is_canonical()
    {
        FormSpreadsheetCell[] cells = [new("A1"), new("1234.5", IsCanonicalValue: true), new(null, FormCatalogImportErrors.FormulaValueMissing)];
        var sheet = Sheet(Row(1, "Código", "Peso", "Fecha"), new FormSpreadsheetRow(2, cells));

        var mapped = FormCatalogAssistantImportPolicy.Sheet(sheet, true, [new("C", "fecha"), new("A", "Código"), new("B", "peso")]);

        Assert.Equal([cells[2], cells[0], cells[1]], mapped.Rows[1].Cells);
    }

    // Each column takes the header an upload of the template would have: a field by its key, also one that is no longer active,
    // which the import then warns as a column that is not the catalog's, as it would for an uploaded file.
    [Fact]
    public void Columns_take_the_headers_of_the_import_and_keep_a_field_that_is_no_longer_active()
    {
        var phone = Field("telefono", "Teléfono", "text");
        var fax = Field("fax", "Fax", "text", active: false, position: 1);
        var detail = Field("detalle", "Detalle", "text", active: false, position: 2);
        var summary = Summary(new("code", null, null, "A", "CUIT"), new("option", null, null, "B", "Razón social"),
            new("value", null, null, "C", "Kilos"), new("detail", null, null, "D", "Notas"), new("field", "Teléfono", "telefono", "E", "Tel"),
            new("field", "Fax", "fax", "F", "Fax"), new("field", "Detalle", "detalle", "G", "Detalle"));

        var columns = FormCatalogAssistantImportPolicy.Columns(summary, BusinessFormCatalogValueType.Integer, "es", [phone, fax, detail],
            column => column.Key);

        // A key that is also a fixed header keeps its «[campo]», so it never reads as the detail.
        Assert.Equal([("A", "Código"), ("B", "Texto"), ("C", "Valor (entero)"), ("D", "Detalle"), ("E", "telefono"), ("F", "fax"), ("G", "detalle [campo]")],
            columns.Select(column => (column.Letter, column.Header)));
    }

    [Fact]
    public void A_column_of_a_field_that_is_no_longer_active_is_warned_as_unknown_and_imports_nothing()
    {
        var phone = Field("telefono", "Teléfono", "text");
        var fax = Field("fax", "Fax", "text", active: false, position: 1);
        var source = new FormCatalogImportSource(Guid.NewGuid(), 1, BusinessFormCatalogValueType.Text, "es", [], Fields: [phone, fax]);
        var columns = FormCatalogAssistantImportPolicy.Columns(Summary(new("code", null, null, "A", "CUIT"), new("option", null, null, "B", "Razón social"),
            new("field", "Teléfono", "telefono", "C", "Tel"), new("field", "Fax", "fax", "D", "Fax")), BusinessFormCatalogValueType.Text, "es",
            [phone, fax], column => column.Key);
        var sheet = Sheet(Row(1, "CUIT", "Razón social", "Tel", "Fax"), Row(2, "20-1", "Transportes Sur", "11 5555-0001", "11 5555-0002"));

        var plan = FormCatalogImportPolicy.Build(source, FormCatalogAssistantImportPolicy.Sheet(sheet, true, columns), FormCatalogImportMode.Replace, "es");

        Assert.True(plan.IsSuccess);
        var warning = Assert.Single(plan.Value.Preview.Issues);
        Assert.Equal(("fax", "Warning", FormCatalogImportErrors.UnknownColumn.Code, 1), (warning.Column, warning.Severity, warning.Code, warning.Row));
        var added = Assert.Single(plan.Value.Preview.Added);
        Assert.Equal(["11 5555-0001"], added.Fields!.Values);
        Assert.Equal(phone.Id.ToString("D"), Assert.Single(added.Fields).Key);
    }

    // The field's header does not make its cells canonical: a typed text is read in the menu's culture, a number cell canonically.
    [Fact]
    public void The_import_reads_the_mapped_sheet_like_an_upload_and_keeps_where_each_value_came_from()
    {
        var weight = Field("peso", "Peso", "decimal");
        var source = new FormCatalogImportSource(Guid.NewGuid(), 1, BusinessFormCatalogValueType.Integer, "es", [], Fields: [weight]);
        var columns = FormCatalogAssistantImportPolicy.Columns(Summary(new("code", null, null, "A", "CUIT"),
                new("option", null, null, "B", "Razón social"), new("value", null, null, "C", "Kilos"), new("field", "Peso", "peso", "D", "Peso")),
            BusinessFormCatalogValueType.Integer, "es", [weight], column => column.Key);
        var sheet = Sheet(Row(1, "CUIT", "Razón social", "Kilos", "Peso"),
            new FormSpreadsheetRow(2, [new("20-1"), new("Transportes Sur"), new("1200", IsCanonicalValue: true), new("1234.5", IsCanonicalValue: true)]),
            Row(3, "20-2", "Logística Pampa", "800", "1.234,5"));

        var plan = FormCatalogImportPolicy.Build(source, FormCatalogAssistantImportPolicy.Sheet(sheet, true, columns), FormCatalogImportMode.Replace, "es");

        Assert.True(plan.IsSuccess);
        Assert.Empty(plan.Value.Preview.Issues);
        Assert.Equal(["20-1", "20-2"], plan.Value.Preview.Added.Select(row => row.Code));
        Assert.Equal(["Transportes Sur", "Logística Pampa"], plan.Value.Preview.Added.Select(row => row.Text));
        Assert.Equal(["1200", "800"], plan.Value.Preview.Added.Select(row => row.Value));
        Assert.All(plan.Value.Preview.Added, row => Assert.Equal("1234.5", row.Fields![weight.Id.ToString("D")]));
    }
}
