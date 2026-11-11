using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

/// <summary>
/// «¿La emito?» as it leaves the server: the summary carries the receiver, the total and the amounts, so no log prints them; and
/// the date of the document, a day without time, travels as yyyy-MM-dd, the same bytes the runs stored while it was a string.
/// </summary>
public sealed class FormIssueContractTests
{
    private static readonly DateOnly Day = new(2026, 11, 9);

    private static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };

    [Fact]
    public void The_workflow_its_confirmation_and_the_preview_do_not_print_the_receiver_nor_the_amounts()
    {
        var confirmation = new FormIssueConfirmationResponse(Guid.NewGuid(), Text("Factura A", "Invoice A"), "Agro del Sur S.A.", "1210")
        {
            Date = Day,
            Amounts = [new(Text("IVA 21 %", "VAT 21%"), "210")],
        };
        var workflow = new FormWorkflowResponse(Guid.NewGuid(), "Received", null, null, [], []) { Confirmation = confirmation };
        var preview = new FormIssuePreviewResponse(Text("Factura A", "Invoice A"), "Agro del Sur S.A.", "1210") { Date = Day };

        Assert.Equal("FormIssueConfirmationResponse { Content = [REDACTED] }", confirmation.ToString());
        Assert.Equal("FormWorkflowResponse { Content = [REDACTED] }", workflow.ToString());
        Assert.Equal("FormIssuePreviewResponse { Content = [REDACTED] }", preview.ToString());
    }

    [Fact]
    public void The_date_of_the_document_travels_as_yyyy_MM_dd_and_is_left_out_without_one()
    {
        var issuer = new StructuredIssuePreview(Text("Factura C", "Invoice C"), "Juana Pérez", "605500") { Date = Day };
        var preview = new FormIssuePreviewResponse(issuer.Document, issuer.Receiver, issuer.Total) { Date = Day };
        var confirmation = new FormIssueConfirmationResponse(Guid.NewGuid(), issuer.Document, issuer.Receiver, issuer.Total) { Date = Day };

        Assert.Equal("2026-11-09", Json(issuer).GetProperty("date").GetString());
        Assert.Equal("2026-11-09", Json(preview).GetProperty("date").GetString());
        Assert.Equal("2026-11-09", Json(confirmation).GetProperty("date").GetString());
        Assert.False(Json(new StructuredIssuePreview(null, null, null, "Arca.Registry.PersonNotFound")).TryGetProperty("date", out _));
    }

    // A run keeps the summary it asked about in its outcome: one stored while the date was a string reads the same day.
    [Fact]
    public void A_summary_stored_with_the_date_as_text_reads_the_same_day()
    {
        const string stored = """{"document":{"es":"Factura C","en":"Invoice C"},"receiver":"Juana Pérez","total":"605500","date":"2026-11-09"}""";

        var preview = FormDraftPolicy.Read<StructuredIssuePreview>(stored);

        Assert.Equal(Day, preview.Date);
        Assert.Contains("\"date\":\"2026-11-09\"", FormDraftPolicy.Serialize(new FormActionOutcomeResponse(
            ArquitecturaBase.Domain.Modules.BusinessForms.FormActionRunState.Uncertain, IssuePreview: preview)), StringComparison.Ordinal);
    }

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web);
}
