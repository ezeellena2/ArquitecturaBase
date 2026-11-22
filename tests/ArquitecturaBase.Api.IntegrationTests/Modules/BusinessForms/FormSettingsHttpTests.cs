using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    // RC4-A: Configuración › Formularios needs «Administrar la configuración»; it reads today's values until someone saves, saves with the revision
    // read (409 with a stale one) and refuses a number out of its range, or a member it does not know, on its field.
    [Fact]
    public async Task Form_settings_are_read_and_saved_with_their_revision_by_whoever_manages_the_settings()
    {
        try
        {
            using var anonymous = factory.CreateClient();
            using var designer = await OperatorAsync([BusinessFormsPermissions.Manage]);
            using var admin = await OperatorAsync([Permissions.Settings.Manage]);
            using var missing = await anonymous.GetAsync("/api/forms/settings", Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
            using var denied = await designer.GetAsync("/api/forms/settings", Ct);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var deniedWrite = await designer.PutAsJsonAsync("/api/forms/settings", SettingsBody(0, 10), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);

            using var initial = await admin.GetAsync("/api/forms/settings", Ct);
            Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
            Assert.Equal((25, 5, 3, 90, 90, 0L), Settings(await initial.ReadJsonAsync()));

            using var saved = await admin.PutAsJsonAsync("/api/forms/settings", SettingsBody(0, 10), Ct);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal((10, 7, 4, 30, 180, 1L), Settings(await saved.ReadJsonAsync()));
            using var read = await admin.GetAsync("/api/forms/settings", Ct);
            Assert.Equal((10, 7, 4, 30, 180, 1L), Settings(await read.ReadJsonAsync()));

            using var stale = await admin.PutAsJsonAsync("/api/forms/settings", SettingsBody(0, 12), Ct);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(FormSettingsErrors.RevisionConflict.Code, (await stale.ReadJsonAsync()).GetProperty("code").GetString());
            using var outOfRange = await admin.PutAsJsonAsync("/api/forms/settings", SettingsBody(1, 26), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);
            Assert.True((await outOfRange.ReadJsonAsync()).GetProperty("errors").TryGetProperty("maxFileSizeMiB", out _));
            using var unknown = await admin.PutAsJsonAsync("/api/forms/settings", new { expectedRevision = 1, maxFileSizeMiB = 10, dailyRereadsPerAsker = 7,
                similarDocumentsToSuggest = 4, proposedExpiryDays = 30, discardedRetentionDays = 180, other = 1 }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            Assert.Equal((10, 1L), await factory.ExecuteDbContextAsync(async db =>
            {
                var stored = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Set<BusinessFormsSettings>(), Ct);
                return (stored.MaxFileSizeMiB, stored.Revision);
            }));
        }
        finally
        {
            await factory.ExecuteDbContextAsync(db => FormSettingsTestData.ResetAsync(db, Ct));
        }
    }

    private static object SettingsBody(long revision, int maxFileSizeMiB) => new { expectedRevision = revision, maxFileSizeMiB, dailyRereadsPerAsker = 7,
        similarDocumentsToSuggest = 4, proposedExpiryDays = 30, discardedRetentionDays = 180 };

    private static (int, int, int, int, int, long) Settings(System.Text.Json.JsonElement body) => (body.GetProperty("maxFileSizeMiB").GetInt32(),
        body.GetProperty("dailyRereadsPerAsker").GetInt32(), body.GetProperty("similarDocumentsToSuggest").GetInt32(),
        body.GetProperty("proposedExpiryDays").GetInt32(), body.GetProperty("discardedRetentionDays").GetInt32(), body.GetProperty("revision").GetInt64());
}
