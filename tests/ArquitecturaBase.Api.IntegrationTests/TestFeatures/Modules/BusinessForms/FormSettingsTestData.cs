using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

/// <summary>Configuración › Formularios (RC4-A) for a test: saved over today's values, and removed afterwards so the next test reads today's.</summary>
internal static class FormSettingsTestData
{
    public static async Task SaveAsync(ApplicationDbContext db, CancellationToken ct, int maxFileSizeMiB = 25, int dailyRereadsPerAsker = 5,
        int similarDocumentsToSuggest = 3, int proposedExpiryDays = 90, int discardedRetentionDays = 90)
    {
        var stored = await db.Set<BusinessFormsSettings>().SingleOrDefaultAsync(ct);
        var settings = stored ?? BusinessFormsSettings.Create();
        if (settings.Update(settings.Revision, maxFileSizeMiB, dailyRereadsPerAsker, similarDocumentsToSuggest, proposedExpiryDays, discardedRetentionDays).IsFailure)
            throw new InvalidOperationException("The test forms settings are out of their ranges.");
        if (stored is null) db.Add(settings);
        await db.SaveChangesAsync(ct);
    }

    public static async Task ResetAsync(ApplicationDbContext db, CancellationToken ct)
    {
        if (await db.Set<BusinessFormsSettings>().SingleOrDefaultAsync(ct) is not { } settings) return;
        db.Remove(settings);
        await db.SaveChangesAsync(ct);
    }
}
