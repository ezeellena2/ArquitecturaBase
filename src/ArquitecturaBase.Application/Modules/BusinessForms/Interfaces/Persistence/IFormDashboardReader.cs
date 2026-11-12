using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
/// <summary>
/// The dashboard of a form (DOC7-A), added up in SQL over the sheet's filter (the same grammar and the same restriction to one's
/// own responses, from the filter's <c>OwnerUserId</c>): one statement per family, never one per question. A sum, an average, a
/// minimum or a maximum that does not fit in a decimal comes back null with its overflow mark.
/// </summary>
public interface IFormDashboardReader
{
    /// <summary>How many responses match, the first and the last received, each measure by currency and each date question's range.</summary>
    Task<FormDashboardTotalsRow> FindTotalsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        IReadOnlyList<FormDashboardMeasure> measures, IReadOnlyCollection<Guid> dateFieldIds, CancellationToken cancellationToken);
    /// <summary>The buckets of the received dates with responses, from the series' first one on, with each measure's total by currency.</summary>
    Task<IReadOnlyList<FormDashboardBucketRow>> ListBucketsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        FormDashboardSeries series, IReadOnlyList<FormDashboardMeasure> measures, CancellationToken cancellationToken);
    /// <summary>The buckets of each date question with responses, from its window's first one on, with each measure's total by currency.</summary>
    Task<IReadOnlyList<FormDashboardDateBucketRow>> ListDateBucketsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        string period, IReadOnlyList<FormDashboardDateWindow> windows, IReadOnlyList<FormDashboardMeasure> measures, CancellationToken cancellationToken);
    /// <summary>
    /// The first <paramref name="top"/> options of each question by how many responses chose them (a tie, by label: the fixed ones
    /// by <paramref name="labels"/>, a catalog's by its latest reference), each measure's total among them, and what is left.
    /// </summary>
    Task<IReadOnlyList<FormDashboardDistributionRow>> ListDistributionsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        IReadOnlyCollection<Guid> fieldIds, IReadOnlyList<FormDashboardChoiceLabel> labels, IReadOnlyList<FormDashboardMeasure> measures, int top,
        CancellationToken cancellationToken);
    /// <summary>The label of each catalog option among the responses of the form (only the owner's, with an owner), in the filter's culture.</summary>
    Task<IReadOnlyList<FormDashboardLabelRow>> ListCatalogLabelsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        IReadOnlyCollection<Guid> fieldIds, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken);
    /// <summary>The first <paramref name="top"/> people by how many responses they loaded (a tie, by name), and what is left.</summary>
    Task<FormDashboardActorsRow> FindActorsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter, int top, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormDashboardStateRow>> ListStatesAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter, CancellationToken cancellationToken);
}
