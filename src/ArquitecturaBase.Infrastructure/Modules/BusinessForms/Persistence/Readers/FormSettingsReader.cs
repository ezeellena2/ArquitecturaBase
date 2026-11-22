using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

// RC4-A: Configuración › Formularios as it is now, read on every use.
internal sealed class FormSettingsReader(ApplicationDbContext dbContext) : IFormSettingsReader
{
    public Task<FormSettingsResponse> FindAsync(CancellationToken cancellationToken) => FormSettingsQuery.FindAsync(dbContext, cancellationToken);
}

/// <summary>The saved row of Configuración › Formularios, or today's values at revision 0; the readers that need a value inside their own query read it here.</summary>
internal static class FormSettingsQuery
{
    public static async Task<FormSettingsResponse> FindAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken) =>
        await dbContext.Set<BusinessFormsSettings>().AsNoTracking().Where(settings => settings.Id == BusinessFormsSettings.SingletonId)
            .Select(settings => new FormSettingsResponse(settings.MaxFileSizeMiB, settings.DailyRereadsPerAsker, settings.SimilarDocumentsToSuggest,
                settings.ProposedExpiryDays, settings.DiscardedRetentionDays, settings.Revision))
            .SingleOrDefaultAsync(cancellationToken) ?? FormSettingsResponse.From(BusinessFormsSettings.Create());
}
