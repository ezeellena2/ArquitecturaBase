using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormDefinitionReadPolicy(IFormDefinitionReader reader, IFormLifecycleReader lifecycle, FormDraftAccessGuard access,
    IStructuredFormChannelStateSource channelState, TimeProvider clock)
{
    public async Task<Result<FormDefinitionDetailResponse>> FindAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var row = await reader.FindAsync(id, ct); if (row is null) return FormDefinitionErrors.NotFound;
        var canSave = await access.HasPermissionAsync(actor, BusinessFormsPermissions.Manage, ct);
        var canPublish = await access.HasPermissionAsync(actor, BusinessFormsPermissions.Publish, ct);
        var publication = await reader.FindPublicationAsync(id, canPublish ? actor : null, ct);
        var facts = (await lifecycle.ListFactsAsync([id], ct)).SingleOrDefault(value => value.DefinitionId == id);
        if (facts is null) return FormDefinitionErrors.NotFound;
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var state = (await channelState.ListAsync([id], nowUtc, ct)).SingleOrDefault(value => value.DefinitionId == id);
        var canReadRecords = await access.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsRead, ct);
        var current = await access.CheckAsync(false, ct);
        if (current.IsFailure || current.Value != actor) return FormDefinitionErrors.Forbidden;
        return new FormDefinitionDetailResponse(FormDraftPolicy.Definition(row.Id, row.NameJson, row.Prefix, row.PrefixLocked, row.Revision,
            row.Culture, row.SubmitLabelJson, row.SchemaJson, row.OfferWhen), new(canSave, canPublish, publication.ActiveVersion is not null),
            new(row.CreatedAtUtc, row.ModifiedAtUtc, row.LastChangedAtUtc, row.Author), publication,
            FormDefinitionLifecyclePolicy.View(facts, state, nowUtc, canSave, canPublish, canReadRecords));
    }
    public async Task<Result<PagedResult<FormDefinitionListResponse>>> ListAsync(ListFormDefinitionsRequest request, Guid actor, CancellationToken ct)
    {
        var page = await reader.ListAsync(request, ct);
        var facts = (await lifecycle.ListFactsAsync(page.Items.Select(row => row.Id).ToArray(), ct)).ToDictionary(row => row.DefinitionId);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var states = (await channelState.ListAsync(page.Items.Select(row => row.Id).ToArray(), nowUtc, ct)).ToDictionary(row => row.DefinitionId);
        var canSave = await access.HasPermissionAsync(actor, BusinessFormsPermissions.Manage, ct);
        var canPublish = await access.HasPermissionAsync(actor, BusinessFormsPermissions.Publish, ct);
        var canReadRecords = await access.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsRead, ct);
        var current = await access.CheckAsync(false, ct);
        if (current.IsFailure || current.Value != actor) return FormDefinitionErrors.Forbidden;
        return new PagedResult<FormDefinitionListResponse>(page.Items.Where(row => facts.ContainsKey(row.Id)).Select(row => new FormDefinitionListResponse(row.Id, row.Name, row.Prefix,
            row.QuestionCount, row.ActiveVersionNumber, row.DisplayState, row.LastChangedAtUtc, row.LastChangedBy, canSave,
            FormDefinitionLifecyclePolicy.View(facts[row.Id], states.GetValueOrDefault(row.Id), nowUtc, canSave, canPublish, canReadRecords))).ToArray(), page.Page, page.PageSize, page.TotalCount);
    }
    public Task<FormChangeStoredRow?> FindChangeAsync(Guid actor, Guid requestId, CancellationToken ct) => reader.FindChangeAsync(actor, requestId, ct);
    public Task<FormDefinitionCounts> CountFacetsAsync(string? search, string culture, CancellationToken ct) => reader.CountFacetsAsync(search, culture, ct);
}
