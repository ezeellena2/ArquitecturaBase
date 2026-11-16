using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormSpreadsheetInflationTests
{
    [Theory]
    [InlineData("xl/styles.xml")]
    [InlineData("xl/sharedStrings.xml")]
    public async Task An_inflated_styles_or_shared_strings_part_is_rejected_before_the_library_opens_the_workbook(string part)
    {
        // Measured before the cap: about 0.6 GiB of memory for the styles part and 1.5 GiB for the shared strings of 98 MiB of XML.
        var bytes = Inflated(part, FormCatalogImportLimits.MaximumArchiveBytes - 2 * 1024 * 1024);
        Assert.True(bytes.Length < 1024 * 1024);
        var result = await FormSpreadsheetFixture.ReadAsync(bytes);
        Assert.True(result.IsFailure);
        Assert.Equal("Forms.CatalogImport.FileTooLarge", result.Error.Code);
    }

    // F12: the cap does not depend on the case of the part's name, nor on where the workbook's own relationships say the part is.
    [Theory]
    [InlineData("xl/Styles.xml", false)]
    [InlineData("XL/SHAREDSTRINGS.XML", false)]
    [InlineData("xl/custom/styles-part.xml", true)]
    public async Task An_inflated_part_under_another_case_or_path_is_rejected_too(string part, bool relationship)
    {
        var bytes = Inflated(part, FormCatalogImportLimits.MaximumArchiveBytes - 2 * 1024 * 1024);
        if (relationship) bytes = Retarget(bytes, part);
        Assert.True(bytes.Length < 1024 * 1024);
        var result = await FormSpreadsheetFixture.ReadAsync(bytes);
        Assert.True(result.IsFailure);
        Assert.Equal("Forms.CatalogImport.FileTooLarge", result.Error.Code);
    }

    private static byte[] Retarget(byte[] workbook, string part)
    {
        using var content = new MemoryStream();
        content.Write(workbook);
        using (var archive = new ZipArchive(content, ZipArchiveMode.Update, true))
        {
            var entry = archive.GetEntry("xl/_rels/workbook.xml.rels")!;
            XDocument document;
            using (var stream = entry.Open()) document = XDocument.Load(stream);
            entry.Delete();
            foreach (var relationship in document.Descendants().Where(value => value.Name.LocalName == "Relationship" && ((string?)value.Attribute("Type"))!.EndsWith("/styles", StringComparison.Ordinal)))
                relationship.SetAttributeValue("Target", part["xl/".Length..]);
            using var target = archive.CreateEntry("xl/_rels/workbook.xml.rels").Open();
            document.Save(target);
        }
        return content.ToArray();
    }

    private static byte[] Inflated(string part, int bytes)
    {
        using var content = new MemoryStream();
        content.Write(FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code"));
        using (var archive = new ZipArchive(content, ZipArchiveMode.Update, true))
        {
            archive.GetEntry(part)?.Delete();
            using var target = archive.CreateEntry(part, CompressionLevel.SmallestSize).Open();
            var styles = part.Contains("styles", StringComparison.OrdinalIgnoreCase);
            var main = "xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"";
            var unit = styles ? "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/>" : "<si><t>x</t></si>";
            var prefix = styles
                ? "<styleSheet " + main + "><fonts count=\"1\"><font/></fonts><fills count=\"1\"><fill/></fills><borders count=\"1\"><border/></borders><cellXfs>"
                : "<sst " + main + ">";
            var suffix = styles ? "</cellXfs></styleSheet>" : "</sst>";
            target.Write(Encoding.UTF8.GetBytes(prefix));
            var chunk = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(unit, 4000)));
            for (var written = 0L; written < bytes; written += chunk.Length) target.Write(chunk);
            target.Write(Encoding.UTF8.GetBytes(suffix));
        }
        return content.ToArray();
    }
}
