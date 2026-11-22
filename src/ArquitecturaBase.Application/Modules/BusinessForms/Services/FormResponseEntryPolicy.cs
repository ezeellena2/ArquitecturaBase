using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The detail of a response as the app's form (VF1-A): its own version as the app loads it, with the answers and files in its shape,
/// and whether the person reading it may correct it there now (decision 8). Only reads; the detail checks who reads.
/// </summary>
internal sealed class FormResponseEntryPolicy(FormEntryVersionPolicy describer, FormEntryCatalogPolicy catalogs, FormRecordAmendmentGuard amendments)
{
    /// <summary>
    /// Described in the name of whoever answered, like a correction, but on the response's own version and projected on its own sealed
    /// form (<paramref name="schema"/>). That version is always there, also once the form is deactivated or published again.
    /// </summary>
    public async Task<FormResponseEntryResponse> DescribeAsync(FormResponseStoredRow row, FormVersionSchema schema, CancellationToken cancellationToken)
    {
        var described = await describer.DescribeVersionAsync(row.Record.DefinitionId, row.Record.VersionId, row.Actor.UserId, cancellationToken);
        if (described.IsFailure) throw new InvalidOperationException("The response version is missing.");
        var entry = described.Value.Entry;
        var values = FormRecordPresentationPolicy.Project(row.Record, entry.Culture, schema).Values;
        var answers = await catalogs.WithGroupsAsync(schema, FormEntryAnswerPolicy.Answers(schema, schema, row.Record, values, entry.Culture),
            entry.Culture, cancellationToken);
        return new(entry, answers, FormEntryAnswerPolicy.Files(schema, row.Record));
    }

    public async Task<bool> CanAmendAsync(FormResponseStoredRow row, CancellationToken cancellationToken) =>
        await amendments.FindAsync(cancellationToken) is { } amend && FormRecordAmendmentGuard.Allows(amend, row.Actor.UserId,
            Enum.Parse<FormRecordState>(row.State), row.ReplacedByRecordId is not null).IsSuccess;
}
