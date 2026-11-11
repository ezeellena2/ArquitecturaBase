using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// What a completed action leaves in its response, in the transaction that confirms it under its fence: the PDF copies of
/// «Armar el PDF», and the result of an issuance (RE1-A) with its fiscal PDF as one more document. It does not save on its own.
/// </summary>
internal sealed class FormActionOutcomeRecorder(FormPdfDocumentRecorder documents, FormRecordResultRecorder results, IFormActionRunRepository runs)
{
    /// <summary>The fiscal PDF of an issuance, in Spanish, offered to a notice in both languages.</summary>
    public static IReadOnlyList<FormPdfArtifact> Artifacts(StructuredRecordActionDocument document) =>
        [new("es", document.StorageKey, document.Name, document.ContentType, document.Size, document.Sha256),
            new("en", document.StorageKey, document.Name, document.ContentType, document.Size, document.Sha256)];

    public async Task AddAsync(BusinessFormActionRun run, FormActionSnapshot snapshot, FormActionOutcomeResponse outcome, CancellationToken cancellationToken)
    {
        if (outcome.Pdf is { } pdf)
            await documents.AddAsync(run.DefinitionId, run.RecordId, snapshot.Pdf ?? throw new InvalidOperationException("The PDF snapshot is missing."), pdf, cancellationToken);
        if (outcome.Issue?.Result is { } result)
            results.Add(run.DefinitionId, run.RecordId, run.Id, result);
        if (outcome.Issue?.Document is { } document)
        {
            var record = await runs.GetRecordAsync(run.RecordId, cancellationToken) ?? throw new InvalidOperationException("The action response is missing.");
            documents.AddIssued(run.DefinitionId, run.RecordId, run.Id, record.ActorUserId, document);
        }
    }
}
