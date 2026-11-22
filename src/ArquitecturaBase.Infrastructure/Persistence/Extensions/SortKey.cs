using System.Linq.Expressions;

namespace ArquitecturaBase.Infrastructure.Persistence.Extensions;

/// <summary>
/// Una clave del orden de siempre de un listado, con su sentido. Sirve cuando ese orden tiene varias claves (Formularios:
/// primero lo que espera aprobación, después lo último recibido y el nombre), para ordenar sin un orden pedido y para
/// desempatar uno pedido con <c>ApplySortWithDefaultOrder</c> de <see cref="QueryableExtensions"/>.
/// </summary>
public sealed record SortKey<T>(Expression<Func<T, object?>> Key, bool Descending);
