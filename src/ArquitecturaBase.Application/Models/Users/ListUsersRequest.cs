using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// El listado de usuarios y lo que sabe filtrar. Lo usan dos casos: el listado y el conteo por opción de filtro, que
/// tienen que filtrar exactamente igual o los números de cada opción dejan de coincidir con lo que trae elegirla. El
/// conteo deja el paginado y el orden en sus valores por omisión.
/// </summary>
public sealed record ListUsersRequest : PagedRequest
{
    /// <summary>Diez años. Es un filtro de "recién creados": más allá de eso, el filtro no filtra nada.</summary>
    public const int MaxCreatedWithinDays = 3650;

    public static readonly IReadOnlyCollection<string> SortableFields = ["email", "displayName", "createdAtUtc"];

    /// <summary>
    /// Los tramos que ofrece la interfaz, y por los que el endpoint de conteos devuelve un número. Viven acá
    /// y no en el front: el que cuenta y el que dibuja las opciones tienen que estar de acuerdo, y un tramo
    /// dibujado sin conteo se ve como un filtro roto.
    /// </summary>
    public static readonly IReadOnlyCollection<int> CreatedWithinOptions = [7, 30];

    public bool? IsActive { get; init; }

    /// <summary>
    /// Nombre del rol. Un rol que no existe devuelve cero resultados y no un 400: el código de respuesta
    /// diría si ese nombre de rol existe o no, y eso no se lo contamos a nadie por el camino de un filtro.
    /// </summary>
    public string? Role { get; init; }

    public int? CreatedWithinDays { get; init; }
}
