using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.Models.Roles;

/// <summary>
/// El listado paginado de roles (<c>GET /api/roles/paged</c>). El catálogo completo, para los selectores, sigue en
/// <c>GET /api/roles</c>. Search busca en el nombre y en la descripción, sin distinguir mayúsculas.
/// </summary>
public sealed record ListRolesRequest : PagedRequest
{
    public static readonly IReadOnlyCollection<string> SortableFields = ["name", "createdAtUtc"];
}
