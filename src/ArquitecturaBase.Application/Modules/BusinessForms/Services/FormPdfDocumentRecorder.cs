using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormPdfDocumentRecorder(IFormDocumentRepository documents, IStoredFileRepository files)
{
    public Task AddAsync(Guid definitionId, Guid recordId, FormPdfActionSnapshotResponse snapshot,
        FormPdfActionOutcomeResponse outcome, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (outcome.State != FormPdfActionState.Completed || outcome.Artifacts.Count != snapshot.Cultures.Count
            || !outcome.Artifacts.Select(artifact => artifact.Culture).Order().SequenceEqual(snapshot.Cultures.Order()))
            throw new InvalidOperationException("Only a confirmed complete PDF effect can be recorded.");
        foreach (var artifact in outcome.Artifacts)
        {
            var file = StoredFile.Create(snapshot.Author.UserId, artifact.Name, artifact.ContentType,
                artifact.Size, artifact.Sha256, artifact.StorageKey, null);
            files.Add(file);
            documents.Add(BusinessFormDocument.Create(definitionId, recordId, snapshot.RunId,
                file.Id, artifact.Culture, snapshot.DesignId, snapshot.DesignRevision));
        }
        return Task.CompletedTask;
    }

    /// <summary>The fiscal PDF an issuer stored, recorded as a Spanish document of the response, without a design.</summary>
    public void AddIssued(Guid definitionId, Guid recordId, Guid runId, Guid ownerUserId, StructuredRecordActionDocument document)
    {
        var file = StoredFile.Create(ownerUserId, document.Name, document.ContentType, document.Size, document.Sha256, document.StorageKey, null);
        files.Add(file);
        documents.Add(BusinessFormDocument.Create(definitionId, recordId, runId, file.Id, "es", null, 0));
    }
}
