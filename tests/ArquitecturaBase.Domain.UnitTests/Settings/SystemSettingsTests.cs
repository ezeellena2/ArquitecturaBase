using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Domain.UnitTests.Settings;

public sealed class SystemSettingsTests
{
    [Fact]
    public void New_installations_use_the_approved_field_palette()
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);
        Assert.Equal("#285A3D", settings.PrimaryColor);
        Assert.Equal("#24352B", settings.MenuColor);
        Assert.Equal("#DFE8D9", settings.HeaderColor);
        Assert.Equal("#F4F6F0", settings.AlternateRowColor);
    }

    [Fact]
    public void Appearance_changes_are_normalized_and_advance_one_revision_without_changing_general_settings()
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);
        var palette = new AppearancePalette("#335f85", "#243b53", "#d9e8f5", "#eef4fa");
        Assert.True(settings.Update(null, null, null, null, palette).IsSuccess);
        Assert.Equal("#335F85", settings.PrimaryColor);
        Assert.Equal("#243B53", settings.MenuColor);
        Assert.Equal("#D9E8F5", settings.HeaderColor);
        Assert.Equal("#EEF4FA", settings.AlternateRowColor);
        Assert.Equal("es", settings.DefaultCulture);
        Assert.Equal(2, settings.Revision);
    }

    [Theory]
    [InlineData("#FFF")]
    [InlineData("#GG0000")]
    [InlineData("#11223344")]
    [InlineData("url(invalid)")]
    [InlineData(null)]
    public void Invalid_appearance_keeps_all_colors_and_general_settings(string? color)
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);
        var invalid = new AppearancePalette("#FFFFFF", color!, "#000000", "#FFFFFF");
        Assert.True(settings.Update("en", "UTC", 50, RegistrationMode.Open, invalid).IsFailure);
        Assert.Equal(AppearancePalette.InitialPrimaryColor, settings.PrimaryColor);
        Assert.Equal(AppearancePalette.InitialMenuColor, settings.MenuColor);
        Assert.Equal(AppearancePalette.InitialHeaderColor, settings.HeaderColor);
        Assert.Equal(AppearancePalette.InitialAlternateRowColor, settings.AlternateRowColor);
        Assert.Equal("es", settings.DefaultCulture);
        Assert.Equal(1, settings.Revision);
    }

    [Fact]
    public void A_group_of_changes_advances_one_revision()
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);
        var result = settings.Update("en", "UTC", 50, RegistrationMode.Open);
        Assert.True(result.IsSuccess);
        Assert.Equal("en", settings.DefaultCulture);
        Assert.Equal("UTC", settings.DefaultTimeZoneId);
        Assert.Equal(50, settings.DefaultPageSize);
        Assert.Equal(RegistrationMode.Open, settings.RegistrationMode);
        Assert.Equal(2, settings.Revision);
    }

    [Theory]
    [InlineData("fr", 50)]
    [InlineData("en", 25)]
    public void An_invalid_group_keeps_all_values_and_revision(string culture, int size)
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);
        Assert.True(settings.Update(culture, "UTC", size, RegistrationMode.Open).IsFailure);
        Assert.Equal("es", settings.DefaultCulture);
        Assert.Equal(SystemSettings.InitialTimeZoneId, settings.DefaultTimeZoneId);
        Assert.Equal(20, settings.DefaultPageSize);
        Assert.Equal(RegistrationMode.InviteOnly, settings.RegistrationMode);
        Assert.Equal(1, settings.Revision);
    }

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

    // RC6-B: the name, the sender and the logo start as the installation had them in Email:AppName, Email:Smtp:FromName and
    // Email:LogoUrl; RC1-A: the sign-in data keep the periods decided on 2026-11-14.
    [Fact]
    public void New_installations_keep_the_current_name_sender_logo_and_retention_periods()
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);

        Assert.Equal(("Acceso", "Acceso", (string?)null), (settings.AppName, settings.EmailSenderName, settings.EmailLogoUrl));
        Assert.Equal((12, 30, 30), (settings.LoginAuditRetentionMonths, settings.LoginCodeRetentionDays, settings.InvitationRetentionDays));
    }

    [Fact]
    public void Branding_is_trimmed_an_empty_logo_means_none_and_one_revision_advances()
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);
        Assert.True(settings.Update(null, null, null, null, branding: new SystemBranding("  Campo  ", " Campo Avisos ",
            " https://example.com/logo.png ")).IsSuccess);
        Assert.Equal(("Campo", "Campo Avisos", (string?)"https://example.com/logo.png"),
            (settings.AppName, settings.EmailSenderName, settings.EmailLogoUrl));
        Assert.Equal(2, settings.Revision);

        Assert.True(settings.Update(null, null, null, null, branding: new SystemBranding("Campo", "Campo", "  ")).IsSuccess);
        Assert.Null(settings.EmailLogoUrl);
        Assert.Equal("es", settings.DefaultCulture);
        Assert.Equal(3, settings.Revision);
    }

    [Theory]
    [InlineData("", "Acceso", null)]
    [InlineData("   ", "Acceso", null)]
    [InlineData("Acceso", "", null)]
    [InlineData("Dos\nlíneas", "Acceso", null)]
    [InlineData("Acceso", "Con\ttabulación", null)]
    [InlineData("Acceso", "Acceso", "http://example.com/logo.png")]
    [InlineData("Acceso", "Acceso", "/logo.png")]
    [InlineData("Acceso", "Acceso", "https://example.com/un logo.png")]
    [InlineData("Acceso", "Acceso", "javascript:alert(1)")]
    public void Invalid_branding_keeps_every_value_and_the_revision(string appName, string senderName, string? logoUrl)
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);

        var result = settings.Update("en", null, null, null, branding: new SystemBranding(appName, senderName, logoUrl));

        Assert.Equal(SettingsErrors.BrandingInvalid, result.Error);
        Assert.Equal(("Acceso", "Acceso", (string?)null), (settings.AppName, settings.EmailSenderName, settings.EmailLogoUrl));
        Assert.Equal("es", settings.DefaultCulture);
        Assert.Equal(1, settings.Revision);
    }

    [Fact]
    public void Names_and_logo_have_a_length_limit()
    {
        Assert.True(SystemBranding.IsValidName(new string('a', SystemBranding.MaxNameLength)));
        Assert.False(SystemBranding.IsValidName(new string('a', SystemBranding.MaxNameLength + 1)));
        var longest = "https://example.com/" + new string('a', SystemBranding.MaxLogoUrlLength - 20);
        Assert.True(SystemBranding.IsValidLogoUrl(longest));
        Assert.False(SystemBranding.IsValidLogoUrl(longest + "a"));
    }

    [Fact]
    public void Retention_periods_change_together_with_one_revision()
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);

        Assert.True(settings.Update(null, null, null, null, retention: new IdentityRetentionPeriods(6, 1, 7)).IsSuccess);

        Assert.Equal((6, 1, 7), (settings.LoginAuditRetentionMonths, settings.LoginCodeRetentionDays, settings.InvitationRetentionDays));
        Assert.Equal(2, settings.Revision);
    }

    [Theory]
    [InlineData(0, 30, 30)]
    [InlineData(121, 30, 30)]
    [InlineData(12, 0, 30)]
    [InlineData(12, 3651, 30)]
    [InlineData(12, 30, 6)]
    [InlineData(12, 30, 3651)]
    public void A_period_out_of_range_keeps_every_value_and_the_revision(int auditMonths, int codeDays, int invitationDays)
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);
        var palette = new AppearancePalette("#335F85", "#243B53", "#D9E8F5", "#EEF4FA");

        var result = settings.Update(null, null, null, null, palette, retention: new IdentityRetentionPeriods(auditMonths, codeDays, invitationDays));

        Assert.Equal(SettingsErrors.RetentionInvalid, result.Error);
        Assert.Equal((12, 30, 30), (settings.LoginAuditRetentionMonths, settings.LoginCodeRetentionDays, settings.InvitationRetentionDays));
        Assert.Equal(AppearancePalette.InitialPrimaryColor, settings.PrimaryColor);
        Assert.Equal(1, settings.Revision);
    }

    [Fact]
    public void The_limits_of_each_period_are_valid()
    {
        Assert.True(new IdentityRetentionPeriods(1, 1, 7).IsValid);
        Assert.True(new IdentityRetentionPeriods(120, 3650, 3650).IsValid);
        Assert.Equal(new IdentityRetentionPeriods(12, 30, 30), IdentityRetentionPeriods.Initial);
    }
}
