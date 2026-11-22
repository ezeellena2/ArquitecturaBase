using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace ArquitecturaBase.Infrastructure.Persistence.Extensions;

public static class QueryableExtensions
{
    /// <summary>
    /// Ordena por un campo de la lista blanca (sin distinguir mayúsculas) con la regla de todas las tablas (OT1-C, 2026-11-22):
    /// lo vacío va al final en los dos sentidos; un enum (un estado, un tipo) ordena por su valor, que es el orden en que se
    /// declaró, también si se guarda como texto; un campo con varias claves se declara con un tipo anónimo
    /// (<c>row =&gt; new { row.Prefix, row.Number }</c>) y queda vacío si le falta alguna; en una celda vacía todas sus claves son
    /// nulas, para que las filas vacías empaten y sigan el orden de siempre. Con el mismo valor, las filas siguen
    /// el orden de siempre de la lista (<paramref name="defaultSort"/>) y al final <paramref name="tieBreaker"/>, que debe ser
    /// una clave única: sin desempate, dos páginas consecutivas (cada una un LIMIT/OFFSET independiente) pueden repetir o
    /// saltear filas. Sin orden pedido, usa <paramref name="defaultSort"/>. Un campo fuera de la lista es un error de
    /// programación: el validador de la consulta ya tuvo que rechazarlo.
    /// </summary>
    public static IQueryable<T> ApplySort<T>(
        this IQueryable<T> query,
        SortDescriptor? sort,
        IReadOnlyDictionary<string, Expression<Func<T, object?>>> sortableFields,
        SortDescriptor defaultSort,
        Expression<Func<T, object?>> tieBreaker)
    {
        ArgumentNullException.ThrowIfNull(sortableFields);
        ArgumentNullException.ThrowIfNull(defaultSort);

        // Pedir el mismo campo del orden de siempre no lo repite: manda el sentido pedido.
        SortKey<T>[] usual = sort is not null && string.Equals(sort.Field, defaultSort.Field, StringComparison.OrdinalIgnoreCase)
            ? []
            : [new SortKey<T>(Field(sortableFields, defaultSort.Field), defaultSort.Descending)];

        return query.ApplySortWithDefaultOrder(sort, sortableFields, usual, tieBreaker);
    }

    /// <summary>
    /// Como <c>ApplySort</c>, para un listado cuyo orden de siempre tiene varias claves (<paramref name="defaultOrder"/>):
    /// se usa sin orden pedido y para desempatar uno pedido, antes de <paramref name="tieBreaker"/>.
    /// </summary>
    public static IQueryable<T> ApplySortWithDefaultOrder<T>(
        this IQueryable<T> query,
        SortDescriptor? sort,
        IReadOnlyDictionary<string, Expression<Func<T, object?>>> sortableFields,
        IReadOnlyList<SortKey<T>> defaultOrder,
        Expression<Func<T, object?>> tieBreaker)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sortableFields);
        ArgumentNullException.ThrowIfNull(defaultOrder);
        ArgumentNullException.ThrowIfNull(tieBreaker);

        IReadOnlyList<SortKey<T>> keys = sort is null
            ? defaultOrder
            : [new SortKey<T>(Field(sortableFields, sort.Field), sort.Descending), .. defaultOrder];
        // PostgreSQL (the only database of the template) already puts NULL last in an ASC: there an extra «is null» term is
        // only needed in a DESC or for a field with several keys, and it would repeat the key, which may be a subquery.
        var nullsLastWhenAscending = query.Provider is IAsyncQueryProvider;
        IOrderedQueryable<T>? ordered = null;
        foreach (var key in keys)
        {
            ordered = OrderBy(query, ordered, key, nullsLastWhenAscending);
        }

        return ordered is null ? query.OrderBy(tieBreaker) : ordered.ThenBy(tieBreaker);
    }

    /// <summary>
    /// Cuenta el total y trae solo la página pedida (Skip/Take). Page y PageSize fuera de rango son un
    /// error de programación (el validador de la consulta ya tuvo que rechazarlos): se rechazan acá para
    /// no llegar a Postgres como un OFFSET negativo o desbordado.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.PageSize, 1);
        // Defensa en profundidad: si alguien se salta el validador, evita que (Page - 1) * PageSize desborde int.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.Page, int.MaxValue / request.PageSize);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, request.Page, request.PageSize, totalCount);
    }

    private static Expression<Func<T, object?>> Field<T>(IReadOnlyDictionary<string, Expression<Func<T, object?>>> sortableFields, string field) =>
        sortableFields.FirstOrDefault(candidate => string.Equals(candidate.Key, field, StringComparison.OrdinalIgnoreCase)).Value
        ?? throw new InvalidOperationException($"'{field}' is not in the sort whitelist.");

    // One field may be several keys (an anonymous type). It is empty when any of them is null, and an empty field goes last
    // in both directions: Postgres alone would put NULL first in a DESC.
    private static IOrderedQueryable<T> OrderBy<T>(IQueryable<T> query, IOrderedQueryable<T>? ordered, SortKey<T> key, bool nullsLastWhenAscending)
    {
        var parameter = key.Key.Parameters[0];
        var parts = Parts(key.Key.Body);
        var empty = parts.Where(CanBeEmpty)
            .Select(part => (Expression)Expression.Equal(part, Expression.Constant(null, part.Type)))
            .Aggregate((Expression?)null, (all, next) => all is null ? next : Expression.OrElse(all, next));
        if (empty is not null && (key.Descending || parts.Length > 1 || !nullsLastWhenAscending))
        {
            ordered = Then(query, ordered, Lambda<T>(empty, parameter), descending: false);
        }

        foreach (var part in parts)
        {
            ordered = Then(query, ordered, Lambda<T>(Ordinal(part), parameter), key.Descending);
        }

        return ordered ?? throw new InvalidOperationException("A sort key has no parts.");
    }

    private static IOrderedQueryable<T> Then<T>(IQueryable<T> query, IOrderedQueryable<T>? ordered, Expression<Func<T, object?>> key, bool descending) =>
        ordered is null
            ? descending ? query.OrderByDescending(key) : query.OrderBy(key)
            : descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key);

    private static Expression[] Parts(Expression body)
    {
        var unwrapped = body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert ? convert.Operand : body;

        return unwrapped is NewExpression { Members: not null } anonymous ? [.. anonymous.Arguments] : [unwrapped];
    }

    private static bool CanBeEmpty(Expression part) => !part.Type.IsValueType || Nullable.GetUnderlyingType(part.Type) is not null;

    // An enum orders by its value, the order it was declared in, with a CASE: stored as text, the column alone would sort
    // its English names alphabetically.
    private static Expression Ordinal(Expression part)
    {
        var type = Nullable.GetUnderlyingType(part.Type) ?? part.Type;
        if (!type.IsEnum)
        {
            return part;
        }

        var values = Enum.GetValues(type);
        Expression ordinal = Expression.Constant(values.Length);
        for (var index = values.Length - 1; index >= 0; index--)
        {
            ordinal = Expression.Condition(Expression.Equal(part, Expression.Constant(values.GetValue(index), part.Type)), Expression.Constant(index), ordinal);
        }

        return ordinal;
    }

    private static Expression<Func<T, object?>> Lambda<T>(Expression body, ParameterExpression parameter) =>
        Expression.Lambda<Func<T, object?>>(body.Type == typeof(object) ? body : Expression.Convert(body, typeof(object)), parameter);
}
