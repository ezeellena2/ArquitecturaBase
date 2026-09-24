using System.Buffers;
using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Features.Users.GetUser;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

internal sealed class UserReader(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    ApplicationDbContext dbContext,
    TimeProvider timeProvider) : IUserReader
{
    private const string LikeEscapeCharacter = "\\";

    /// <summary>Con menos dígitos, cualquier búsqueda con un par de números traería medio listado por el teléfono.</summary>
    private const int MinPhoneSearchDigits = 4;

    /// <summary>
    /// Lo que puede tener un número tal como lo escribe una persona: dígitos, espacios, "+", guiones, puntos y
    /// paréntesis. Un texto con cualquier otra cosa es un nombre o un correo, y no se busca por número.
    /// </summary>
    private static readonly SearchValues<char> PhoneSearchCharacters = SearchValues.Create("0123456789 +-.()");

    // Lista blanca: los mismos nombres que ListUsersRequest.SortableFields.
    private static readonly Dictionary<string, Expression<Func<ApplicationUser, object?>>> SortMap = new()
    {
        ["email"] = user => user.Email,
        ["displayName"] = user => user.DisplayName,
        ["createdAtUtc"] = user => user.CreatedAtUtc,
    };

    private static readonly SortDescriptor DefaultSort = new("createdAtUtc", Descending: true);

    private static readonly Expression<Func<ApplicationUser, UserAccount>> AccountProjection = user => new UserAccount(
        user.Id,
        user.Email,
        user.EmailConfirmed,
        user.PhoneNumber,
        user.PhoneNumberConfirmed,
        user.DisplayName,
        user.Culture,
        user.TimeZoneId,
        user.IsActive);

    public Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        userManager.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(AccountProjection)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<UserAccount?> FindByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phone);

        return userManager.Users
            .AsNoTracking()
            .Where(user => user.PhoneNumber == phone.Value)
            .Select(AccountProjection)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> IsDeletedEmailAsync(Email email, CancellationToken cancellationToken)
    {
        var normalized = userManager.NormalizeEmail(email.Value);

        return userManager.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .AnyAsync(user => user.IsDeleted && user.NormalizedEmail == normalized, cancellationToken);
    }

    public Task<bool> IsDeletedPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phone);

        return userManager.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .AnyAsync(user => user.IsDeleted && user.PhoneNumber == phone.Value, cancellationToken);
    }

    public Task<UserAccount?> FindDeletedByEmailAsync(Email email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        var normalized = userManager.NormalizeEmail(email.Value);

        return userManager.Users
            .AsNoTracking()
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .Where(user => user.IsDeleted && user.NormalizedEmail == normalized)
            .Select(AccountProjection)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<UserAccount?> FindDeletedByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phone);

        return userManager.Users
            .AsNoTracking()
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .Where(user => user.IsDeleted && user.PhoneNumber == phone.Value)
            .Select(AccountProjection)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> HasExternalLoginAsync(Guid userId, string provider, CancellationToken cancellationToken) =>
        dbContext.UserLogins.AnyAsync(login => login.UserId == userId && login.LoginProvider == provider, cancellationToken);

    public async Task<UserDetail?> FindDetailAsync(Guid userId, CancellationToken cancellationToken)
    {
        var detail = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.EmailConfirmed,
                user.PhoneNumber,
                user.PhoneNumberConfirmed,
                user.DisplayName,
                user.IsActive,
                user.CreatedAtUtc,
                Roles = dbContext.Roles
                    .Where(role => dbContext.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == role.Id))
                    .Select(role => role.Name!)
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return detail is null
            ? null
            : new UserDetail(
                detail.Id,
                detail.Email,
                detail.EmailConfirmed,
                detail.PhoneNumber,
                detail.PhoneNumberConfirmed,
                detail.DisplayName,
                detail.IsActive,
                detail.CreatedAtUtc,
                [.. detail.Roles.Order(StringComparer.Ordinal)]);
    }

    public async Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        (await userManager.GetUsersInRoleAsync(SystemRoles.Admin)).Count(user => user.IsActive);

    public Task<PagedResult<UserListItem>> ListUsersAsync(UserListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return FilterUsers(request)
            .ApplySort(SortDescriptor.Parse(request.Sort), SortMap, DefaultSort, user => user.Id)
            // Los roles salen en la misma consulta: EF los trae con su propio JOIN y son 20 filas por página.
            // Ordenados por nombre para que dos cargas de la misma página no los muestren en distinto orden.
            .Select(user => new UserListItem(
                user.Id,
                user.Email,
                user.PhoneNumber,
                user.PhoneNumberConfirmed,
                user.DisplayName,
                user.IsActive,
                user.CreatedAtUtc,
                dbContext.Roles
                    .Where(role => dbContext.UserRoles.Any(userRole =>
                        userRole.UserId == user.Id && userRole.RoleId == role.Id))
                    .OrderBy(role => role.Name)
                    .Select(role => role.Name!)
                    .ToList()))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<UserFilterCounts> GetUserFilterCountsAsync(UserListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Cada dimensión se cuenta ignorando su propio filtro: el número de una opción es lo que quedaría si
        // se la eligiera, no lo que hay ahora. Con el estado puesto en "activos", contar el estado con ese
        // filtro haría que "Inactivos" dijera 0 y el filtro parecería no tener nada del otro lado.
        var forStatus = FilterUsers(request with { IsActive = null });
        var forRoles = FilterUsers(request with { Role = null });
        var forDates = FilterUsers(request with { CreatedWithinDays = null });

        // Dos filas, no dos consultas: el agrupado los cuenta en la base.
        var byStatus = await forStatus
            .GroupBy(user => user.IsActive)
            .Select(group => new { IsActive = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var active = byStatus.SingleOrDefault(row => row.IsActive)?.Count ?? 0;
        var inactive = byStatus.SingleOrDefault(row => !row.IsActive)?.Count ?? 0;

        // Todos los roles, incluidos los que dan cero: la opción apagada tiene que verse, y para eso hay que
        // saber que existe.
        var roles = await dbContext.Roles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .Select(role => new RoleFilterCount(
                role.Name!,
                forRoles.Count(user => dbContext.UserRoles.Any(userRole =>
                    userRole.RoleId == role.Id && userRole.UserId == user.Id))))
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var createdWithin = new List<CreatedWithinCount>(UserListRequest.CreatedWithinOptions.Count);

        // Un COUNT por tramo. Se podría hacer en una sola consulta con un CASE armado a mano, pero por dos
        // números sobre una columna indexada no vale la pena el árbol de expresiones.
        foreach (var days in UserListRequest.CreatedWithinOptions)
        {
            var since = now.AddDays(-days);
            createdWithin.Add(new CreatedWithinCount(
                days,
                await forDates.CountAsync(user => user.CreatedAtUtc >= since, cancellationToken)));
        }

        return new UserFilterCounts(new UserStatusCounts(active + inactive, active, inactive), roles, createdWithin);
    }

    /// <summary>
    /// La búsqueda y los tres filtros del listado, en un solo lugar. Lo comparten el listado y el conteo por
    /// opción de filtro: si cada uno armara su consulta, un número podría dejar de describir a la lista que
    /// dice describir.
    /// </summary>
    private IQueryable<ApplicationUser> FilterUsers(UserListRequest request)
    {
        var users = userManager.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();

            // "%" y "_" del texto buscado son literales, no comodines.
            var pattern = "%" + EscapeLike(search) + "%";

            var phonePattern = PhoneSearchPatternOf(search);

            users = users.Where(user =>
                (user.Email != null && EF.Functions.ILike(user.Email, pattern, LikeEscapeCharacter))
                || (user.DisplayName != null && EF.Functions.ILike(user.DisplayName, pattern, LikeEscapeCharacter))
                || (phonePattern != null && user.PhoneNumber != null && EF.Functions.Like(user.PhoneNumber, phonePattern)));
        }

        if (request.IsActive is { } isActive)
        {
            users = users.Where(user => user.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            // El nombre normalizado es el que tiene índice; comparar por Name haría un scan. Un rol que no
            // existe no matchea con nada y devuelve cero filas, que es la decisión: el código de respuesta
            // no cuenta qué roles existen.
            var normalized = roleManager.NormalizeKey(request.Role);
            users = users.Where(user => dbContext.UserRoles.Any(userRole =>
                userRole.UserId == user.Id
                && dbContext.Roles.Any(role => role.Id == userRole.RoleId && role.NormalizedName == normalized)));
        }

        if (request.CreatedWithinDays is { } days)
        {
            var since = timeProvider.GetUtcNow().UtcDateTime.AddDays(-days);
            users = users.Where(user => user.CreatedAtUtc >= since);
        }

        return users;
    }

    /// <summary>
    /// El patrón para buscar por número, o null si el texto no parece uno. El número se guarda como "+" y dígitos, así
    /// que se busca solo con los dígitos del texto: "11 2345", "11-2345" y "112345" encuentran lo mismo. Sin comodines
    /// que escapar, porque son todos dígitos. Con letras o una arroba no se busca: los dígitos de "juan2024@…"
    /// traerían a quien los tiene en el número por casualidad.
    /// </summary>
    private static string? PhoneSearchPatternOf(string search)
    {
        if (search.AsSpan().ContainsAnyExcept(PhoneSearchCharacters))
        {
            return null;
        }

        var digits = string.Concat(search.Where(char.IsAsciiDigit));

        return digits.Length >= MinPhoneSearchDigits ? "%" + digits + "%" : null;
    }

    private static string EscapeLike(string value) =>
        value
            .Replace(LikeEscapeCharacter, LikeEscapeCharacter + LikeEscapeCharacter, StringComparison.Ordinal)
            .Replace("%", LikeEscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscapeCharacter + "_", StringComparison.Ordinal);
}
