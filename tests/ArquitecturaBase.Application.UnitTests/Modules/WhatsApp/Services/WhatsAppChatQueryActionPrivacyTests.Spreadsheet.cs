using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// Stage 2 of the assistant authoring: the operator's chat shows a received spreadsheet as such, with its normalized name and its
// caption as the body; the list keeps a generic preview, without the name or the caption.
public sealed partial class WhatsAppChatQueryActionPrivacyTests
{
    private static WhatsAppMessageRow SpreadsheetRow(string? caption) => new(Guid.NewGuid(), WhatsAppMessageDirection.Inbound,
        WhatsAppMessageKind.Spreadsheet, caption, Now, 1, null, null, null, WhatsAppMessagePurpose.Business) { SpreadsheetName = "clientes.xlsx" };

    [Theory]
    [InlineData("Armame un catálogo con esta planilla")]
    [InlineData(null)]
    public async Task A_spreadsheet_shows_its_name_and_its_caption_as_the_body(string? caption)
    {
        var host = new Host();
        host.Reader.Audio = SpreadsheetRow(caption);
        var result = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);
        Assert.True(result.IsSuccess);
        var message = Assert.Single(result.Value.Messages);
        Assert.Equal((WhatsAppMessageKind.Spreadsheet, caption, "clientes.xlsx"), (message.Kind, message.Body, message.SpreadsheetName));
        Assert.Null(message.Audio);
        Assert.Contains("\"spreadsheetName\":\"clientes.xlsx\"", JsonSerializer.Serialize(message, JsonSerializerOptions.Web), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Any_other_message_keeps_its_shape_without_a_spreadsheet_name()
    {
        var host = new Host();
        var result = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);
        var message = Assert.Single(result.Value.Messages);
        Assert.Null(message.SpreadsheetName);
        Assert.DoesNotContain("spreadsheetName", JsonSerializer.Serialize(message, JsonSerializerOptions.Web), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("es", "Planilla")]
    [InlineData("en", "Spreadsheet")]
    public async Task Spreadsheet_list_preview_is_a_generic_label_without_its_name_or_caption(string culture, string preview)
    {
        using var scope = new CultureScope(culture);
        var host = new Host();
        host.Reader.SpreadsheetPreview = true;
        var result = await host.Service.ListAsync(new(), Guid.NewGuid(), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(preview, Assert.Single(result.Value.Page.Items).Preview);
    }
}
