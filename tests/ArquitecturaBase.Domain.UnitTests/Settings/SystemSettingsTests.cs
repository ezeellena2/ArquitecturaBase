using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Domain.UnitTests.Settings;

public sealed class SystemSettingsTests
{
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
