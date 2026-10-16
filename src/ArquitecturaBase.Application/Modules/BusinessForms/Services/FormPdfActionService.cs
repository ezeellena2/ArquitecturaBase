using System.Security.Cryptography;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormPdfActionService(IFormPdfDesignReader reader, IPrivateFileStore store, IFormPdfRenderer renderer,
    IAppName appName, ILogger<FormPdfActionService> logger) : IFormPdfActionService
{
    public Task<Result<FormPdfActionSnapshotResponse>> PrepareAsync(Guid runId, FormActionRecordSnapshot record, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormPdfActionSnapshotResponse>>(logger, "PrepareFormPdfAction", async () =>
        {
            var current = await reader.FindCurrentAsync(record.Response.Record.DefinitionId, cancellationToken);
            var schema = FormDraftPolicy.Read<FormVersionSchema>(record.Response.Record.SchemaJson);
            var design = current is null ? new FormPdfDesign(FormDraftPolicy.Read<Dictionary<string,string>>(record.Response.Record.NameJson),
                schema.Questions.Select(question => question.FieldId).ToArray(), true, null, true) : FormDraftPolicy.Read<FormPdfDesign>(current.DesignJson);
            var original = current?.OriginalCulture ?? record.OriginalCulture;
            return new FormPdfActionSnapshotResponse(runId, current?.Id, current?.Revision ?? 0, design, record,
                FormPdfDesignPolicy.CompleteCultures(design, original, schema), appName.Value);
        });
    public Task<Result<FormPdfActionOutcomeResponse>> ExecuteAsync(FormPdfActionSnapshotResponse snapshot, bool reconciliationOnly, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormPdfActionOutcomeResponse>>(logger, "ExecuteFormPdfAction", async () =>
        {
            if (!store.IsAvailable) return new FormPdfActionOutcomeResponse(FormPdfActionState.Failed, [], FileErrors.Unavailable.Code);
            var artifacts = new List<FormPdfArtifact>();
            foreach (var culture in snapshot.Cultures)
            {
                var artifact = await FindAsync(snapshot, culture, cancellationToken);
                if (artifact is null && !reconciliationOnly)
                {
                    var bytes = await renderer.RenderAsync(snapshot, culture, false, cancellationToken);
                    await using var content = new MemoryStream(bytes, false);
                    await store.WriteOnceAsync(FormPdfStorageKey.Create(snapshot.RunId, culture), content, "application/pdf", cancellationToken);
                    artifact = await FindAsync(snapshot, culture, cancellationToken)
                        ?? throw new InvalidOperationException("The PDF upload could not be confirmed.");
                }
                if (artifact is not null) artifacts.Add(artifact);
            }
            return new FormPdfActionOutcomeResponse(artifacts.Count == snapshot.Cultures.Count ? FormPdfActionState.Completed : FormPdfActionState.NotFound, artifacts);
        });
    private async Task<FormPdfArtifact?> FindAsync(FormPdfActionSnapshotResponse snapshot, string culture, CancellationToken cancellationToken)
    {
        var key = FormPdfStorageKey.Create(snapshot.RunId, culture);
        await using var existing = await store.OpenReadAsync(key, cancellationToken);
        if (existing is null) return null;
        using var content = new MemoryStream(); await existing.CopyToAsync(content, cancellationToken);
        return new(culture, key, Name(snapshot, culture), "application/pdf", content.Length, Convert.ToHexStringLower(SHA256.HashData(content.GetBuffer().AsSpan(0, checked((int)content.Length)))));
    }
    private static string Name(FormPdfActionSnapshotResponse snapshot, string culture)
    {
        var title = string.Concat(snapshot.Design.Name[culture].Select(character => "<>:\"/\\|?*".Contains(character, StringComparison.Ordinal) ? ' ' : character)).Trim(' ', '.');
        var row = snapshot.Record.Response.Record;
        return title + "-" + row.Prefix + "-" + row.Number.ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + ".pdf";
    }
}
