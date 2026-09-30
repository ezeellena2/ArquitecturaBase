using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

internal sealed class SystemCultureOverride(ApiFactory factory, string previous) : IAsyncDisposable
{
    public static async Task<SystemCultureOverride> SetAsync(ApiFactory factory, string culture)
    {
        var previous = await factory.ExecuteScopeAsync(async services =>
        {
            var settings = services.GetRequiredService<ISystemSettingsService>();
            var current = await settings.GetAsync(TestContext.Current.CancellationToken);
            var result = await settings.UpdateSectionAsync(new UpdateSystemSettingsSectionRequest(
                current.Value.Revision, DefaultCulture: culture), TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess);
            return current.Value.DefaultCulture;
        });
        return new(factory, previous);
    }

    public async ValueTask DisposeAsync() => await factory.ExecuteScopeAsync(async services =>
    {
        var settings = services.GetRequiredService<ISystemSettingsService>();
        var current = await settings.GetAsync(TestContext.Current.CancellationToken);
        var result = await settings.UpdateSectionAsync(new UpdateSystemSettingsSectionRequest(
            current.Value.Revision, DefaultCulture: previous), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
    });
}
