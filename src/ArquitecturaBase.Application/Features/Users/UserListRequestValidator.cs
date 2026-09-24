using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Users;

/// <summary>
/// Valida paginado y, además, los filtros de <see cref="UserListRequest"/>. Lo heredan el listado y el
/// conteo por opción de filtro: si cada uno validara por su cuenta, uno podría aceptar lo que el otro
/// rechaza y los números dejarían de coincidir con el listado que describen.
/// </summary>
internal abstract class UserListRequestValidator<TRequest> : PagedRequestValidator<TRequest>
    where TRequest : UserListRequest
{
    protected UserListRequestValidator(IReadOnlyCollection<string> sortableFields)
        : base(sortableFields)
    {
        // El parámetro ausente es "sin filtro"; el parámetro presente y vacío es un error del cliente, y
        // contestarlo dice dónde está. Devolver todo, como si no se hubiera filtrado, parecería un filtro roto.
        RuleFor(request => request.Role)
            .NotEmpty().WithMessage(_ => ValidationMessages.Required)
            .MaxLength(ValidationRules.RoleNameMaxLength)
            .When(request => request.Role is not null);

        // Un rol que no existe NO se valida: devuelve cero resultados. Ver UserListRequest.Role.
        RuleFor(request => request.CreatedWithinDays)
            .InclusiveBetween(1, UserListRequest.MaxCreatedWithinDays)
            .WithMessage(_ => ValidationMessages.CreatedWithinDaysInvalid)
            .When(request => request.CreatedWithinDays is not null);
    }
}
