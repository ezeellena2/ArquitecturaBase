using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// What a completed action leaves in its response, in the transaction that confirms it under its fence: the PDF copies of
/// «Armar el PDF» and the result of an issuance (RE1-A). It does not save on its own.
/// </summary>
internal sealed class FormActionOutcomeRecorder(FormPdfDocumentRecorder documents, FormRecordResultRecorder results)
{
    public async Task AddAsync(BusinessFormActionRun run, FormActionSnapshot snapshot, FormActionOutcomeResponse outcome, CancellationToken cancellationToken)
    {
        if (outcome.Pdf is { } pdf)
            await documents.AddAsync(run.DefinitionId, run.RecordId, snapshot.Pdf ?? throw new InvalidOperationException("The PDF snapshot is missing."), pdf, cancellationToken);
        if (outcome.Issue?.Result is { } result)
            results.Add(run.DefinitionId, run.RecordId, run.Id, result);
    }
}
