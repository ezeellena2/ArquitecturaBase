using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal static class FormOfferableVersionQuery
{
    internal static IQueryable<BusinessFormVersion> Versions(ApplicationDbContext dbContext) =>
        PublishedVersions(dbContext).Where(version => dbContext.Set<BusinessFormDefinition>()
            .Any(definition => definition.Id == version.DefinitionId && definition.ActiveVersionId == version.Id
                && definition.DeactivatedAtUtc == null));

    internal static IQueryable<BusinessFormVersion> PublishedVersions(ApplicationDbContext dbContext) =>
        dbContext.Set<BusinessFormVersion>().AsNoTracking().Where(version => version.NameJson != null
            && version.SubmitLabelJson != null && version.QuestionCount != null && version.Culture != null
            && dbContext.Set<BusinessFormPublicationOperation>().Any(operation => operation.DefinitionId == version.DefinitionId
                && operation.VersionId == version.Id && operation.State == FormPublicationState.Published));
}