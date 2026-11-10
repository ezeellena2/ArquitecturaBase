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

    // What a channel offers (sending, the assistant and suggestions): an active version published in a channel. A version
    // completed only in the app (SA2-A) is loaded in the app and nowhere else.
    internal static IQueryable<BusinessFormVersion> ChannelVersions(ApplicationDbContext dbContext) =>
        Versions(dbContext).Where(version => !version.AppOnly);

    internal static IQueryable<BusinessFormVersion> PublishedVersions(ApplicationDbContext dbContext) =>
        dbContext.Set<BusinessFormVersion>().AsNoTracking().Where(version => version.NameJson != null
            && version.SubmitLabelJson != null && version.QuestionCount != null && version.Culture != null
            && dbContext.Set<BusinessFormPublicationOperation>().Any(operation => operation.DefinitionId == version.DefinitionId
                && operation.VersionId == version.Id && operation.State == FormPublicationState.Published));
}