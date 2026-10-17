using ArquitecturaBase.Application.Interfaces.Persistence;
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
}
