using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class BusinessFormsManualAppPreviewTests(ApiFactory factory)
{
    [Fact]
    public async Task Serve_manual_catalog_app_with_real_api_and_permission_profiles()
    {
        var preview = BusinessFormsManualAppPreview.FromEnvironment();
        if (preview is null) return;

        var sessions = await BusinessFormsManualAppPreview.CreateSessionsAsync(factory, TestContext.Current.CancellationToken);
        await using var host = factory.WithWebHostBuilder(preview.Configure);
        BusinessFormsManualAppPreview.Listen(host);
        using var startup = host.CreateClient();
        using var response = await startup.GetAsync("/", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        await preview.HoldAsync(sessions, TestContext.Current.CancellationToken);
    }
}
