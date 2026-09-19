using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

public sealed record GetWidgetByIdQuery(Guid Id) : IQuery<WidgetDetailsResponse>;

public sealed record WidgetDetailsResponse(
    Guid Id,
    string Name,
    DateTime CreatedAtUtc,
    Guid? CreatedBy,
    DateTime? ModifiedAtUtc,
    Guid? ModifiedBy);

internal sealed class GetWidgetByIdQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetWidgetByIdQuery, WidgetDetailsResponse>
{
    public async Task<Result<WidgetDetailsResponse>> Handle(GetWidgetByIdQuery query, CancellationToken cancellationToken)
    {
        var widget = await dbContext.Set<Widget>()
            .AsNoTracking()
            .Where(w => w.Id == query.Id)
            .Select(w => new WidgetDetailsResponse(w.Id, w.Name, w.CreatedAtUtc, w.CreatedBy, w.ModifiedAtUtc, w.ModifiedBy))
            .SingleOrDefaultAsync(cancellationToken);

        return widget is null ? WidgetErrors.NotFound : widget;
    }
}
