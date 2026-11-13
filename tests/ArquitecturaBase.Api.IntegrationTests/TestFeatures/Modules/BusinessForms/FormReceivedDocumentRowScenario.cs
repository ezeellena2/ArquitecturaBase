using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

// VC1-A, task 28: a spreadsheet a channel registers through the rows port of the inbox, with its copy in the channel's store and the mapping of
// forms.documents.propose-rows@1 on its sample (amount in A, operation in B), as the engine returns it. Nothing calls a provider.
internal static class FormReceivedDocumentRowScenario
{
    public const string Sheet = "Cobranzas";
    public const string MediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>A book of one sheet: the headers in its first row and each row of data after them; an empty text leaves its cell empty.</summary>
    public static byte[] Workbook(params (string Amount, string Operation)[] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(Sheet);
        sheet.Cell(1, 1).Value = "Monto";
        sheet.Cell(1, 2).Value = "Operación";
        for (var index = 0; index < rows.Length; index++)
        {
            if (rows[index].Amount.Length > 0) sheet.Cell(index + 2, 1).Value = rows[index].Amount;
            if (rows[index].Operation.Length > 0) sheet.Cell(index + 2, 2).Value = rows[index].Operation;
        }
        using var content = new MemoryStream();
        workbook.SaveAs(content, new SaveOptions { EvaluateFormulasBeforeSaving = false });
        return content.ToArray();
    }

    /// <summary>
    /// A book with its sheets in their order, the headers in the first row of each: per cell a text, a number or an error (#N/A, #REF!), or
    /// null to leave it empty.
    /// </summary>
    public static byte[] Book(params (string Name, object?[][] Rows)[] sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var (name, rows) in sheets)
        {
            var sheet = workbook.AddWorksheet(name);
            for (var row = 0; row < rows.Length; row++)
                for (var column = 0; column < rows[row].Length; column++)
                    switch (rows[row][column])
                    {
                        case string text: sheet.Cell(row + 1, column + 1).Value = text; break;
                        case double number: sheet.Cell(row + 1, column + 1).Value = number; break;
                        case XLError error: sheet.Cell(row + 1, column + 1).Value = error; break;
                    }
        }
        using var content = new MemoryStream();
        workbook.SaveAs(content, new SaveOptions { EvaluateFormulasBeforeSaving = false });
        return content.ToArray();
    }

    /// <summary>The engine's mapping: the rows of the form (amount A, operation B), the ones that fit none and the ones it saw, from its read.</summary>
    public static AssistantObservedProposal Proposal(FormReceivedDocumentScenarioForm form, Guid reader, IReadOnlyList<int> rows, IReadOnlyList<int> unmatched,
        IReadOnlyList<int> seen, bool formRead = false) =>
        Proposal([(form, Sheet, rows)], reader, [(Sheet, seen)], unmatched.Count == 0 ? null : [(Sheet, unmatched)], formRead);

    /// <summary>The engine's mapping of the rows of each form in each sheet (amount A, operation B), from one read of the reader with those forms.</summary>
    public static AssistantObservedProposal Proposal(IReadOnlyList<(FormReceivedDocumentScenarioForm Form, string Sheet, IReadOnlyList<int> Rows)> forms, Guid reader,
        IReadOnlyList<(string Sheet, IReadOnlyList<int> Rows)> seen, IReadOnlyList<(string Sheet, IReadOnlyList<int> Rows)>? unmatched = null, bool formRead = false)
    {
        var read = new FormDocumentReadPayload("complete", reader, "es", true, [.. forms.Select(item => item.Form).DistinctBy(form => form.DefinitionId)
            .Select(form => new FormDocumentCandidate(form.DefinitionId, form.VersionId, form.Revision, form.Name, formRead ? null : "Un comprobante de pago.",
                [new(form.AmountId, "decimal", "Monto", true, null, [], null), new(form.OperationId, "shortText", "Operación", false, 40, [], null)]))]);
        var mapping = new FormDocumentRowsProposal("proposed", 1, [.. forms.Where(item => item.Rows.Count > 0).Select(item => new FormDocumentRowForm(item.Form.DefinitionId,
                item.Form.VersionId, item.Form.Revision, item.Sheet, item.Rows, [new(item.Form.AmountId, "decimal", "A"), new(item.Form.OperationId, "shortText", "B")], []))],
            [.. (unmatched ?? []).Select(set => new FormDocumentRowSet(set.Sheet, set.Rows))], [.. seen.Select(set => new FormDocumentRowSet(set.Sheet, set.Rows))],
            "planilla de cobranzas");
        var turn = Guid.CreateVersion7();
        return new(turn, Guid.CreateVersion7(), new(BusinessFormDocumentAssistantCapabilitySource.ProposeRowsId, BusinessFormDocumentAssistantCapabilitySource.Version),
            mapping)
        {
            ObservedReads = [new(turn, 1, new(formRead ? BusinessFormDocumentAssistantCapabilitySource.FormId : BusinessFormDocumentAssistantCapabilitySource.CandidatesId,
                BusinessFormDocumentAssistantCapabilitySource.Version), read)],
        };
    }

    /// <summary>The spreadsheet the sender sent now, registered by the port as a channel calls it; without a proposal, over the day's quota.</summary>
    public static async Task<(StructuredDocumentRowsRegistration Reply, StructuredDocumentRowsRegistrationRequest Request)> SendRowsAsync(
        this FormReceivedDocumentScenario scenario, byte[] content, AssistantObservedProposal? proposal, CancellationToken ct, DateTime? replyExpiresAtUtc = null)
    {
        var key = "channel/documents/" + Guid.NewGuid().ToString("N") + ".xlsx";
        scenario.Store.Put(key, content);
        var sha = FormReceivedDocumentScenario.Sha256(content);
        var request = new StructuredDocumentRowsRegistrationRequest(FormReceivedDocumentScenario.Channel, "wamid." + Guid.NewGuid().ToString("N"), scenario.Sender,
            "es", scenario.Factory.Clock.GetUtcNow().UtcDateTime, new(key, "cobranzas.xlsx", MediaType, content.LongLength, sha, null))
        {
            Proposal = proposal, SampleSha256 = proposal is null ? null : sha, NotRead = proposal is null ? StructuredDocumentNotRead.Quota : null,
            ReplyExpiresAtUtc = replyExpiresAtUtc,
        };
        return (await RegisterAsync(scenario, request, ct), request);
    }

    public static Task<StructuredDocumentRowsRegistration> RegisterAsync(this FormReceivedDocumentScenario scenario, StructuredDocumentRowsRegistrationRequest request,
        CancellationToken ct) =>
        scenario.ScopeAsync(services => services.GetRequiredService<IStructuredDocumentRowsChannel>().RegisterRowsAsync(request, ct));
}
