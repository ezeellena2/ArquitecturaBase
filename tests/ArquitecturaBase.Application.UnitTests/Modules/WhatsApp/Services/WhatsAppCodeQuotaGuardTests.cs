using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

/// <summary>
/// El tope diario de códigos por WhatsApp (sección 13 del spec del ingreso con WhatsApp): cuenta los que salieron por
/// WhatsApp en las últimas 24 horas, a cualquier número y con cualquier propósito, y en el tope responde cuándo se libera
/// un lugar. Que el ingreso y el perfil lo miren antes del lock del número lo prueban sus servicios
/// (RequestWhatsAppLoginCodeServiceTests y ProfileWhatsAppServiceTests).
/// </summary>
public sealed class WhatsAppCodeQuotaGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryLoginCodeRepository _codes = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeLogger<WhatsAppCodeQuotaGuard> _logger = new();

    [Fact]
    public async Task Below_the_limit_there_is_no_error()
    {
        AddSent("+5493515550101", TimeSpan.FromMinutes(5));

        Assert.Null(await Guard(limit: 10).CheckAsync(Ct));
        Assert.Empty(_logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task At_the_limit_it_answers_too_many_requests_until_the_oldest_leaves_the_window()
    {
        AddSent("+5493515550101", TimeSpan.FromMinutes(10));
        AddSent("+5493515550102", TimeSpan.FromMinutes(5));

        var error = await Guard(limit: 2).CheckAsync(Ct);

        Assert.NotNull(error);
        Assert.Equal(LoginCodeErrors.TooManyRequestsCode, error.Code);
        // El más viejo salió hace 10 minutos: deja la ventana dentro de 23 horas y 50 minutos.
        Assert.Equal((24 * 60 - 10) * 60, error.Metadata![LoginCodeErrors.RetryAfterKey]);
    }

    [Fact]
    public async Task Reaching_the_limit_logs_it_without_any_number()
    {
        AddSent("+5493515550101", TimeSpan.FromMinutes(1));

        await Guard(limit: 1).CheckAsync(Ct);

        var log = Assert.Single(_logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Equal(
            "The daily limit of WhatsApp codes (1 in 24 hours) was reached; no code is sent until the oldest one leaves the window",
            log.Message);
    }

    [Fact]
    public async Task Only_whatsapp_codes_that_went_out_in_the_last_24_hours_count()
    {
        // Uno de hace más de 24 horas, uno que no salió y uno por correo: ninguno cuenta.
        AddSent("+5493515550101", TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        _codes.Add(Issue(LoginCodeDestination.ForPhone(PhoneNumber.Create("+5493515550102").Value), sentAgo: null));
        _codes.Add(Issue(LoginCodeDestination.ForEmail(Email.Create("ana@example.com").Value), TimeSpan.FromMinutes(1)));

        Assert.Null(await Guard(limit: 1).CheckAsync(Ct));
    }

    // H10: the request that takes the codes of the day to 80 % of the limit leaves a warning, once and without any number.
    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public async Task The_request_that_crosses_eighty_percent_of_the_daily_limit_warns(int sent, bool warns)
    {
        for (var index = 0; index < sent; index++) AddSent($"+54935155501{index:00}", TimeSpan.FromMinutes(index + 1));
        var guard = Guard(limit: 10);

        Assert.Null(await guard.CheckAsync(Ct));
        Assert.Empty(_logger.Collector.GetSnapshot());
        guard.ReportSent();

        var logs = _logger.Collector.GetSnapshot();
        if (!warns) { Assert.Empty(logs); return; }
        var log = Assert.Single(logs);
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Equal("The WhatsApp codes of the last 24 hours reached 8 of the daily limit of 10; at the limit no code is sent", log.Message);
    }

    // H10: one account cannot spend the daily limit asking for codes for other numbers from its profile.
    [Fact]
    public async Task An_account_asks_for_a_limited_number_of_link_codes_a_day()
    {
        var account = Guid.CreateVersion7();
        AddSent("+5493515550101", TimeSpan.FromMinutes(10), account);
        AddSent("+5493515550102", TimeSpan.FromMinutes(5), account);
        AddSent("+5493515550103", TimeSpan.FromMinutes(1), Guid.CreateVersion7());

        var error = await Guard(limit: 100, perAccount: 2).CheckLinkAsync(account, Ct);

        Assert.NotNull(error);
        Assert.Equal(LoginCodeErrors.TooManyRequestsCode, error.Code);
        Assert.Equal((24 * 60 - 10) * 60, error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Null(await Guard(limit: 100, perAccount: 2).CheckLinkAsync(Guid.CreateVersion7(), Ct));
        Assert.Equal("An account reached its daily limit of 2 WhatsApp codes to link a number; no code is sent to it until the oldest one leaves the window",
            Assert.Single(_logger.Collector.GetSnapshot()).Message);
    }

    // H10: a code that went out takes a place of the address of its request; an address the limiter refuses answers 429 with the
    // wait, and a request without its address is not limited.
    [Theory]
    [InlineData("203.0.113.10", true)]
    [InlineData(null, false)]
    public void A_code_that_went_out_takes_a_place_of_the_address_of_its_request(string? address, bool limited)
    {
        var addresses = new FakeAddressLimiter { RetryAfter = TimeSpan.FromHours(3) };

        var error = Guard(limit: 100, addresses: addresses, address: address).TakeAddress();

        Assert.Equal(limited ? new[] { address! } : Array.Empty<string>(), addresses.Taken);
        if (!limited) { Assert.Null(error); return; }
        Assert.Equal(LoginCodeErrors.TooManyRequestsCode, error!.Code);
        Assert.Equal(3 * 60 * 60, error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Equal("An address reached its daily limit of WhatsApp codes while registration is open; no code is sent for it until its window frees a place",
            Assert.Single(_logger.Collector.GetSnapshot()).Message);
    }

    private sealed class FakeAddressLimiter : ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations.IWhatsAppCodeAddressLimiter
    {
        public TimeSpan? RetryAfter { get; init; }
        public List<string> Taken { get; } = [];
        public TimeSpan? TryTake(string address) { Taken.Add(address); return RetryAfter; }
    }

    private WhatsAppCodeQuotaGuard Guard(int limit, int perAccount = 10, FakeAddressLimiter? addresses = null, string? address = "203.0.113.10") => new(
        _codes,
        Options.Create(new WhatsAppLoginOptions { DailyAuthCodeLimit = limit, DailyLinkCodeLimitPerAccount = perAccount }),
        new FakeRequestInfo { IpAddress = address },
        addresses ?? new FakeAddressLimiter(),
        _clock,
        _logger);

    private void AddSent(string phone, TimeSpan sentAgo, Guid? requestedBy = null) =>
        _codes.Add(Issue(LoginCodeDestination.ForPhone(PhoneNumber.Create(phone).Value), sentAgo, requestedBy));

    private static LoginCode Issue(LoginCodeDestination destination, TimeSpan? sentAgo, Guid? requestedBy = null)
    {
        var issuedAtUtc = (Now - (sentAgo ?? TimeSpan.Zero)).UtcDateTime;
        var code = LoginCode.Issue(destination, requestedBy is null ? LoginCodePurpose.SignIn : LoginCodePurpose.VerifyDestination, requestedBy, "hash",
            issuedAtUtc, TimeSpan.FromMinutes(10), maxAttempts: 5);
        if (sentAgo is not null)
        {
            code.MarkSent(issuedAtUtc);
        }

        return code;
    }
}
