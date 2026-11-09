using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;

// A fictitious spreadsheet attached to the assistant. The prepared sheet is opaque to the engine: its shape lives only here.
internal static class FictionalAttachment
{
    public const string MediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static StructuredSpreadsheet Spreadsheet(string sheetName = "Clientes ficticios") =>
        new("clientes.xlsx", StructuredSpreadsheetKind.Xlsx, MediaType, 2048, new string('c', 64), sheetName, 12, 3, []);

    public static AssistantAttachmentInput Input(DateTime expiresAtUtc, string key = "fixture/attachments/clientes.xlsx",
        string sheetName = "Clientes ficticios") =>
        new(key, "Clientes ficticios.xlsx", MediaType, 2048, new string('c', 64), expiresAtUtc, Spreadsheet(sheetName));

    public static string Json(AssistantAttachmentInput attachment) => JsonSerializer.Serialize(attachment, JsonSerializerOptions.Web);

    public static string Json(StructuredSpreadsheet spreadsheet) => JsonSerializer.Serialize(spreadsheet, JsonSerializerOptions.Web);
}
