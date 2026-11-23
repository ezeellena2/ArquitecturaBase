using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

// RC3-A (2026-11-22): Configuración › WhatsApp keeps the time to answer a form, the countries that receive codes and the retention of
// the messages, each with the value it had in appsettings until then.
public sealed class WhatsAppSettingsTests
{
    [Fact]
    public void New_settings_keep_the_values_of_before_the_screen()
    {
        var settings = WhatsAppSettings.Create();

        Assert.Equal(24, settings.FlowSessionLifetimeHours);
        Assert.Equal(["AR"], settings.AllowedCountries);
        Assert.Equal(90, settings.MessageRetentionDays);
        Assert.Equal(0, settings.Revision);
        Assert.Equal(WhatsAppSettings.SingletonId, settings.Id);
    }

    [Fact]
    public void Saving_applies_the_three_values_and_advances_the_revision()
    {
        var settings = WhatsAppSettings.Create();

        var saved = settings.Update(48, ["UY", "AR"], 30);

        Assert.True(saved.IsSuccess);
        Assert.Equal(48, settings.FlowSessionLifetimeHours);
        Assert.Equal(["AR", "UY"], settings.AllowedCountries);
        Assert.Equal(30, settings.MessageRetentionDays);
        Assert.Equal(1, settings.Revision);
    }

    [Fact]
    public void The_limits_of_each_range_are_valid()
    {
        var settings = WhatsAppSettings.Create();

        Assert.True(settings.Update(WhatsAppSettings.MinFlowSessionLifetimeHours, ["AR"], WhatsAppSettings.MinMessageRetentionDays).IsSuccess);
        Assert.True(settings.Update(WhatsAppSettings.MaxFlowSessionLifetimeHours, ["AR"], WhatsAppSettings.MaxMessageRetentionDays).IsSuccess);
        Assert.Equal(2, settings.Revision);
    }

    [Theory]
    [InlineData(WhatsAppSettings.MinFlowSessionLifetimeHours - 1, 90)]
    [InlineData(WhatsAppSettings.MaxFlowSessionLifetimeHours + 1, 90)]
    [InlineData(24, WhatsAppSettings.MinMessageRetentionDays - 1)]
    [InlineData(24, WhatsAppSettings.MaxMessageRetentionDays + 1)]
    public void A_value_out_of_its_range_changes_nothing(int hours, int days)
    {
        var settings = WhatsAppSettings.Create();

        var saved = settings.Update(hours, ["AR"], days);

        Assert.Equal(WhatsAppSettingsErrors.Invalid, saved.Error);
        Assert.Equal(24, settings.FlowSessionLifetimeHours);
        Assert.Equal(90, settings.MessageRetentionDays);
        Assert.Equal(0, settings.Revision);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("ARG")]
    [InlineData("A1")]
    [InlineData("")]
    [InlineData(" AR")]
    public void A_country_is_its_upper_case_ISO_code(string country)
    {
        var settings = WhatsAppSettings.Create();

        var saved = settings.Update(24, ["UY", country], 90);

        Assert.Equal(WhatsAppSettingsErrors.Invalid, saved.Error);
        Assert.Equal(["AR"], settings.AllowedCountries);
    }

    [Fact]
    public void At_least_one_country_receives_codes()
    {
        var settings = WhatsAppSettings.Create();

        Assert.Equal(WhatsAppSettingsErrors.Invalid, settings.Update(24, [], 90).Error);
    }

    [Fact]
    public void A_repeated_country_is_kept_once()
    {
        var settings = WhatsAppSettings.Create();

        settings.Update(24, ["UY", "AR", "UY"], 90);

        Assert.Equal(["AR", "UY"], settings.AllowedCountries);
    }

    [Fact]
    public void Too_many_countries_change_nothing()
    {
        var settings = WhatsAppSettings.Create();
        var countries = Enumerable.Range(0, WhatsAppSettings.MaxAllowedCountries + 1)
            .Select(index => $"{(char)('A' + index / 26 % 26)}{(char)('A' + index % 26)}").ToArray();

        Assert.Equal(WhatsAppSettingsErrors.Invalid, settings.Update(24, countries, 90).Error);
    }

    [Fact]
    public void A_revision_read_before_another_save_conflicts()
    {
        var settings = WhatsAppSettings.Create();
        settings.Update(48, ["AR"], 90);

        Assert.True(settings.EnsureRevision(1).IsSuccess);
        Assert.Equal(WhatsAppSettingsErrors.RevisionConflict, settings.EnsureRevision(0).Error);
    }

    [Fact]
    public void Allows_reads_the_country_of_a_number_by_its_code()
    {
        var settings = WhatsAppSettings.Create();
        settings.Update(24, ["AR", "UY"], 90);

        Assert.True(settings.Allows("UY"));
        Assert.False(settings.Allows("BR"));
        Assert.False(settings.Allows(null));
    }
}
