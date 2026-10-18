using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppFormAppPreviewSettingsTests
{
    [Fact]
    public void Stage_five_requires_its_own_opt_in_instead_of_activating_the_legacy_preview()
    {
        var requested = new List<string>();
        var settings = new Dictionary<string, string>
        {
            ["FORM_APP_PREVIEW_WEB_ROOT"] = "legacy-web-root",
            ["FORM_APP_PREVIEW_STATE"] = "legacy-state"
        };
        var preview = WhatsAppFormAppPreview.FromStageFiveEnvironment(name =>
        {
            requested.Add(name);
            return settings.GetValueOrDefault(name);
        });
        Assert.Null(preview);
        Assert.Equal(["STAGE5_APP_PREVIEW_WEB_ROOT", "STAGE5_APP_PREVIEW_STATE"], requested);
        settings["STAGE5_APP_PREVIEW_WEB_ROOT"] = "stage-five-web-root";
        settings["STAGE5_APP_PREVIEW_STATE"] = "stage-five-state";
        Assert.NotNull(WhatsAppFormAppPreview.FromStageFiveEnvironment(settings.GetValueOrDefault));
    }

    [Theory]
    [InlineData("available", "Available")]
    [InlineData("paused", "Paused")]
    [InlineData("modelNotQualified", "ModelNotQualified")]
    [InlineData("notConfigured", "NotConfigured")]
    [InlineData("notInstalled", "NotInstalled")]
    public void Only_known_local_availability_states_can_control_the_preview(string text, string expected) =>
        Assert.Equal(expected, WhatsAppFormAppPreview.ParseAvailability(text).ToString());

    [Theory]
    [InlineData("0")]
    [InlineData("unknown")]
    [InlineData("")]
    public void An_unknown_availability_control_is_rejected(string text) =>
        Assert.Throws<InvalidOperationException>(() => WhatsAppFormAppPreview.ParseAvailability(text));
}
