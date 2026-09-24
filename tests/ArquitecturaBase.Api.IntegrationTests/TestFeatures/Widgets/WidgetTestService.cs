using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

public interface IWidgetTestService
{
    Task<Result<Guid>> CreateAsync(CreateWidgetRequest request, CancellationToken cancellationToken);

    Task<Result<WidgetDetailsResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<PagedResult<WidgetResponse>>> ListAsync(GetWidgetsRequest request, CancellationToken cancellationToken);
}

/// <summary>Test-only data service for auditing, validation and pagination against the real PostgreSQL pipeline.</summary>
internal sealed class WidgetTestService(
    ApplicationDbContext dbContext,
    IUnitOfWork unitOfWork,
    ServiceRequestValidator<CreateWidgetRequest> createValidator,
    ServiceRequestValidator<GetWidgetsRequest> listValidator) : IWidgetTestService
{
    private static readonly Dictionary<string, Expression<Func<Widget, object?>>> SortMap = new()
    {
        ["name"] = widget => widget.Name,
        ["createdAtUtc"] = widget => widget.CreatedAtUtc,
    };

    private static readonly SortDescriptor DefaultSort = new("createdAtUtc", Descending: true);

    public async Task<Result<Guid>> CreateAsync(CreateWidgetRequest request, CancellationToken cancellationToken)
    {
        var validationError = await createValidator.ValidateAsync(request, cancellationToken);
        if (validationError is not null)
        {
            return validationError;
        }

        var widget = new Widget(request.Name!);
        dbContext.Set<Widget>().Add(widget);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return widget.Id;
    }

    public async Task<Result<WidgetDetailsResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var widget = await dbContext.Set<Widget>()
            .AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new WidgetDetailsResponse(w.Id, w.Name, w.CreatedAtUtc, w.CreatedBy, w.ModifiedAtUtc, w.ModifiedBy))
            .SingleOrDefaultAsync(cancellationToken);

        return widget is null ? WidgetErrors.NotFound : widget;
    }

    public async Task<Result<PagedResult<WidgetResponse>>> ListAsync(
        GetWidgetsRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = await listValidator.ValidateAsync(request, cancellationToken);
        if (validationError is not null)
        {
            return validationError;
        }

        var widgets = dbContext.Set<Widget>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Código de prueba: no escapa "%" ni "_"; una feature real sí debe escaparlos.
            widgets = widgets.Where(widget => EF.Functions.Like(widget.Name, request.Search + "%"));
        }

        return await widgets
            .ApplySort(SortDescriptor.Parse(request.Sort), SortMap, DefaultSort, widget => widget.Id)
            .Select(widget => new WidgetResponse(widget.Id, widget.Name, widget.CreatedAtUtc))
            .ToPagedResultAsync(request, cancellationToken);
    }
}
