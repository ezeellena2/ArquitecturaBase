using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Common.Validation;

/// <summary>
/// Valida página, tamaño y orden. Cada consulta pasa su lista blanca de campos ordenables:
/// <c>internal sealed class GetUsersQueryValidator() : PagedRequestValidator&lt;GetUsersQuery&gt;(GetUsersQuery.SortableFields);</c>
/// </summary>
public abstract class PagedRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : PagedRequest
{
    protected PagedRequestValidator(IReadOnlyCollection<string> sortableFields)
    {
        ArgumentNullException.ThrowIfNull(sortableFields);

        RuleFor(request => request.Page)
            .InclusiveBetween(1, PagedRequest.MaxPage).WithMessage(_ => ValidationMessages.PageInvalid);

        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, PagedRequest.MaxPageSize).WithMessage(_ => ValidationMessages.PageSizeInvalid);

        RuleFor(request => request.Sort)
            .Must(sort => IsSortable(sort, sortableFields)).WithMessage(_ => ValidationMessages.SortNotAllowed);
    }

    private static bool IsSortable(string? sort, IReadOnlyCollection<string> sortableFields)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return true;
        }

        var descriptor = SortDescriptor.Parse(sort);

        return descriptor is not null && sortableFields.Contains(descriptor.Field, StringComparer.OrdinalIgnoreCase);
    }
}
