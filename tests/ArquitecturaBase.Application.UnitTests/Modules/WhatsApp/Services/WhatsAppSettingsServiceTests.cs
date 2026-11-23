using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

/// <summary>Configuración › WhatsApp (RC3-A): read the settings and save the three values with the revision read, 409 if another save came first.</summary>
public sealed class WhatsAppSettingsServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Before_the_first_save_it_reads_the_values_of_before_the_screen()
    {
        var host = new Host();

        var settings = await host.Service.GetAsync(Ct);

        Assert.True(settings.IsSuccess);
        Assert.Equal((0L, 24, 90), (settings.Value.Revision, settings.Value.FlowSessionLifetimeHours, settings.Value.MessageRetentionDays));
        Assert.Equal(["AR"], settings.Value.AllowedCountries);
        Assert.Equal(0, host.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task The_first_save_creates_the_settings_and_reads_them_back()
    {
        var host = new Host();

        var saved = await host.Service.UpdateAsync(new(0, 48, ["UY", "AR"], 30), Ct);

        Assert.True(saved.IsSuccess);
        Assert.Equal((1L, 48, 30), (saved.Value.Revision, saved.Value.FlowSessionLifetimeHours, saved.Value.MessageRetentionDays));
        Assert.Equal(["AR", "UY"], saved.Value.AllowedCountries);
        Assert.Single(host.Settings.Added);
        Assert.Equal(1, host.Settings.Locks);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_later_save_changes_the_saved_row_with_its_revision()
    {
        var host = new Host();
        await host.Service.UpdateAsync(new(0, 48, ["AR"], 30), Ct);

        var saved = await host.Service.UpdateAsync(new(1, 12, ["AR", "CL"], 60), Ct);

        Assert.True(saved.IsSuccess);
        Assert.Equal((2L, 12, 60), (saved.Value.Revision, saved.Value.FlowSessionLifetimeHours, saved.Value.MessageRetentionDays));
        Assert.Single(host.Settings.Added);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task A_revision_read_before_another_save_answers_a_conflict(long expected)
    {
        var host = new Host();
        if (expected == 1) await host.Service.UpdateAsync(new(0, 48, ["AR"], 30), Ct);

        var conflict = await host.Service.UpdateAsync(new(expected == 1 ? 0 : 5, 12, ["AR"], 60), Ct);

        Assert.Equal(WhatsAppSettingsErrors.RevisionConflict, conflict.Error);
    }

    [Theory]
    [InlineData(0, 90, "flowSessionLifetimeHours")]
    [InlineData(169, 90, "flowSessionLifetimeHours")]
    [InlineData(24, 0, "messageRetentionDays")]
    [InlineData(24, 3651, "messageRetentionDays")]
    public async Task A_value_out_of_its_range_answers_at_its_path(int hours, int days, string path)
    {
        var host = new Host();

        var invalid = await host.Service.UpdateAsync(new(0, hours, ["AR"], days), Ct);

        var validation = Assert.IsType<ValidationError>(invalid.Error);
        Assert.Contains(path, validation.Errors.Keys);
        Assert.Equal(0, host.UnitOfWork.Transactions);
    }

    public static TheoryData<string[]> InvalidCountries => new() { Array.Empty<string>(), new[] { "ar" }, new[] { "AR", "ARG" } };

    [Theory]
    [MemberData(nameof(InvalidCountries))]
    public async Task The_countries_are_upper_case_ISO_codes_and_at_least_one(string[] countries)
    {
        var host = new Host();

        var invalid = await host.Service.UpdateAsync(new(0, 24, countries, 90), Ct);

        var validation = Assert.IsType<ValidationError>(invalid.Error);
        Assert.Contains(validation.Errors.Keys, key => key.StartsWith("allowedCountries", StringComparison.Ordinal));
        Assert.Equal(0, host.UnitOfWork.Transactions);
    }

    private sealed class Host
    {
        public FakeWhatsAppSettings Settings { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public WhatsAppSettingsService Service { get; }

        public Host() => Service = new(Settings, Settings, RequestValidators.For(new UpdateWhatsAppSettingsRequestValidator()), UnitOfWork,
            NullLogger<WhatsAppSettingsService>.Instance);
    }
}
