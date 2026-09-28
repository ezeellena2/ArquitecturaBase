using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.Roles;

namespace ArquitecturaBase.Application.Validation.Roles;

/// <summary>Solo el paginado: el listado de roles no tiene filtros propios.</summary>
internal sealed class ListRolesRequestValidator() : PagedRequestValidator<ListRolesRequest>(ListRolesRequest.SortableFields);
