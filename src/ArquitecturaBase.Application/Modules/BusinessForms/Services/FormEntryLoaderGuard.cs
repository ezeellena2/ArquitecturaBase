using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>Whoever loads, and whose data the catalog filters use: their own for a new response, the author's for a correction.</summary>
internal sealed record FormEntryLoader(Guid Actor, Guid Person, FormRecordAmendAccess? Amend);

/// <summary>
/// Who loads in the app: «Completar formularios» for a new response; for a correction (CW10-A), whoever may correct that response
/// (decision 8), in the name of whoever answered it. Checked before reading and again before answering.
/// </summary>
internal sealed class FormEntryLoaderGuard(FormEntryAccessGuard access, FormRecordAmendmentGuard amendments, IFormResponseReader responses,
    IFormRecordIssuanceReader issuances)
{
    public async Task<Result<FormEntryLoader>> EntryAsync(CancellationToken cancellationToken)
    {
        var actor = await access.CheckAsync(cancellationToken);
        return actor.IsFailure ? actor.Error : new FormEntryLoader(actor.Value, actor.Value, null);
    }

    public Task<Result<FormRecordAmendAccess>> AmendAsync(CancellationToken cancellationToken) => amendments.CheckAsync(cancellationToken);

    public async Task<Result<(FormEntryLoader Loader, FormResponseStoredRow Record)>> CorrectionAsync(Guid definitionId, Guid recordId,
        CancellationToken cancellationToken)
    {
        var amend = await amendments.CheckAsync(cancellationToken); if (amend.IsFailure) return amend.Error;
        var record = await responses.FindRecordAsync(recordId, cancellationToken);
        if (record is null || record.Record.DefinitionId != definitionId) return FormRecordErrors.NotFound;
        var allowed = FormRecordAmendmentGuard.Allows(amend.Value, record.Actor.UserId, Enum.Parse<FormRecordState>(record.State),
            record.ReplacedByRecordId is not null);
        if (allowed.IsFailure) return allowed.Error;
        // A response with an issued document, or one being issued, is voided and loaded again: never corrected.
        if (await issuances.ExistsLiveAsync(recordId, cancellationToken)) return FormRecordErrors.IssuedNotAmendable;
        return (new FormEntryLoader(amend.Value.Actor, record.Actor.UserId, amend.Value), record);
    }

    public Task<bool> StillAllowedAsync(FormEntryLoader loader, CancellationToken cancellationToken) =>
        loader.Amend is { } amend ? amendments.StillAllowedAsync(amend, cancellationToken) : access.StillAllowedAsync(loader.Actor, cancellationToken);
}
