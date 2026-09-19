using System.Linq.Expressions;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

public sealed record GetWidgetsQuery : PagedRequest, IQuery<PagedResult<WidgetResponse>>
{
    // Lista blanca: los mismos nombres que usa el handler para ordenar.
    public static readonly IReadOnlyCollection<string> SortableFields = ["name", "createdAtUtc"];
}

public sealed record WidgetResponse(Guid Id, string Name, DateTime CreatedAtUtc);

internal sealed class GetWidgetsQueryValidator() : PagedRequestValidator<GetWidgetsQuery>(GetWidgetsQuery.SortableFields);

internal sealed class GetWidgetsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetWidgetsQuery, PagedResult<WidgetResponse>>
{
    private static readonly Dictionary<string, Expression<Func<Widget, object?>>> SortMap = new()
    {
        ["name"] = widget => widget.Name,
        ["createdAtUtc"] = widget => widget.CreatedAtUtc,
    };

    private static readonly SortDescriptor DefaultSort = new("createdAtUtc", Descending: true);

    public async Task<Result<PagedResult<WidgetResponse>>> Handle(GetWidgetsQuery query, CancellationToken cancellationToken)
    {
        var widgets = dbContext.Set<Widget>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Código de prueba: no escapa "%" ni "_"; una feature real sí debe escaparlos.
            widgets = widgets.Where(widget => EF.Functions.Like(widget.Name, query.Search + "%"));
        }

        return await widgets
            .ApplySort(SortDescriptor.Parse(query.Sort), SortMap, DefaultSort, widget => widget.Id)
            .Select(widget => new WidgetResponse(widget.Id, widget.Name, widget.CreatedAtUtc))
            .ToPagedResultAsync(query, cancellationToken);
    }
}
