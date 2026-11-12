using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;

// A fictitious file attached to the assistant: a spreadsheet or a read document. What the owner prepared is opaque to the engine:
// its shape lives only here.
internal static class FictionalAttachment
{
    public const string MediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string PdfMediaType = "application/pdf";
    public const string DocumentName = "Comprobante ficticio.pdf";

    // A spreadsheet sealed before documents existed, as the engine wrote it then, and the sheet it carries.
    public const string SealedSheetJson = """{"fileName":"clientes.xlsx","kind":0,"mediaType":"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","size":2048,"sha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","sheetName":"Clientes ficticios","rowCount":12,"columnCount":3,"rows":[{"number":1,"cells":{"A":"C\u00F3digo","B":"Nombre"}}]}""";
    public const string SealedSpreadsheetJson = """{"storageKey":"ai/assistant/attachments/0193a5f2c4d87e6fb1a2c3d4e5f60718.xlsx","name":"Clientes ficticios.xlsx","contentType":"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","size":2048,"sha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","expiresAtUtc":"2026-12-11T12:00:00Z","spreadsheet":""" + SealedSheetJson + "}";

    public static StructuredSpreadsheet Spreadsheet(string sheetName = "Clientes ficticios") =>
        new("clientes.xlsx", StructuredSpreadsheetKind.Xlsx, MediaType, 2048, new string('c', 64), sheetName, 12, 3, []);

    public static AssistantAttachmentInput Input(DateTime expiresAtUtc, string key = "fixture/attachments/clientes.xlsx",
        string sheetName = "Clientes ficticios") =>
        new(key, "Clientes ficticios.xlsx", MediaType, 2048, new string('c', 64), expiresAtUtc, Spreadsheet(sheetName));

    // A one-page PDF as the engine read it, with an invented text.
    public static StructuredDocument Document(string text = "Transferencia ficticia de $ 1.500 a Comercio Ejemplo", string fileName = "comprobante.pdf",
        char hash = 'd') =>
        new(fileName, StructuredDocumentKind.Pdf, PdfMediaType, 4096, new string(hash, 64), 1,
            new StructuredDocumentReading(StructuredDocumentReadability.Readable, null, "es", text, false));

    public static AssistantAttachmentInput DocumentInput(DateTime expiresAtUtc, string key = "fixture/documents/comprobante.pdf",
        StructuredDocument? document = null, IReadOnlyList<StructuredDocument>? moreDocuments = null) =>
        AssistantAttachmentInput.ForDocument(key, DocumentName, expiresAtUtc, document ?? Document(), moreDocuments);

    public static string Json(AssistantAttachmentInput attachment) => JsonSerializer.Serialize(attachment, JsonSerializerOptions.Web);

    public static string Json(StructuredSpreadsheet? spreadsheet) => JsonSerializer.Serialize(spreadsheet, JsonSerializerOptions.Web);
}
