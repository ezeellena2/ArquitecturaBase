using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// VC1-A (task 29): the notice of the loads of a spreadsheet, the footer of a load, «Por qué lo propuso» and the loading turn's capabilities.
public sealed class WhatsAppSpreadsheetLoadTests
{
    private static readonly StructuredDocumentFormChoice Collections = new(Guid.NewGuid(), Guid.NewGuid(), "Cobranzas");
    private static readonly StructuredDocumentFormChoice Expenses = new(Guid.NewGuid(), Guid.NewGuid(), "Gastos");
    private static readonly DateTime Day = new(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_notice_names_the_form_the_first_loads_a_repeated_row_and_the_ones_for_the_team()
    {
        var registration = Rows(Load(2, 1), Load(3, 2), Load(4, 3), Row(5, StructuredDocumentRowKind.OverLimit, Collections),
            Row(6, StructuredDocumentRowKind.Repeated, Collections) with { Duplicate = new(new DateTime(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc), "COB-000012") },
            Row(7, StructuredDocumentRowKind.Unmatched), Row(8, StructuredDocumentRowKind.NotSendable, Collections));

        Assert.Equal("Recibí una planilla con 7 filas de «Cobranzas». Te preparé un formulario para cada una de las 3 primeras: revisalos y confirmalos. "
            + "La fila 6 ya se cargó el 10/11/2026 en COB-000012: no la cargo de nuevo. Las otras 3 se las pasé a una persona del equipo.",
            WhatsAppAssistantSpreadsheetPolicy.Notice(registration, "UTC", "es"));
        Assert.Equal("I got a spreadsheet with 7 rows of “Cobranzas”. I prepared a form for each of the first 3: check them and confirm them. "
            + "Row 6 was already loaded on 11/10/2026 in COB-000012: I won't load it again. I passed the other 3 to a person on the team.",
            WhatsAppAssistantSpreadsheetPolicy.Notice(registration, "UTC", "en"));
    }

    [Fact]
    public void Rows_of_two_forms_say_only_how_many_and_the_variants_follow_what_went()
    {
        Assert.Equal("Recibí una planilla con 3 filas. Te preparé un formulario por cada una: revisalos y confirmalos. La otra se la pasé a una persona del equipo.",
            WhatsAppAssistantSpreadsheetPolicy.Notice(Rows(Load(2, 1), Load(3, 2) with { Form = Expenses }, Row(4, StructuredDocumentRowKind.Unseen)), "UTC", "es"));
        Assert.Equal("Recibí una planilla con 1 fila de «Cobranzas». Te preparé el formulario: revisalo y confirmalo.",
            WhatsAppAssistantSpreadsheetPolicy.Notice(Rows(Load(2, 1)), "UTC", "es"));
        Assert.Equal("I got a spreadsheet with 1 row of “Cobranzas”. I prepared the form: check it and confirm it.",
            WhatsAppAssistantSpreadsheetPolicy.Notice(Rows(Load(2, 1)), "UTC", "en"));
    }

    // Rule B4: each repeated row says where it is, the response it repeats or the row of the same spreadsheet it is equal to.
    [Fact]
    public void Each_repeated_row_says_where_it_is_when_rows_equal_to_others_and_rows_already_loaded_mix()
    {
        var registration = Rows(Load(2, 1) with { Sheet = "Ventas" },
            Row(3, StructuredDocumentRowKind.Repeated, Collections) with { Sheet = "Ventas", SameAs = new("Ventas", 2) },
            Row(4, StructuredDocumentRowKind.Repeated, Collections) with { Sheet = "Ventas", Duplicate = new(Day, "COB-000013") },
            Row(5, StructuredDocumentRowKind.Repeated, Collections) with { Sheet = "Ventas", Duplicate = new(Day, null) });

        Assert.Equal("Recibí una planilla con 4 filas de «Cobranzas». Te preparé el formulario: revisalo y confirmalo. La fila 3 (Ventas) es igual a la fila 2 "
            + "(Ventas): no la cargo de nuevo. La fila 4 (Ventas) ya se cargó el 10/11/2026 en COB-000013: no la cargo de nuevo. La fila 5 (Ventas) ya se cargó: "
            + "no la cargo de nuevo.", WhatsAppAssistantSpreadsheetPolicy.Notice(registration, "UTC", "es"));
        Assert.Equal("I got a spreadsheet with 4 rows of “Cobranzas”. I prepared the form: check it and confirm it. Row 3 (Ventas) is the same as row 2 (Ventas): "
            + "I won't load it again. Row 4 (Ventas) was already loaded on 11/10/2026 in COB-000013: I won't load it again. Row 5 (Ventas) was already loaded: "
            + "I won't load it again.", WhatsAppAssistantSpreadsheetPolicy.Notice(registration, "UTC", "en"));
    }

    // Decision 10: a spreadsheet alone that only loads and closes without a proposal still goes to the inbox, which offers its forms.
    [Fact]
    public void A_spreadsheet_alone_that_closes_without_a_proposal_goes_to_the_inbox_and_one_with_a_request_does_not()
    {
        var context = new AssistantChannelContext("whatsapp-assistant", Guid.NewGuid(), Guid.NewGuid(), "es", "UTC", new([], []),
            [new("forms.assistant-candidates", 1), new("forms.propose-registration", 1), new("forms.clarify-registration", 1), new("whatsapp.no-reply", 1)],
            [new("assistant_proposal", 1)]);
        var alone = Reference(WhatsAppAssistantAccessPolicy.ForSpreadsheet(context));
        var requested = Reference(WhatsAppAssistantAccessPolicy.WithSpreadsheet(context));

        Assert.True(WhatsAppAssistantTerminalPolicy.HandlesDocumentWithoutProposal(alone, new(alone.RequestId, "completed", null, null, null)));
        Assert.False(WhatsAppAssistantTerminalPolicy.HandlesDocumentWithoutProposal(requested, new(requested.RequestId, "completed", null, null, null)));
    }

    private static WhatsAppAssistantReferenceRow Reference(AssistantChannelContext context) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        context.ActorUserId, context.SubjectUserId!.Value, WhatsAppAssistantRequestKind.Automatic, 1, 1, JsonSerializer.Serialize(context, JsonSerializerOptions.Web),
        Day.AddDays(1), WhatsAppAssistantRequestState.Pending, null, null, null) { AttachmentMessageId = Guid.NewGuid() };

    [Fact]
    public void The_footer_names_the_form_within_metas_sixty_characters()
    {
        Assert.Equal("Cobranzas · prellenado", WhatsAppAssistantSpreadsheetPolicy.Footer("Cobranzas", "es"));
        Assert.Equal("Cobranzas · prefilled", WhatsAppAssistantSpreadsheetPolicy.Footer("Cobranzas", "en"));
        var footer = WhatsAppAssistantSpreadsheetPolicy.Footer(new string('a', 70), "es");
        Assert.Equal(60, footer.Length);
        Assert.EndsWith("… · prellenado", footer, StringComparison.Ordinal);
    }

    [Fact]
    public void Why_it_proposed_keeps_the_ids_and_is_read_with_the_names_of_now_each_row_in_one_place()
    {
        var registration = Rows(Load(2, 1), Load(3, 2) with { Form = Expenses }, Row(4, StructuredDocumentRowKind.OverLimit, Collections),
            Row(5, StructuredDocumentRowKind.Repeated, Collections) with { SameAs = new(null, 2) },
            Row(6, StructuredDocumentRowKind.Repeated, Collections) with { Duplicate = new(Day, "COB-000012") },
            Row(7, StructuredDocumentRowKind.Repeated, Collections) with { Duplicate = new(Day, null) }, Row(8, StructuredDocumentRowKind.Unmatched));

        var json = WhatsAppSpreadsheetRowsCodec.Serialize(registration);
        var read = WhatsAppSpreadsheetRowsCodec.Read(json, new Dictionary<Guid, string> { [Collections.DefinitionId] = "Cobranzas del mes" })!;

        Assert.Contains(Collections.DefinitionId.ToString(), json, StringComparison.Ordinal);
        Assert.Equal([("Cobranzas del mes", 5), ("Gastos", 1)], read.Recognized.Select(form => (form.Form, form.Count)));
        Assert.Equal([2, 3], read.Sent.Select(row => row.Number));
        Assert.Equal([4, 8], read.ForTeam.Select(row => row.Number));
        Assert.Equal([(5, null, 2), (6, "COB-000012", null)], read.Repeated.Select(entry => (entry.Row.Number, entry.RecordNumber, entry.SameAs?.Number)));
        Assert.Null(WhatsAppSpreadsheetRowsCodec.Serialize(Rows(Row(2, StructuredDocumentRowKind.Unmatched))));
        Assert.Null(WhatsAppSpreadsheetRowsCodec.Read("{", new Dictionary<Guid, string>()));
        Assert.Equal("""{"sheet":null,"number":2}""", JsonSerializer.Serialize(read.Sent[0], JsonSerializerOptions.Web));
    }

    [Fact]
    public void The_loading_turn_swaps_the_three_text_registrations_for_the_loading_ones_and_alone_only_loads()
    {
        var context = new AssistantChannelContext("whatsapp-assistant", Guid.NewGuid(), Guid.NewGuid(), "es", "UTC", new([], []),
            [new("forms.assistant-candidates", 1), new("forms.propose-registration", 1), new("forms.clarify-registration", 1), new("whatsapp.no-reply", 1)],
            [new("assistant_proposal", 1)]);
        var agent = WhatsAppAssistantAccessPolicy.WithAgent(context, true);

        Assert.Equal([new("whatsapp.no-reply", 1), .. WhatsAppAssistantAccessPolicy.SpreadsheetCapabilities],
            WhatsAppAssistantAccessPolicy.WithSpreadsheet(context).AllowedCapabilities);
        var loading = WhatsAppAssistantAccessPolicy.WithSpreadsheet(agent);
        Assert.DoesNotContain(new AssistantCapabilityReference("forms.assistant-candidates", 1), loading.PreloadedCapabilities!);
        Assert.Equal(WhatsAppAssistantAccessPolicy.SpreadsheetCapabilities, loading.AllowedCapabilities.TakeLast(2));
        var alone = WhatsAppAssistantAccessPolicy.ForSpreadsheet(agent);
        Assert.Equal((WhatsAppAssistantAccessPolicy.SpreadsheetCapabilities, (IReadOnlyList<AssistantCapabilityReference>?)null, 1),
            (alone.AllowedCapabilities, alone.PreloadedCapabilities, alone.AllowedTerminals.Single().Version));
    }

    private static StructuredDocumentRowsRegistration Rows(params StructuredDocumentRowRegistration[] rows) => new(StructuredDocumentReplyKind.Sure) { Rows = rows };

    private static StructuredDocumentRowRegistration Load(int number, int position) =>
        new(Guid.NewGuid(), null, number, StructuredDocumentRowKind.Load) { Form = Collections, Position = position, Summary = ["$ 95.000"] };

    private static StructuredDocumentRowRegistration Row(int number, StructuredDocumentRowKind kind, StructuredDocumentFormChoice? form = null) =>
        new(Guid.NewGuid(), null, number, kind) { Form = form };
}
