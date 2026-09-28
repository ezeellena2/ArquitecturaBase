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

    Task<Result<WidgetDetailResponse>> GetWidgetAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<PagedResult<WidgetListItemResponse>>> ListWidgetsAsync(ListWidgetsRequest request, CancellationToken cancellationToken);
}

// Usa ApplicationDbContext directamente solo porque es de prueba: una feature real no lo hace, va detrás de un
// repositorio o un lector (Application/Interfaces/Persistence + Infrastructure/Persistence/{Repositories,Readers}).
/// <summary>Test-only data service for auditing, validation and pagination against the real PostgreSQL pipeline.</summary>
internal sealed class WidgetTestService(
    ApplicationDbContext dbContext,
    IUnitOfWork unitOfWork,
    IRequestValidator validator) : IWidgetTestService
{
    private static readonly Dictionary<string, Expression<Func<Widget, object?>>> SortMap = new()
    {
        ["name"] = widget => widget.Name,
        ["createdAtUtc"] = widget => widget.CreatedAtUtc,
    };

    private static readonly SortDescriptor DefaultSort = new("createdAtUtc", Descending: true);

    public async Task<Result<Guid>> CreateAsync(CreateWidgetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            return validationError;
        }

        // Validar afuera y escribir adentro de un solo límite, sin logs: es un servicio de prueba. El patrón completo,
        // con sus logs y lo que va después del commit, es RoleService.UpdateAsync.
        return await unitOfWork.ExecuteInTransactionAsync(
            _ =>
            {
                var widget = new Widget(request.Name!);
                dbContext.Set<Widget>().Add(widget);

                return Task.FromResult(Result.Success(widget.Id));
            },
            CommitPolicy.OnSuccess,
            cancellationToken);
    }

    public async Task<Result<WidgetDetailResponse>> GetWidgetAsync(Guid id, CancellationToken cancellationToken)
    {
        var widget = await dbContext.Set<Widget>()
            .AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new WidgetDetailResponse(w.Id, w.Name, w.CreatedAtUtc, w.CreatedBy, w.ModifiedAtUtc, w.ModifiedBy))
            .SingleOrDefaultAsync(cancellationToken);

        return widget is null ? WidgetErrors.NotFound : widget;
    }

    public async Task<Result<PagedResult<WidgetListItemResponse>>> ListWidgetsAsync(
        ListWidgetsRequest request,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            return validationError;
        }

        var widgets = dbContext.Set<Widget>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePatterns.Contains(request.Search.Trim());
            widgets = widgets.Where(widget => EF.Functions.ILike(widget.Name, pattern, LikePatterns.EscapeCharacter));
        }

        return await widgets
            .ApplySort(SortDescriptor.Parse(request.Sort), SortMap, DefaultSort, widget => widget.Id)
            .Select(widget => new WidgetListItemResponse(widget.Id, widget.Name, widget.CreatedAtUtc))
            .ToPagedResultAsync(request, cancellationToken);
    }
}
