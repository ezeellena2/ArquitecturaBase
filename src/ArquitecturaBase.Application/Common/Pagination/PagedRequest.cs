namespace ArquitecturaBase.Application.Common.Pagination;

/// <summary>
/// Base de las consultas paginadas: <c>?page=2&amp;pageSize=20&amp;sort=-createdAtUtc&amp;search=juan</c>.
/// Cada consulta declara su lista blanca de campos ordenables y la valida con PagedRequestValidator.
/// </summary>
public abstract record PagedRequest
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = DefaultPage;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Campo por el que se ordena; con "-" adelante, descendente.</summary>
    public string? Sort { get; init; }

    public string? Search { get; init; }
}
