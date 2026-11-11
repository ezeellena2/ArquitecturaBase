using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// What a completed action leaves in its response, in the transaction that confirms it under its fence: the PDF copies of
/// «Armar el PDF», the result of an issuance (RE1-A) with its fiscal PDF as one more document, and, when a reversal ends, the
/// voided response (NC1-A) with the reason and the person of the void. It does not save on its own.
/// </summary>
internal sealed class FormActionOutcomeRecorder(FormPdfDocumentRecorder documents, FormRecordResultRecorder results, IFormActionRunRepository runs,
    FormIssuedCatalogItemRecorder issuedItems, IEmailQueue emails)
{
    /// <summary>
    /// «SendCopy»: the copy by email leaves by the app's durable mail queue in the transaction that confirms its run, so it is queued
    /// once; a queue that does not take it leaves the run failed, to retry.
    /// </summary>
    public FormActionOutcomeResponse QueueCopy(FormActionOutcomeResponse outcome) =>
        outcome is { State: FormActionRunState.Completed, Email: { } email } && !emails.TryEnqueue(email)
            ? new(FormActionRunState.Failed, FormActionErrors.CopyNotQueued.Code, true) : outcome;

    /// <summary>
    /// «Emitir»: the party the issuer completed when it queued (a customer written by its tax id) joins the catalog of its
    /// standard, in the same transaction as the outcome of the run, whatever its state.
    /// </summary>
    public Task AddIssuedItemAsync(FormActionOutcomeResponse outcome, CancellationToken cancellationToken) =>
        outcome.Issue?.CatalogItem is { } item ? issuedItems.AddAsync(item, cancellationToken) : Task.CompletedTask;

    /// <summary>The fiscal PDF of an issuance, in Spanish, offered to a notice in both languages.</summary>
    public static IReadOnlyList<FormPdfArtifact> Artifacts(StructuredRecordActionDocument document) =>
        [new("es", document.StorageKey, document.Name, document.ContentType, document.Size, document.Sha256),
            new("en", document.StorageKey, document.Name, document.ContentType, document.Size, document.Sha256)];

    public async Task AddAsync(BusinessFormActionRun run, FormActionSnapshot snapshot, FormActionOutcomeResponse outcome, DateTime nowUtc,
        CancellationToken cancellationToken)
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
        if (snapshot.ReversesRunId is not null)
        {
            var record = await runs.GetRecordAsync(run.RecordId, cancellationToken) ?? throw new InvalidOperationException("The action response is missing.");
            var voided = record.Void(snapshot.VoidedByUserId!.Value, snapshot.VoidReason, nowUtc);
            if (voided.IsFailure) throw new InvalidOperationException("The response of a finished reversal could not be voided: " + voided.Error.Code);
            if (await runs.GetOpenTaskAsync(record.Id, cancellationToken) is { } task && task.Close() is { IsFailure: true } closed)
                throw new InvalidOperationException("The open approval task of the voided response could not close: " + closed.Error.Code);
        }
    }
}
