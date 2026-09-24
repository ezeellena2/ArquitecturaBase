namespace ArquitecturaBase.Application.Models.Identity;

/// <summary>
/// Cuántos usuarios traería cada opción de filtro. Cada dimensión se cuenta con los demás filtros puestos e
/// <b>ignorando el propio</b>: el número de una opción es lo que quedaría si se la eligiera, no lo que hay
/// ahora. Contado de otra forma, "Inactivos" diría 0 mientras se están mirando los activos, y el filtro
/// parecería no tener nada del otro lado.
/// </summary>
public sealed record UserFilterCounts(
    UserStatusCounts Status,
    IReadOnlyCollection<RoleFilterCount> Roles,
    IReadOnlyCollection<CreatedWithinCount> CreatedWithin);

/// <summary>All es el total sin filtrar por estado; Active + Inactive tiene que dar All.</summary>
public sealed record UserStatusCounts(int All, int Active, int Inactive);

/// <summary>Un rol sin nadie viene igual, con cero: la opción se muestra apagada y para eso hay que saber que existe.</summary>
public sealed record RoleFilterCount(string Name, int Count);

public sealed record CreatedWithinCount(int Days, int Count);
