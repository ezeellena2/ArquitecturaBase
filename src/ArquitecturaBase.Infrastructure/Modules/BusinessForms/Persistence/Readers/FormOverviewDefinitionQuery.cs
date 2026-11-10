using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

/// <summary>
/// The forms of the overview («Formularios»), shared by its reader and by the folders, whose tree and menu (CP1-A, CP5-A) show the
/// same forms.
/// </summary>
internal static class FormOverviewDefinitionQuery
{
    /// <summary>The published forms: an active version whose publication completed.</summary>
    internal static IQueryable<BusinessFormDefinition> Published(ApplicationDbContext dbContext) =>
        dbContext.Set<BusinessFormDefinition>().AsNoTracking()
            .Where(definition => definition.ActiveVersionId != null
                && dbContext.Set<BusinessFormPublicationOperation>().Any(operation => operation.DefinitionId == definition.Id
                    && operation.VersionId == definition.ActiveVersionId && operation.State == FormPublicationState.Published));

    /// <summary>
    /// The forms the overview shows to this person, active or deactivated: with only Decide, those with an open task they can decide,
    /// as the overview does.
    /// </summary>
    internal static IQueryable<Guid> VisibleIds(ApplicationDbContext dbContext, Guid actorUserId, bool canReadRecords, bool canCreate)
    {
        var definitions = from definition in Published(dbContext)
                          join version in dbContext.Set<BusinessFormVersion>().AsNoTracking().Where(version => version.PublishedCultures != null)
                              on definition.ActiveVersionId equals version.Id
                          select definition;
        if (!canReadRecords && !canCreate)
            definitions = definitions.Where(definition => dbContext.Set<BusinessFormTask>().Any(task =>
                task.DefinitionId == definition.Id && task.State == FormTaskState.Open && task.AuthorUserId != actorUserId
                && dbContext.Users.Any(user => user.Id == actorUserId && user.IsActive)
                && dbContext.UserRoles.Any(link => link.UserId == actorUserId && link.RoleId == task.RoleId)));
        return definitions.Select(definition => definition.Id);
    }
}
