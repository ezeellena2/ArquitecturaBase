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
        var palette = new AppearancePalette("#335f85", "#243b53", "#d9e8f5", "#eef4fa", "#ffc10e");
        Assert.True(settings.Update(null, null, null, null, palette).IsSuccess);
        Assert.Equal("#335F85", settings.PrimaryColor);
        Assert.Equal("#243B53", settings.MenuColor);
        Assert.Equal("#D9E8F5", settings.HeaderColor);
        Assert.Equal("#EEF4FA", settings.AlternateRowColor);
        Assert.Equal("#FFC10E", settings.MenuAccentColor);
        Assert.Equal("#FFC10E", settings.EffectiveMenuAccentColor);
        Assert.Equal("es", settings.DefaultCulture);
        Assert.Equal(2, settings.Revision);
    }

    // ME3-B: without a saved accent, the one the front derived from the menu until 2026-11-27, now readable as text.
    [Fact]
    public void Without_a_saved_accent_the_effective_one_derives_from_the_menu()
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);

        Assert.Null(settings.MenuAccentColor);
        Assert.Equal(AppearancePalette.InitialMenuAccentColor, settings.EffectiveMenuAccentColor);
        Assert.Equal("#D3D7D5", AppearancePalette.DeriveMenuAccent("#24352B"));
        Assert.True(AppearancePalette.ContrastRatio(AppearancePalette.DeriveMenuAccent("#6B7280"), "#6B7280") >= 4.5);
        Assert.True(AppearancePalette.ContrastRatio(AppearancePalette.DeriveMenuAccent("#F4F6F0"), "#F4F6F0") >= 4.5);
    }

    [Theory]
    [InlineData("#24352B", "#2B4150")]
    [InlineData("#223340", "#3A4C59")]
    [InlineData("#FFFFFF", "#FFC10E")]
    public void An_accent_that_cannot_be_read_on_the_menu_keeps_every_color(string menu, string accent)
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);

        var result = settings.Update(null, null, null, null, new AppearancePalette("#2B4150", menu, "#F6F2E9", "#FBF7EE", accent));

        Assert.Equal(SettingsErrors.AppearanceInvalid, result.Error);
        Assert.Equal(AppearancePalette.InitialMenuColor, settings.MenuColor);
        Assert.Null(settings.MenuAccentColor);
        Assert.Equal(1, settings.Revision);
    }

    [Fact]
    public void The_contrast_ratio_follows_wcag()
    {
        Assert.Equal(21, AppearancePalette.ContrastRatio("#000000", "#FFFFFF"), 2);
        Assert.Equal(1, AppearancePalette.ContrastRatio("#FFC10E", "#FFC10E"), 2);
        Assert.Equal(7.98, AppearancePalette.ContrastRatio("#FFC10E", "#223340"), 2);
    }

    // ME4: one font for the whole application, from a closed list.
    [Fact]
    public void The_font_is_one_of_the_list_and_changes_with_one_revision()
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);
        Assert.Equal("public-sans", settings.Font);

        Assert.True(settings.Update(null, null, null, null, font: "hanken-grotesk").IsSuccess);

        Assert.Equal("hanken-grotesk", settings.Font);
        Assert.Equal(2, settings.Revision);
        Assert.Equal(["public-sans", "inter", "ibm-plex-sans", "source-sans-3", "hanken-grotesk", "nunito-sans"], AppearanceFont.Supported);
    }

    [Theory]
    [InlineData("comic-sans")]
    [InlineData("Inter")]
    [InlineData("")]
    public void A_font_out_of_the_list_keeps_every_value(string font)
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);

        var result = settings.Update("en", null, null, null, font: font);

        Assert.Equal(SettingsErrors.AppearanceInvalid, result.Error);
        Assert.Equal(("public-sans", "es", 1L), (settings.Font, settings.DefaultCulture, settings.Revision));
    }

    // ME2: the logo and the icon are files of the installation's store, one new key per upload.
    [Fact]
    public void A_logo_and_an_icon_are_set_and_cleared_with_one_revision_each()
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);
        var logo = BrandImage.NewKey(BrandImageKind.Logo, Guid.Parse("0193a4c2-7d1e-7c3a-9f00-1a2b3c4d5e6f"), BrandImageFormat.Png);
        var icon = BrandImage.NewKey(BrandImageKind.Icon, Guid.Parse("0193a4c2-7d1e-7c3a-9f00-6f5e4d3c2b1a"), BrandImageFormat.Png);

        Assert.True(settings.SetBrandImage(BrandImageKind.Logo, logo).IsSuccess);
        Assert.True(settings.SetBrandImage(BrandImageKind.Icon, icon).IsSuccess);

        Assert.Equal("branding/logo/0193a4c27d1e7c3a9f001a2b3c4d5e6f.png", settings.LogoFileKey);
        Assert.Equal("branding/icon/0193a4c27d1e7c3a9f006f5e4d3c2b1a.png", settings.IconFileKey);
        Assert.Equal("0193a4c27d1e7c3a9f001a2b3c4d5e6f", BrandImage.VersionOf(logo));
        Assert.Equal(3, settings.Revision);

        Assert.True(settings.SetBrandImage(BrandImageKind.Logo, null).IsSuccess);
        Assert.Null(settings.LogoFileKey);
        Assert.Equal(icon, settings.IconFileKey);
        Assert.Equal(4, settings.Revision);
    }

    [Theory]
    [InlineData("branding/icon/0193a4c27d1e7c3a9f001a2b3c4d5e6f.png")]
    [InlineData("branding/logo/0193a4c27d1e7c3a9f001a2b3c4d5e6f.gif")]
    [InlineData("branding/logo/not-a-guid.png")]
    [InlineData("form-files/0193a4c27d1e7c3a9f001a2b3c4d5e6f.png")]
    [InlineData("")]
    public void A_key_that_is_not_a_logo_key_is_refused(string key)
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);

        Assert.Equal(SettingsErrors.LogoInvalid, settings.SetBrandImage(BrandImageKind.Logo, key).Error);
        Assert.Null(settings.LogoFileKey);
        Assert.Equal(1, settings.Revision);
    }

    [Fact]
    public void A_jpeg_logo_keeps_its_type_and_an_icon_is_only_png()
    {
        var id = Guid.Parse("0193a4c2-7d1e-7c3a-9f00-1a2b3c4d5e6f");
        var jpeg = BrandImage.NewKey(BrandImageKind.Logo, id, BrandImageFormat.Jpeg);

        Assert.Equal("branding/logo/0193a4c27d1e7c3a9f001a2b3c4d5e6f.jpg", jpeg);
        Assert.Equal("image/jpeg", BrandImage.ContentTypeOf(jpeg));
        Assert.True(BrandImage.Allows(BrandImageKind.Logo, BrandImageFormat.Jpeg, 400, 112));
        Assert.False(BrandImage.Allows(BrandImageKind.Icon, BrandImageFormat.Jpeg, 192, 192));
        Assert.Equal(SettingsErrors.IconInvalid, SystemSettings.Create(RegistrationMode.InviteOnly)
            .SetBrandImage(BrandImageKind.Icon, BrandImage.NewKey(BrandImageKind.Icon, id, BrandImageFormat.Jpeg)).Error);
    }

    [Theory]
    [InlineData(BrandImageKind.Logo, 400, 112, true)]
    [InlineData(BrandImageKind.Logo, 112, 112, true)]
    [InlineData(BrandImageKind.Logo, 112, 400, false)]
    [InlineData(BrandImageKind.Logo, 4001, 112, false)]
    [InlineData(BrandImageKind.Icon, 64, 64, true)]
    [InlineData(BrandImageKind.Icon, 1024, 1024, true)]
    [InlineData(BrandImageKind.Icon, 63, 63, false)]
    [InlineData(BrandImageKind.Icon, 192, 190, false)]
    [InlineData(BrandImageKind.Icon, 1025, 1025, false)]
    public void Each_image_has_its_shape_and_its_limits(BrandImageKind kind, int width, int height, bool allowed) =>
        Assert.Equal(allowed, BrandImage.Allows(kind, BrandImageFormat.Png, width, height));

    [Theory]
    [InlineData("#FFF")]
    [InlineData("#GG0000")]
    [InlineData("#11223344")]
    [InlineData("url(invalid)")]
    [InlineData(null)]
    public void Invalid_appearance_keeps_all_colors_and_general_settings(string? color)
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);
        var invalid = new AppearancePalette("#FFFFFF", color!, "#000000", "#FFFFFF", "#FFFFFF");
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

    // RC6-B: the name and the sender start as the installation had them in Email:AppName and Email:Smtp:FromName; ME2: no
    // logo or icon until the administrator uploads them; RC1-A: the sign-in data keep the periods decided on 2026-11-14.
    [Fact]
    public void New_installations_keep_the_current_name_sender_and_retention_periods_without_logo()
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);

        Assert.Equal(("Acceso", "Acceso"), (settings.AppName, settings.EmailSenderName));
        Assert.Null(settings.LogoFileKey);
        Assert.Null(settings.IconFileKey);
        Assert.Equal((12, 30, 30), (settings.LoginAuditRetentionMonths, settings.LoginCodeRetentionDays, settings.InvitationRetentionDays));
    }

    [Fact]
    public void Branding_is_trimmed_and_one_revision_advances()
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);
        Assert.True(settings.Update(null, null, null, null, branding: new SystemBranding("  Campo  ", " Campo Avisos ")).IsSuccess);
        Assert.Equal(("Campo", "Campo Avisos"), (settings.AppName, settings.EmailSenderName));
        Assert.Equal("es", settings.DefaultCulture);
        Assert.Equal(2, settings.Revision);
    }

    [Theory]
    [InlineData("", "Acceso")]
    [InlineData("   ", "Acceso")]
    [InlineData("Acceso", "")]
    [InlineData("Dos\nlíneas", "Acceso")]
    [InlineData("Acceso", "Con\ttabulación")]
    public void Invalid_branding_keeps_every_value_and_the_revision(string appName, string senderName)
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);

        var result = settings.Update("en", null, null, null, branding: new SystemBranding(appName, senderName));

        Assert.Equal(SettingsErrors.BrandingInvalid, result.Error);
        Assert.Equal(("Acceso", "Acceso"), (settings.AppName, settings.EmailSenderName));
        Assert.Equal("es", settings.DefaultCulture);
        Assert.Equal(1, settings.Revision);
    }

    [Fact]
    public void Names_have_a_length_limit()
    {
        Assert.True(SystemBranding.IsValidName(new string('a', SystemBranding.MaxNameLength)));
        Assert.False(SystemBranding.IsValidName(new string('a', SystemBranding.MaxNameLength + 1)));
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
        var palette = new AppearancePalette("#335F85", "#243B53", "#D9E8F5", "#EEF4FA", "#FFC10E");

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
