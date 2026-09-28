using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

public sealed record ListWidgetsRequest : PagedRequest
{
    // Lista blanca: los mismos nombres que usa el servicio para ordenar.
    public static readonly IReadOnlyCollection<string> SortableFields = ["name", "createdAtUtc"];
}
