using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Configuration;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

/// <summary>
/// What the detail of a response describes with the app's form (VF1-A), over the responses of a <see cref="FakeFormResponseReader"/>:
/// the version of each one, published, without rules, issuers or person data and with catalogs that are no longer there.
/// </summary>
internal sealed class FormResponseEntryDouble : IFormEntryReader
{
    private const string SubmitLabel = "{\"es\":\"Enviar\",\"en\":\"Submit\"}";
    private readonly FakeFormResponseReader responses;
    private readonly FormEntryCatalogPolicy catalogs;
    private readonly FormEntryVersionPolicy describer;

    public FormResponseEntryDouble(FakeFormResponseReader responses)
    {
        this.responses = responses;
        var sources = new FormCatalogPreparationFixture { CatalogMissing = true };
        catalogs = new(new FormCatalogOptionsPolicy(sources, sources, new FormCatalogPersonReader()), this, sources);
        describer = new(this, catalogs, new(sources.Rules, sources), Options.Create(new BusinessFormsOptions()), new([], sources.Rules));
    }

    /// <summary>The detail's form over these responses, with whether the person reading may correct them.</summary>
    public FormResponseEntryPolicy Policy(FormRecordAmendmentGuard amendments) => new(describer, catalogs, amendments);

    public Task<FormEntryDefinitionRow?> FindDefinitionAsync(Guid definitionId, Guid? versionId, CancellationToken cancellationToken)
    {
        var row = responses.ExportRows.FirstOrDefault(value => value.Record.DefinitionId == definitionId && value.Record.VersionId == versionId);
        return Task.FromResult(row is null ? null : new FormEntryDefinitionRow(definitionId, row.Record.Prefix, versionId, false,
            new(row.Record.VersionId, row.Record.VersionNumber, row.Record.NameJson, SubmitLabel, row.Version!.OriginalCulture, row.Record.SchemaJson)));
    }

    public Task<PagedResult<FormCatalogStoredItemRow>> ListItemsAsync(FormEntryItemsQuery query, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<FormEntryGroupRow>> ListGroupsAsync(Guid catalogId, string? containment, Guid fieldId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<PagedResult<FormCatalogFilterOptionResponse>> ListPeopleAsync(Guid recipientUserId, FormCatalogFilterDefinition filters,
        FormEntryItemsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<FormEntryOutcomeRow?> FindOutcomeAsync(Guid recordId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<FormEntryOriginRow>> ListOriginsAsync(IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
