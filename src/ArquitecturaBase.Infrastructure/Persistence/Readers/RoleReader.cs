using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

/// <summary>
/// El lector de referencia: el catálogo (todo, para los selectores), el detalle y la página del listado comparten una
/// sola proyección (<see cref="Project"/>) y un solo armado de la fila (<see cref="ToRow"/>).
/// </summary>
internal sealed class RoleReader(ApplicationDbContext dbContext, RoleManager<ApplicationRole> roleManager) : IRoleReader
{
    // Lista blanca: los mismos nombres que ListRolesRequest.SortableFields.
    private static readonly Dictionary<string, Expression<Func<ApplicationRole, object?>>> SortMap = new()
    {
        ["name"] = role => role.Name,
        ["createdAtUtc"] = role => role.CreatedAtUtc,
    };

    private static readonly SortDescriptor DefaultSort = new("name", Descending: false);

    public async Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
        await dbContext.Roles
            .AsNoTracking()
            .Select(role => role.Name!)
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<RoleRow>> ListAllRolesAsync(CancellationToken cancellationToken) =>
        await LoadRolesAsync(roleId: null, cancellationToken);

    public async Task<PagedResult<RoleRow>> ListRolesAsync(ListRolesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = dbContext.Roles.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // "%" y "_" del texto buscado son literales, no comodines. Los "!= null" no son adorno: Name y Description
            // son string?, y un rol sin descripción tiene que seguir apareciendo si el nombre coincide.
            var pattern = LikePatterns.Contains(request.Search.Trim());

            query = query.Where(role =>
                (role.Name != null && EF.Functions.ILike(role.Name, pattern, LikePatterns.EscapeCharacter))
                || (role.Description != null && EF.Functions.ILike(role.Description, pattern, LikePatterns.EscapeCharacter)));
        }

        // El orden y el desempate por Id van antes de la proyección, sobre las columnas de la tabla: sin desempate, dos
        // roles con la misma fecha podrían repetirse o faltar entre una página y la siguiente.
        var page = await Project(query.ApplySort(SortDescriptor.Parse(request.Sort), SortMap, DefaultSort, role => role.Id))
            .ToPagedResultAsync(request, cancellationToken);

        return new PagedResult<RoleRow>([.. page.Items.Select(ToRow)], page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<RoleRow?> FindByIdAsync(Guid roleId, CancellationToken cancellationToken) =>
        (await LoadRolesAsync(roleId, cancellationToken)).FirstOrDefault();

    public Task<bool> ExistsByNameAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken)
    {
        var normalized = roleManager.NormalizeKey(name);

        return dbContext.Roles.AnyAsync(
            role => role.NormalizedName == normalized && (excludedRoleId == null || role.Id != excludedRoleId),
            cancellationToken);
    }

    // El catálogo, ordenado por nombre, o solo el rol pedido.
    private async Task<List<RoleRow>> LoadRolesAsync(Guid? roleId, CancellationToken cancellationToken)
    {
        var query = dbContext.Roles.AsNoTracking();

        if (roleId is { } id)
        {
            query = query.Where(role => role.Id == id);
        }

        var rows = await Project(query.OrderBy(role => role.Name)).ToListAsync(cancellationToken);

        return [.. rows.Select(ToRow)];
    }

    /// <summary>
    /// La misma forma para el catálogo, el detalle y el listado. UserCount cuenta sobre dbContext.Users, que arrastra el
    /// filtro global: un usuario borrado no mantiene vivo a un rol. Es de instancia porque la expresión usa dbContext.
    /// </summary>
    private IQueryable<RoleData> Project(IQueryable<ApplicationRole> roles) =>
        roles.Select(role => new RoleData(
            role.Id,
            role.Name!,
            role.Description,
            dbContext.Users.Count(user =>
                dbContext.UserRoles.Any(userRole => userRole.RoleId == role.Id && userRole.UserId == user.Id)),
            dbContext.RoleClaims
                .Where(claim => claim.RoleId == role.Id && claim.ClaimType == Permissions.ClaimType)
                .Select(claim => claim.ClaimValue!)
                .ToList()));

    // En memoria: si es de sistema y el orden ordinal de los permisos, que Postgres no garantiza.
    private static RoleRow ToRow(RoleData role) =>
        new(
            role.Id,
            role.Name,
            role.Description,
            SystemRoles.All.Contains(role.Name, StringComparer.Ordinal),
            role.UserCount,
            [.. role.Permissions.Order(StringComparer.Ordinal)]);

    private sealed record RoleData(Guid Id, string Name, string? Description, int UserCount, List<string> Permissions);
}
