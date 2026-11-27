using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

/// <summary>
/// Consumo de WhatsApp (T7, PR14 to PR16): the month is the business's, from the current one back to eleven before; without the account or the
/// number nothing is asked, and Meta is asked for the business month up to now with the digits of the installation's number.
/// </summary>
public sealed class WhatsAppUsageServiceTests
{
    // 27/11/2026 12:00 in Buenos Aires (UTC-3), the business zone by default.
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 15, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, "5493406401234")]
    [InlineData(true, null)]
    public async Task Without_the_account_or_the_number_it_says_so_without_asking_meta(bool accountConfigured, string? displayPhoneNumber)
    {
        var host = new Host(accountConfigured, displayPhoneNumber);

        var usage = await host.Service.GetAsync(new("2026-11"), Ct);

        Assert.True(usage.IsSuccess);
        Assert.Equal(("2026-11", false, null, null), (usage.Value.Month, usage.Value.Configured, usage.Value.DisplayPhoneNumber, usage.Value.Currency));
        Assert.Empty(usage.Value.Categories);
        Assert.Equal((0L, 0m), (usage.Value.TotalDelivered, usage.Value.TotalCost));
        Assert.Empty(host.Analytics.Reads);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    [InlineData("11-2026")]
    [InlineData("2026-11-01")]
    public async Task A_month_that_is_missing_or_not_yyyy_mm_is_answered_at_its_field(string? month)
    {
        var host = new Host();

        var usage = await host.Service.GetAsync(new(month), Ct);

        var error = Assert.IsType<ValidationError>(usage.Error);
        Assert.Equal(WhatsAppUsageErrors.MonthInvalid.Code, error.Code);
        Assert.Equal(["month"], error.Errors.Keys);
        Assert.Empty(host.Analytics.Reads);
    }

    [Theory]
    [InlineData("2026-12")]
    [InlineData("2025-11")]
    [InlineData("1999-01")]
    public async Task A_month_after_the_current_one_or_more_than_eleven_before_is_out_of_range(string month)
    {
        var host = new Host();

        var usage = await host.Service.GetAsync(new(month), Ct);

        var error = Assert.IsType<ValidationError>(usage.Error);
        Assert.Equal(WhatsAppUsageErrors.MonthOutOfRange.Code, error.Code);
        Assert.Equal(["month"], error.Errors.Keys);
        Assert.Empty(host.Analytics.Reads);
    }

    [Fact]
    public async Task The_current_month_is_the_business_one_even_when_utc_is_already_in_the_next()
    {
        // 01/12/2026 02:00 UTC is still the 30th of November in Buenos Aires.
        var host = new Host(now: new DateTimeOffset(2026, 12, 1, 2, 0, 0, TimeSpan.Zero));

        var december = await host.Service.GetAsync(new("2026-12"), Ct);
        var november = await host.Service.GetAsync(new("2026-11"), Ct);
        var oldest = await host.Service.GetAsync(new("2025-12"), Ct);

        Assert.Equal(WhatsAppUsageErrors.MonthOutOfRange.Code, december.Error.Code);
        Assert.True(november.IsSuccess);
        Assert.True(oldest.IsSuccess);
        Assert.Equal(new DateTime(2026, 12, 1, 2, 0, 0, DateTimeKind.Utc), host.Analytics.Reads[0].EndUtc);
    }

    [Fact]
    public async Task A_past_month_asks_meta_for_the_whole_business_month_with_the_digits_of_the_number()
    {
        var host = new Host();
        host.Analytics.Next = new WhatsAppPricingAnalytics("USD", [new("SERVICE", 2480, 0m), new("MARKETING", 120, 7.42m)]);

        var usage = await host.Service.GetAsync(new("2026-10"), Ct);

        var read = Assert.Single(host.Analytics.Reads);
        Assert.Equal("5493406401234", read.PhoneNumber);
        Assert.Equal(new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc), read.StartUtc);
        Assert.Equal(new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc), read.EndUtc);
        Assert.True(usage.IsSuccess);
        Assert.Equal(("2026-10", true, "formatted +5493406401234", "USD"),
            (usage.Value.Month, usage.Value.Configured, usage.Value.DisplayPhoneNumber, usage.Value.Currency));
        Assert.Equal([new("MARKETING", 120, 7.42m), new WhatsAppUsageCategoryResponse("SERVICE", 2480, 0m)], usage.Value.Categories);
        Assert.Equal((2600L, 7.42m), (usage.Value.TotalDelivered, usage.Value.TotalCost));
    }

    [Fact]
    public async Task The_current_month_asks_meta_up_to_now()
    {
        var host = new Host();

        var usage = await host.Service.GetAsync(new("2026-11"), Ct);

        Assert.True(usage.IsSuccess);
        var read = Assert.Single(host.Analytics.Reads);
        Assert.Equal((new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc), Now.UtcDateTime), (read.StartUtc, read.EndUtc));
    }

    [Fact]
    public async Task When_meta_does_not_answer_the_usage_is_unavailable()
    {
        var host = new Host();
        host.Analytics.Next = WhatsAppUsageErrors.Unavailable;

        var usage = await host.Service.GetAsync(new("2026-11"), Ct);

        Assert.Equal(WhatsAppUsageErrors.Unavailable, usage.Error);
        Assert.Single(host.Analytics.Reads);
    }

    private sealed class Host
    {
        public Host(bool accountConfigured = true, string? displayPhoneNumber = "5493406401234", DateTimeOffset? now = null)
        {
            Analytics = new FakePricingAnalytics(accountConfigured);
            Service = new(Analytics, new FakeSystemSettingsReader(), Options.Create(new WhatsAppLoginOptions { DisplayPhoneNumber = displayPhoneNumber }),
                new FakePhoneNumberParser(), new FakeTimeProvider(now ?? Now), NullLogger<WhatsAppUsageService>.Instance);
        }

        public FakePricingAnalytics Analytics { get; }

        public WhatsAppUsageService Service { get; }
    }

    private sealed class FakePricingAnalytics(bool configured) : IWhatsAppPricingAnalyticsClient
    {
        public List<(string PhoneNumber, DateTime StartUtc, DateTime EndUtc)> Reads { get; } = [];

        public Result<WhatsAppPricingAnalytics> Next { get; set; } = new WhatsAppPricingAnalytics("USD", []);

        public bool IsConfigured => configured;

        public Task<Result<WhatsAppPricingAnalytics>> ReadAsync(string phoneNumber, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken)
        {
            Reads.Add((phoneNumber, startUtc, endUtc));
            return Task.FromResult(Next);
        }
    }
}
