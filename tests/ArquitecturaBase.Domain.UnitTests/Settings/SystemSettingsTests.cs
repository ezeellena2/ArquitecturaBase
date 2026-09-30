using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Domain.UnitTests.Settings;

public sealed class SystemSettingsTests
{
    [Fact]
    public void Presentation_defaults_preserve_existing_installation_behavior()
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);
        Assert.Equal("es", settings.DefaultCulture);
        Assert.Equal("America/Argentina/Buenos_Aires", settings.DefaultTimeZoneId);
        Assert.Equal(20, settings.DefaultPageSize);
        Assert.Equal(1, settings.Revision);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Changing_culture_updates_only_that_setting_and_revision(string culture)
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);
        Assert.True(settings.SetDefaultCulture(culture).IsSuccess);
        Assert.Equal(culture, settings.DefaultCulture);
        Assert.Equal(20, settings.DefaultPageSize);
        Assert.Equal(RegistrationMode.InviteOnly, settings.RegistrationMode);
        Assert.Equal(2, settings.Revision);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    public void Invalid_culture_does_not_change_settings(string culture)
    {
        var settings = SystemSettings.Create(RegistrationMode.Open);
        Assert.True(settings.SetDefaultCulture(culture).IsFailure);
        Assert.Equal("es", settings.DefaultCulture);
        Assert.Equal(1, settings.Revision);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(20, true)]
    [InlineData(50, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(25, false)]
    [InlineData(101, false)]
    public void Page_size_accepts_only_the_supported_defaults(int size, bool valid)
    {
        var settings = SystemSettings.Create(RegistrationMode.Open);
        Assert.Equal(valid, settings.SetDefaultPageSize(size).IsSuccess);
        Assert.Equal(valid ? size : 20, settings.DefaultPageSize);
        Assert.Equal(valid ? 2 : 1, settings.Revision);
    }

    [Fact]
    public void Settings_always_use_the_same_fixed_id()
    {
        var settings = SystemSettings.Create(RegistrationMode.Open);

        Assert.Equal(SystemSettings.SingletonId, settings.Id);
        Assert.Equal(RegistrationMode.Open, settings.RegistrationMode);
    }

    [Fact]
    public void Two_instances_share_the_id_so_the_table_can_only_have_one_row()
    {
        var first = SystemSettings.Create(RegistrationMode.InviteOnly);
        var second = SystemSettings.Create(RegistrationMode.Open);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Invite_only_is_the_default_value_of_the_enum()
    {
        Assert.Equal(RegistrationMode.InviteOnly, default(RegistrationMode));
    }

    [Fact]
    public void Registration_mode_can_be_changed()
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);

        settings.SetRegistrationMode(RegistrationMode.Open);

        Assert.Equal(RegistrationMode.Open, settings.RegistrationMode);
    }
}
