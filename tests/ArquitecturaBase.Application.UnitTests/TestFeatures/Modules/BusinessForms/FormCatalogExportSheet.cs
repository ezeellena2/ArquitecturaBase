using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

// The sheet the XLSX reader returns for an exported workbook: numbers and dates come back canonical, the rest as text.
internal static class FormCatalogExportSheet
{
    public static FormSpreadsheet Read(FormWorkbookSheet workbook) => new("Options", new('e', 64),
        new[] { new FormSpreadsheetRow(1, workbook.Headers.Select(header => new FormSpreadsheetCell(header)).ToArray()) }
            .Concat(workbook.Rows.Select((row, index) => new FormSpreadsheetRow(index + 2, row.Select(Read).ToArray()))).ToArray());

    private static FormSpreadsheetCell Read(FormWorkbookCell cell) => cell.Value switch
    {
        long number => new(number.ToString(CultureInfo.InvariantCulture), IsCanonicalValue: true),
        decimal number => new(number.ToString(CultureInfo.InvariantCulture), IsCanonicalValue: true),
        DateTime date => new(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), IsCanonicalValue: true),
        var text => new((string?)text),
    };
}
