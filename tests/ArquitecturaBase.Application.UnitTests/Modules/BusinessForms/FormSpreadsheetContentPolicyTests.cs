using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// The one rule for a row with content: the import counts it, the assistant's preparation shows it and the import from the copy skips
// the first one as the header the model saw.
public sealed class FormSpreadsheetContentPolicyTests
{
    [Fact]
    public void A_row_has_content_with_some_text_or_a_cell_that_could_not_be_read()
    {
        Assert.True(FormSpreadsheetContentPolicy.HasContent(new FormSpreadsheetRow(1, [new(null), new(" x ")])));
        Assert.True(FormSpreadsheetContentPolicy.HasContent(new FormSpreadsheetRow(2, [new(null, FormCatalogImportErrors.CellUnreadable)])));
        Assert.False(FormSpreadsheetContentPolicy.HasContent(new FormSpreadsheetRow(3, [new(null), new(""), new(" \t ")])));
        Assert.False(FormSpreadsheetContentPolicy.HasContent(new FormSpreadsheetRow(4, [])));
    }
}
