using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppFormAppPreviewTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Expired_preview_removes_private_session_without_failing_the_test()
    {
        var statePath = Path.Combine(Path.GetTempPath(), "forms-preview-" + Guid.NewGuid().ToString("N") + ".json");
        var preview = new WhatsAppFormAppPreview("", statePath, TimeSpan.FromMilliseconds(100));
        try
        {
            await preview.HoldAsync(factory, factory, [], factory.Clock, new ControlledWhatsAppSendClient(),
                TestContext.Current.CancellationToken);
            Assert.False(File.Exists(statePath));
        }
        finally
        {
            File.Delete(statePath);
        }
    }
}
