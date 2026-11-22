using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.Models.Roles;

/// <summary>
/// El listado paginado de roles (<c>GET /api/roles/paged</c>). El catálogo completo, para los selectores, sigue en
/// <c>GET /api/roles</c>. Search busca en el nombre y en la descripción, sin distinguir mayúsculas.
/// </summary>
public sealed record ListRolesRequest : PagedRequest
{
    /// <summary>
    /// Toda columna con un dato, con el id de su columna en el front (OT1-C, 2026-11-22): <c>permissions</c> es la cantidad
    /// de permisos, como la muestra la tabla.
    /// </summary>
    public static readonly IReadOnlyCollection<string> SortableFields = ["name", "description", "userCount", "permissions", "createdAtUtc"];
}
