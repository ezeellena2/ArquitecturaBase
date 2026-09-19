using System.Linq.Expressions;
using ArquitecturaBase.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Extensions;

internal static class ModelBuilderExtensions
{
    /// <summary>
    /// Filtro global: las entidades ISoftDeletable marcadas como borradas no aparecen en las consultas.
    /// Para verlas: IgnoreQueryFilters().
    /// </summary>
    public static ModelBuilder ApplySoftDeleteQueryFilter(this ModelBuilder modelBuilder)
    {
        var softDeletableRoots = modelBuilder.Model.GetEntityTypes()
            .Where(entityType => entityType.BaseType is null && typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            .ToList();

        foreach (var entityType in softDeletableRoots)
        {
            var entity = Expression.Parameter(entityType.ClrType, "entity");
            var notDeleted = Expression.Lambda(
                Expression.Not(Expression.Property(entity, nameof(ISoftDeletable.IsDeleted))),
                entity);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(notDeleted);
        }

        return modelBuilder;
    }
}
