using System.Globalization;
using System.Security.Cryptography;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// VC1-A, task 27, with PostgreSQL: a row of a spreadsheet is repeated only by the data of a response (the standard's duplicate keys), never
// by a file, which the rows share with their spreadsheet and the documents of the inbox keep comparing; the mapping of a copy of «Comprobante
// de transferencia» finds it with the real reader and the standard the module brings.
[Collection(ApiTestGroup.Name)]
public sealed class FormDocumentRowRepetitionTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_a_file_the_repetitions_are_only_the_responses_with_the_same_data()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var transfer = await scenario.TransferAsync(Ct);
        var number = await scenario.RespondAsync(transfer, "00112233", 150000m, new DateOnly(2026, 11, 11), Ct);
        // A document of the inbox for the copy with other data and its own file.
        var sent = await scenario.SendAsync(Ct, proposal: scenario.TransferProposal(transfer, "44556677", "150000", "2026-11-11"));

        var (repeated, other, document) = await scenario.ScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IFormReceivedDocumentReader>();
            return (await reader.ListRepetitionsAsync(transfer.DefinitionId, null, Keys(transfer, "00112233"), null, Ct),
                await reader.ListRepetitionsAsync(transfer.DefinitionId, null, Keys(transfer, "44556677"), null, Ct),
                await reader.ListRepetitionsAsync(transfer.DefinitionId, Convert.ToHexStringLower(SHA256.HashData(sent.Content)), Keys(transfer, "44556677"), null, Ct));
        });

        var response = Assert.Single(repeated);
        Assert.Equal((number, (Guid?)null), (transfer.Prefix + "-" + response.Number!.Value.ToString("D6", CultureInfo.InvariantCulture), response.DocumentId));
        Assert.Empty(other);
        Assert.Equal(sent.Id, Assert.Single(document).DocumentId);
    }

    [Fact]
    public async Task The_mapping_of_a_spreadsheet_loads_the_new_rows_and_leaves_out_the_one_a_response_already_has()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);
        var transfer = await scenario.TransferAsync(Ct);
        var number = await scenario.RespondAsync(transfer, "00112233", 150000m, new DateOnly(2026, 11, 11), Ct);
        var rows = FormDocumentRowPolicy.DataRows([new FormSpreadsheet("Hoja1", new string('a', 64), [
            Row(1, "Operación", "Monto", "Fecha"), Row(2, "00112233", "150000", "2026-11-11"), Row(3, "44556677", "150.000,00", "11/11/2026")])]);
        var proposal = new FormDocumentRowsProposal("proposed", 1, [new(transfer.DefinitionId, transfer.VersionId, transfer.Revision, "Hoja1", [2, 3],
            [new(transfer.OperationId, "shortText", "A"), new(transfer.AmountId, "decimal", "B"), new(transfer.DateId, "date", "C")], [])],
            [], [new("Hoja1", [2, 3])], "planilla de transferencias");

        var plan = await scenario.ScopeAsync(services => services.GetRequiredService<FormDocumentRowPolicy>().PrepareAsync(proposal, rows, "es", Ct));

        Assert.Equal([FormDocumentRowOutcome.Repeated, FormDocumentRowOutcome.Load], plan.Rows.Select(row => row.Outcome));
        Assert.Equal(number, plan.Rows[0].Repetition!.RecordNumber);
        Assert.Equal(["44556677", "150000", "2026-11-11"], plan.Rows[1].Candidate!.Values.Select(value => value.Value));
    }

    private static FormDocumentRepetitionKey[] Keys(FormReceivedDocumentScenarioTransfer transfer, string operation) =>
        [new(transfer.OperationId, "shortText", operation), new(transfer.AmountId, "decimal", "150000"), new(transfer.DateId, "date", "2026-11-11")];

    private static FormSpreadsheetRow Row(int number, params string[] cells) => new(number, [.. cells.Select(text => new FormSpreadsheetCell(text))]);
}
