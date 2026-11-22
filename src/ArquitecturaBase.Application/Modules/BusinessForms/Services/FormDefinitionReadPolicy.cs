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
        // The official preview is of a channel: a version published only in the app has none (SA2-A).
        return new FormDefinitionDetailResponse(FormDraftPolicy.Definition(row.Id, row.NameJson, row.Prefix, row.PrefixLocked, row.Revision,
            row.Culture, row.SubmitLabelJson, row.SchemaJson, row.OfferWhen, row.AppOnly, row.DocumentReadingJson) with { Standard = FormStandardPolicy.Response(row.StandardKey, row.StandardVersion, row.StandardBindingsJson) },
            new(canSave, canPublish, publication.ActiveVersion is not null && publication.ActiveVersion.AppOnly != true),
            new(row.CreatedAtUtc, row.ModifiedAtUtc, row.LastChangedAtUtc, row.Author), publication,
            FormDefinitionLifecyclePolicy.View(facts, state, nowUtc, canSave, canPublish, canReadRecords));
    }
    public async Task<Result<PagedResult<FormDefinitionListResponse>>> ListAsync(ListFormDefinitionsRequest request, Guid actor, CancellationToken ct)
    {
        // OT1-C: «Respuestas» sorts only for whoever sees the counts, so the order reveals none.
        var sortsByResponses = SortsBy(request, "responseCount") && await access.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsRead, ct);
        var page = await reader.ListAsync(Sortable(request, sortsByResponses), ct);
        var facts = (await lifecycle.ListFactsAsync(page.Items.Select(row => row.Id).ToArray(), ct)).ToDictionary(row => row.DefinitionId);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var states = (await channelState.ListAsync(page.Items.Select(row => row.Id).ToArray(), nowUtc, ct)).ToDictionary(row => row.DefinitionId);
        var canSave = await access.HasPermissionAsync(actor, BusinessFormsPermissions.Manage, ct);
        var canPublish = await access.HasPermissionAsync(actor, BusinessFormsPermissions.Publish, ct);
        var canReadRecords = await access.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsRead, ct);
        var current = await access.CheckAsync(false, ct);
        // The order already followed the counts: without the permission now, hiding them would not be enough.
        if (current.IsFailure || current.Value != actor || sortsByResponses && !canReadRecords) return FormDefinitionErrors.Forbidden;
        return new PagedResult<FormDefinitionListResponse>(page.Items.Where(row => facts.ContainsKey(row.Id)).Select(row => new FormDefinitionListResponse(row.Id, row.Name, row.Prefix,
            row.QuestionCount, row.ActiveVersionNumber, row.DisplayState, row.LastChangedAtUtc, row.LastChangedBy, canSave,
            FormDefinitionLifecyclePolicy.View(facts[row.Id], states.GetValueOrDefault(row.Id), nowUtc, canSave, canPublish, canReadRecords),
            row.AppOnly ? true : null)).ToArray(), page.Page, page.PageSize, page.TotalCount);
    }
    // The list sorts in SQL by what each column shows. Two columns show no datum it can sort by, and keep the usual order:
    // «Respuestas» for whoever cannot read the counts (empty in every row) and «Estado» among the deactivated, where it is the
    // withdrawal from the channel, which comes from the module of the channel.
    private static ListFormDefinitionsRequest Sortable(ListFormDefinitionsRequest request, bool canSortByResponses) =>
        SortsBy(request, "responseCount") && !canSortByResponses || SortsBy(request, "displayState") && request.PublicationFilter == "Deactivated"
            ? request with { Sort = null } : request;
    private static bool SortsBy(ListFormDefinitionsRequest request, string field) =>
        string.Equals(SortDescriptor.Parse(request.Sort)?.Field, field, StringComparison.OrdinalIgnoreCase);
    public Task<FormChangeStoredRow?> FindChangeAsync(Guid actor, Guid requestId, CancellationToken ct) => reader.FindChangeAsync(actor, requestId, ct);
    public Task<FormDefinitionCounts> CountFacetsAsync(ListFormDefinitionsRequest request, CancellationToken ct) => reader.CountFacetsAsync(request, ct);
}
