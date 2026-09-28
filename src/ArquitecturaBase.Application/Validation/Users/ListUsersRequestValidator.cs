using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Users;

/// <summary>
/// Valida paginado y, además, los filtros de <see cref="ListUsersRequest"/>. Lo usan el listado y el conteo por opción
/// de filtro: con un solo validador, uno no puede aceptar lo que el otro rechaza, y los números no dejan de coincidir con
/// el listado que describen.
/// </summary>
internal sealed class ListUsersRequestValidator : PagedRequestValidator<ListUsersRequest>
{
    public ListUsersRequestValidator()
        : base(ListUsersRequest.SortableFields)
    {
        // El parámetro ausente es "sin filtro"; el parámetro presente y vacío es un error del cliente, y
        // contestarlo dice dónde está. Devolver todo, como si no se hubiera filtrado, parecería un filtro roto.
        RuleFor(request => request.Role)
            .NotEmpty().WithMessage(_ => ValidationMessages.Required)
            .MaxLength(ValidationRules.RoleNameMaxLength)
            .When(request => request.Role is not null);

        // Un rol que no existe NO se valida: devuelve cero resultados. Ver ListUsersRequest.Role.
        RuleFor(request => request.CreatedWithinDays)
            .InclusiveBetween(1, ListUsersRequest.MaxCreatedWithinDays)
            .WithMessage(_ => ValidationMessages.CreatedWithinDaysInvalid)
            .When(request => request.CreatedWithinDays is not null);
    }
}
