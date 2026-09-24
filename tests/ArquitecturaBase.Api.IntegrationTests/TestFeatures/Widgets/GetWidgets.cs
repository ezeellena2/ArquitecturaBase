using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

public sealed record GetWidgetsRequest : PagedRequest
{
    // Lista blanca: los mismos nombres que usa el servicio para ordenar.
    public static readonly IReadOnlyCollection<string> SortableFields = ["name", "createdAtUtc"];
}

public sealed record WidgetResponse(Guid Id, string Name, DateTime CreatedAtUtc);

internal sealed class GetWidgetsRequestValidator() : PagedRequestValidator<GetWidgetsRequest>(GetWidgetsRequest.SortableFields);
