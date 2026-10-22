using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Import;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal static class FormSpreadsheetPreflightFixture
{
    private static readonly MethodInfo InspectArchive = typeof(ClosedXmlFormSpreadsheetReader)
        .GetMethod("InspectArchive", BindingFlags.NonPublic | BindingFlags.Static)!;

    public static Result<Dictionary<int, int[]>> Inspect(byte[] workbook) =>
        (Result<Dictionary<int, int[]>>)InspectArchive.Invoke(null, [workbook, TestContext.Current.CancellationToken])!;

    public static byte[] Sheet(byte[] workbook, string rows, int number = 1)
    {
        // Write large synthetic grids directly: the test must not allocate an XML DOM or load ClosedXML.
        using var content = new MemoryStream();
        content.Write(workbook);
        using (var archive = new ZipArchive(content, ZipArchiveMode.Update, true))
        {
            archive.GetEntry($"xl/worksheets/sheet{number}.xml")!.Delete();
            using var target = archive.CreateEntry($"xl/worksheets/sheet{number}.xml").Open();
            using var writer = new StreamWriter(target, new UTF8Encoding(false));
            writer.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            writer.Write(rows);
            writer.Write("</sheetData></worksheet>");
        }
        return content.ToArray();
    }

    public static byte[] AliasSecondSheet(byte[] workbook, bool sameRelationship)
    {
        using var content = new MemoryStream();
        content.Write(workbook);
        using (var archive = new ZipArchive(content, ZipArchiveMode.Update, true))
        {
            var book = ReadPart(archive, "xl/workbook.xml");
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relation = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var sheets = book.Descendants(ns + "sheet").ToArray();
            var firstId = sheets[0].Attribute(relation + "id")!.Value;
            if (sameRelationship)
            {
                sheets[1].SetAttributeValue(relation + "id", firstId);
                WritePart(archive, "xl/workbook.xml", book);
            }
            else
            {
                var rels = ReadPart(archive, "xl/_rels/workbook.xml.rels");
                var relationships = rels.Root!.Elements().ToArray();
                var firstTarget = relationships.Single(item => item.Attribute("Id")!.Value == firstId).Attribute("Target")!.Value;
                var secondId = sheets[1].Attribute(relation + "id")!.Value;
                relationships.Single(item => item.Attribute("Id")!.Value == secondId).SetAttributeValue("Target", firstTarget);
                WritePart(archive, "xl/_rels/workbook.xml.rels", rels);
            }
        }
        return content.ToArray();
    }

    private static XDocument ReadPart(ZipArchive archive, string path)
    {
        using var source = archive.GetEntry(path)!.Open();
        return XDocument.Load(source);
    }

    private static void WritePart(ZipArchive archive, string path, XDocument document)
    {
        archive.GetEntry(path)!.Delete();
        using var target = archive.CreateEntry(path).Open();
        document.Save(target);
    }

    public static string Rows(int count, int cells, bool content = false)
    {
        var row = "<row>" + string.Concat(Enumerable.Repeat(content ? "<c><v>1</v></c>" : "<c s=\"0\"/>", cells)) + "</row>";
        var xml = new StringBuilder(row.Length * count);
        for (var index = 0; index < count; index++) xml.Append(row);
        return xml.ToString();
    }
}
