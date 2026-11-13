using System.Globalization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Task 9b of the documents: a response its person confirms in a channel from a received document (or a row of a spreadsheet) is checked
/// again at capture with what they corrected (point 12): the same values of every duplicate key of the form's standard as a response already
/// saved is a repetition, so two receipts sent together are not both saved. Runs in the capture's unit of work, after the document's lock,
/// and only reads. A reviewer who passes a document from the inbox decides it themselves: not checked.
/// </summary>
internal sealed class FormRecordRepetitionPolicy(IFormReceivedDocumentReader reader, FormDocumentProposalPolicy proposals)
{
    public async Task<Error?> FindAsync(StructuredRecordCaptureRequest request, FormVersionSchema schema, Guid documentId, CancellationToken ct)
    {
        var row = await reader.FindActiveFormAsync(request.DefinitionId, ct);
        if (row is null || proposals.Standard(row)?.Reading is not { DuplicateKeys.Count: > 0 } reading
            || FormDocumentProposalPolicy.Bindings(row) is not { } bound
            // What the capture stores (FormRecordAnswerPolicy, a text as it came), which the reader compares with what each response stored.
            || FormRecordAnswerPolicy.Normalize(schema, request.Answers, request.Catalogs ?? []) is not { } answers) return null;
        var keys = new List<FormDocumentRepetitionKey>();
        foreach (var datum in reading.DuplicateKeys)
        {
            // Every key answered, like at classification: without one of them there is nothing to compare.
            if (!bound.TryGetValue(datum, out var fieldId)
                || answers.FirstOrDefault(item => item.FieldId == fieldId) is not { Type: "shortText" or "longText" or "decimal" or "date" } answer) return null;
            keys.Add(new(fieldId, answer.Type, answer.Type switch
            {
                "decimal" => answer.Decimal!.Value.ToString("0.############################", CultureInfo.InvariantCulture),
                "date" => answer.Date!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => answer.Text!,
            }));
        }
        var found = (await reader.ListRepetitionsAsync(request.DefinitionId, null, keys, documentId, ct)).MinBy(item => item.AtUtc);
        return found is null ? null : FormRecordErrors.RepeatedOf(
            found.Number is { } number ? row.Prefix + "-" + number.ToString("D6", CultureInfo.InvariantCulture) : null, found.AtUtc);
    }

    /// <summary>The response a rejected capture repeats, for the channel to say it; null for any other rejection.</summary>
    public static StructuredRecordRepetition? Describe(Error error) =>
        error.Code == FormRecordErrors.Repeated.Code && error.Metadata?.GetValueOrDefault("atUtc") is DateTime atUtc
            ? new(error.Metadata.GetValueOrDefault("recordNumber") as string, atUtc) : null;
}
