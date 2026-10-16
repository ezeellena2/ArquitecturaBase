using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormPdfDocumentRecorder(IFormDocumentRepository documents, IStoredFileRepository files)
{
    public async Task AddAsync(FormPdfActionSnapshotResponse snapshot, FormPdfActionOutcomeResponse outcome, CancellationToken cancellationToken)
    {
        if (outcome.State != FormPdfActionState.Completed || outcome.Artifacts.Count != snapshot.Cultures.Count
            || !outcome.Artifacts.Select(artifact => artifact.Culture).Order().SequenceEqual(snapshot.Cultures.Order()))
            throw new InvalidOperationException("Only a confirmed complete PDF effect can be recorded.");
        var existing = (await documents.ListForRunAsync(snapshot.RunId, cancellationToken)).Select(document => document.Culture).ToHashSet(StringComparer.Ordinal);
        foreach (var artifact in outcome.Artifacts)
        {
            if (!existing.Add(artifact.Culture)) continue;
            var file = StoredFile.Create(snapshot.Record.Response.Actor.UserId, artifact.Name, artifact.ContentType,
                artifact.Size, artifact.Sha256, artifact.StorageKey, null);
            files.Add(file);
            documents.Add(BusinessFormDocument.Create(snapshot.Record.Response.Record.DefinitionId,
                snapshot.Record.Response.Record.RecordId, snapshot.RunId, file.Id, artifact.Culture, snapshot.DesignId, snapshot.DesignRevision));
        }
    }
}
