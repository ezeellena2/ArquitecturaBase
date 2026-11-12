using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
/// <summary>The dashboard's reader in memory: it answers what each test arranges and keeps every filter it was asked with.</summary>
internal sealed class FakeFormDashboardReader : IFormDashboardReader
{
    public FormDashboardTotalsRow Totals { get; set; } = new(0, null, null, [], []);
    public FormDashboardTotalsRow PreviousTotals { get; set; } = new(0, null, null, [], []);
    public IReadOnlyList<FormDashboardBucketRow> Buckets { get; set; } = [];
    public IReadOnlyList<FormDashboardDateBucketRow> DateBuckets { get; set; } = [];
    public IReadOnlyList<FormDashboardDistributionRow> Distributions { get; set; } = [];
    public IReadOnlyList<FormDashboardLabelRow> Labels { get; set; } = [];
    public FormDashboardActorsRow Actors { get; set; } = new([], 0, 0);
    public IReadOnlyList<FormDashboardStateRow> States { get; set; } = [];
    public Action? BeforeReturn { get; set; }
    public List<(string Family, ListFormRecordsRequest Filter)> Reads { get; } = [];
    public List<IReadOnlyList<FormDashboardMeasure>> Measures { get; } = [];
    public List<IReadOnlyCollection<Guid>> DateFields { get; } = [];
    public FormDashboardSeries? Series { get; private set; }
    public IReadOnlyList<FormDashboardDateWindow>? DateWindows { get; private set; }
    public IReadOnlyCollection<Guid>? DistributedFields { get; private set; }
    public IReadOnlyList<FormDashboardChoiceLabel>? ChoiceLabels { get; private set; }
    public (IReadOnlyCollection<Guid> FieldIds, IReadOnlyCollection<Guid> ItemIds)? CatalogItems { get; private set; }

    public Task<FormDashboardTotalsRow> FindTotalsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        IReadOnlyList<FormDashboardMeasure> measures, IReadOnlyCollection<Guid> dateFieldIds, CancellationToken cancellationToken)
    {
        var previous = Reads.Any(read => read.Family == "totals");
        Read("totals", filter); Measures.Add(measures); DateFields.Add(dateFieldIds);
        return Task.FromResult(previous ? PreviousTotals : Totals);
    }

    public Task<IReadOnlyList<FormDashboardBucketRow>> ListBucketsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        FormDashboardSeries series, IReadOnlyList<FormDashboardMeasure> measures, CancellationToken cancellationToken)
    {
        Read("buckets", filter); Series = series;
        return Task.FromResult(Buckets);
    }

    public Task<IReadOnlyList<FormDashboardDateBucketRow>> ListDateBucketsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        string period, IReadOnlyList<FormDashboardDateWindow> windows, IReadOnlyList<FormDashboardMeasure> measures, CancellationToken cancellationToken)
    {
        Read("dates", filter); DateWindows = windows;
        return Task.FromResult(DateBuckets);
    }

    public Task<IReadOnlyList<FormDashboardDistributionRow>> ListDistributionsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        IReadOnlyCollection<Guid> fieldIds, IReadOnlyList<FormDashboardChoiceLabel> labels, IReadOnlyList<FormDashboardMeasure> measures, int top,
        CancellationToken cancellationToken)
    {
        Read("distributions", filter); DistributedFields = fieldIds; ChoiceLabels = labels;
        return Task.FromResult(Distributions);
    }

    public Task<IReadOnlyList<FormDashboardLabelRow>> ListCatalogLabelsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter,
        IReadOnlyCollection<Guid> fieldIds, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken)
    {
        Read("labels", filter); CatalogItems = (fieldIds, itemIds);
        return Task.FromResult(Labels);
    }

    public Task<FormDashboardActorsRow> FindActorsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter, int top, CancellationToken cancellationToken)
    {
        Read("actors", filter);
        return Task.FromResult(Actors);
    }

    public Task<IReadOnlyList<FormDashboardStateRow>> ListStatesAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest filter, CancellationToken cancellationToken)
    {
        Read("states", filter); BeforeReturn?.Invoke();
        return Task.FromResult(States);
    }

    private void Read(string family, ListFormRecordsRequest filter) => Reads.Add((family, filter));
}
