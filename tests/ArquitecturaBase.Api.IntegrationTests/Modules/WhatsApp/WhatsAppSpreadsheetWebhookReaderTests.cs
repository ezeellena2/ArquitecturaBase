using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// Stage 2 of the assistant authoring: a document that is a spreadsheet, by its name or by the type Meta sends, comes as a
/// spreadsheet with its descriptor and its caption; any other document stays as media. They do not use the database.
/// </summary>
public sealed class WhatsAppSpreadsheetWebhookReaderTests
{
    private const string WaId = "5493413654813";
    private const string Bsuid = "AR.1102953142229032";
    private const string MetaHash = "JTF3Dq2S5uMyJnl2OZw6a0VvbQuVWb0uYUCb+KeBP4U=";
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly DateTime SentAtUtc = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_excel_workbook_with_a_caption_is_a_spreadsheet_with_its_descriptor_and_the_caption_as_its_body()
    {
        var message = Read(MetaWebhook.Document("wamid.xlsx", WaId, Bsuid, SentAtUtc, "Clientes.xlsx", XlsxType, "Armame un catálogo con esto"));

        Assert.Equal(WhatsAppMessageKind.Spreadsheet, message.Kind);
        Assert.Equal("Armame un catálogo con esto", message.Body);
        Assert.Null(message.ReplyId);
        Assert.Equal(new WhatsAppWebhookSpreadsheetDescriptor("1003383421387256", XlsxType,
            Convert.ToHexStringLower(Convert.FromBase64String(MetaHash)), "Clientes.xlsx"), message.Spreadsheet);
        Assert.DoesNotContain("Clientes", message.Spreadsheet!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/vnd.ms-excel")]
    [InlineData("text/csv")]
    [InlineData("application/octet-stream")]
    public void A_csv_is_a_spreadsheet_by_its_name_with_any_type_Meta_sends_and_without_a_caption_has_no_body(string type)
    {
        var message = Read(MetaWebhook.Document("wamid.csv", WaId, Bsuid, SentAtUtc, "clientes.CSV", type));

        Assert.Equal(WhatsAppMessageKind.Spreadsheet, message.Kind);
        Assert.Null(message.Body);
        Assert.Equal((type, "clientes.CSV"), (message.Spreadsheet!.ContentType, message.Spreadsheet.FileName));
    }

    [Theory]
    [InlineData("viejo.xls")]
    [InlineData("con macros.xlsm")]
    [InlineData("binario.xlsb")]
    public void Old_macro_and_binary_workbooks_are_spreadsheets_so_their_reason_can_be_answered(string name)
    {
        Assert.Equal(WhatsAppMessageKind.Spreadsheet, Read(MetaWebhook.Document("wamid.old", WaId, Bsuid, SentAtUtc, name, "application/octet-stream")).Kind);
    }

    [Theory]
    [InlineData(XlsxType)]
    [InlineData("application/vnd.ms-excel")]
    [InlineData("text/csv; charset=utf-8")]
    [InlineData("application/vnd.ms-excel.sheet.macroEnabled.12")]
    [InlineData("application/vnd.ms-excel.sheet.binary.macroEnabled.12")]
    public void A_document_without_a_name_is_a_spreadsheet_by_the_type_Meta_sends(string type)
    {
        var message = Read(MetaWebhook.Document("wamid.typed", WaId, Bsuid, SentAtUtc, null, type));

        Assert.Equal(WhatsAppMessageKind.Spreadsheet, message.Kind);
        Assert.Null(message.Spreadsheet!.FileName);
    }

    [Theory]
    [InlineData("contrato.pdf", "application/pdf")]
    [InlineData("notas.txt", "text/plain")]
    [InlineData(null, "text/plain")]
    [InlineData("informe.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void Any_other_document_is_a_received_document_with_its_caption(string? name, string type)
    {
        var message = Read(MetaWebhook.Document("wamid.other", WaId, Bsuid, SentAtUtc, name, type, "Mirá este archivo"));

        // Task 8 of the documents: it is no longer media (WhatsAppDocumentWebhookReaderTests).
        Assert.Equal(WhatsAppMessageKind.ReceivedDocument, message.Kind);
        Assert.Equal("Mirá este archivo", message.Body);
        Assert.Null(message.Spreadsheet);
        Assert.Equal((name, type), (message.Document!.FileName, message.Document.ContentType));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("sha256")]
    [InlineData("malformedHash")]
    public void A_spreadsheet_whose_file_cannot_be_downloaded_keeps_its_caption_and_its_name(string broken)
    {
        var inbound = MetaWebhook.Document("wamid.broken", WaId, Bsuid, SentAtUtc, "clientes.csv", "text/csv", "Esta es la lista");
        var document = (JsonObject)inbound["document"]!;
        if (broken == "malformedHash") document["sha256"] = "not-a-hash";
        else document.Remove(broken);

        var message = Read(inbound);

        Assert.Equal(WhatsAppMessageKind.Spreadsheet, message.Kind);
        Assert.Equal("Esta es la lista", message.Body);
        Assert.Equal("clientes.csv", message.Spreadsheet!.FileName);
        Assert.True(message.Spreadsheet.MediaId is null || message.Spreadsheet.Sha256 is null);
    }

    private static WhatsAppWebhookMessage Read(JsonObject inbound)
    {
        var reader = new WhatsAppWebhookReader(Options.Create(new WhatsAppOptions { PhoneNumberId = ApiFactory.WhatsAppPhoneNumberId, AccessToken = "fictional-key" }),
            NullLogger<WhatsAppWebhookReader>.Instance);
        return Assert.Single(reader.Read(MetaWebhook.Build(contacts: [MetaWebhook.Contact(WaId, Bsuid, "Ana")], messages: [inbound])).Messages);
    }
}
