using System.Security.Cryptography;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormPdfActionService(IFormPdfDesignReader reader, IPrivateFileStore store, IFormPdfRenderer renderer,
    IAppName appName, IFormResponseReader records, ILogger<FormPdfActionService> logger) : IFormPdfActionService
{
    public Task<Result<FormPdfActionSnapshotResponse>> PrepareAsync(Guid runId, FormActionRecordSnapshot record, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormPdfActionSnapshotResponse>>(logger, "PrepareFormPdfAction", async () =>
        {
            var captured = record.Response.Record;
            var current = await reader.FindCurrentAsync(captured.DefinitionId, cancellationToken);
            var schema = FormDraftPolicy.Read<FormVersionSchema>(captured.SchemaJson);
            var design = FormPdfDesignPolicy.Resolve(current, captured, schema);
            var original = current?.OriginalCulture ?? record.OriginalCulture;
            var cultures = FormPdfDesignPolicy.CompleteCultures(design, original, schema, captured.NameJson)
                .Intersect(FormRecordReferencePolicy.CompleteCultures(record, design.QuestionIds.ToHashSet())).ToArray();
            return new FormPdfActionSnapshotResponse(runId, current?.Id, current?.Revision ?? 0, design,
                cultures, appName.Value, record.Response.Actor, record.Decision);
        });

    public Task<Result<FormPdfActionOutcomeResponse>> ExecuteAsync(Guid recordId, FormPdfActionSnapshotResponse snapshot,
        bool reconciliationOnly, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormPdfActionOutcomeResponse>>(logger, "ExecuteFormPdfAction", async () =>
        {
            if (!store.IsAvailable) return new FormPdfActionOutcomeResponse(FormPdfActionState.Failed, [], FileErrors.Unavailable.Code);
            var record = await records.FindRecordAsync(recordId, cancellationToken);
            if (record is null) return FormRecordErrors.NotFound;
            record = record with { Actor = snapshot.Author };
            var artifacts = new List<FormPdfArtifact>();
            foreach (var culture in snapshot.Cultures)
            {
                var artifact = await FindAsync(snapshot, record.Record, culture, cancellationToken);
                if (artifact is null && !reconciliationOnly)
                {
                    var request = new FormPdfRenderRequest(snapshot, FormRecordReferencePolicy.Localize(record, culture));
                    var bytes = await renderer.RenderAsync(request, culture, false, cancellationToken);
                    await using var content = new MemoryStream(bytes, false);
                    await store.WriteOnceAsync(FormPdfStorageKey.Create(snapshot.RunId, culture), content, "application/pdf", cancellationToken);
                    artifact = await FindAsync(snapshot, record.Record, culture, cancellationToken)
                        ?? throw new InvalidOperationException("The PDF upload could not be confirmed.");
                }
                if (artifact is not null) artifacts.Add(artifact);
            }
            return new FormPdfActionOutcomeResponse(artifacts.Count == snapshot.Cultures.Count
                ? FormPdfActionState.Completed : FormPdfActionState.NotFound, artifacts);
        });

    private async Task<FormPdfArtifact?> FindAsync(FormPdfActionSnapshotResponse snapshot, FormRecordStoredRow record,
        string culture, CancellationToken cancellationToken)
    {
        var key = FormPdfStorageKey.Create(snapshot.RunId, culture);
        await using var existing = await store.OpenReadAsync(key, cancellationToken);
        if (existing is null) return null;
        using var content = new MemoryStream();
        await existing.CopyToAsync(content, cancellationToken);
        return new(culture, key, Name(snapshot, record, culture), "application/pdf", content.Length,
            Convert.ToHexStringLower(SHA256.HashData(content.GetBuffer().AsSpan(0, checked((int)content.Length)))));
    }

    private static string Name(FormPdfActionSnapshotResponse snapshot, FormRecordStoredRow record, string culture)
    {
        var title = string.Concat(snapshot.Design.Name[culture].Select(character => "<>:\"/\\|?*".Contains(character, StringComparison.Ordinal)
            ? ' ' : character)).Trim(' ', '.');
        return title + "-" + record.Prefix + "-" + record.Number.ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + ".pdf";
    }
}
