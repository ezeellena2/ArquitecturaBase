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

        Assert.Null(await Guard(limit: 2).CheckAsync(Ct));
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

    private WhatsAppCodeQuotaGuard Guard(int limit) => new(
        _codes,
        Options.Create(new WhatsAppLoginOptions { DailyAuthCodeLimit = limit }),
        _clock,
        _logger);

    private void AddSent(string phone, TimeSpan sentAgo) =>
        _codes.Add(Issue(LoginCodeDestination.ForPhone(PhoneNumber.Create(phone).Value), sentAgo));

    private static LoginCode Issue(LoginCodeDestination destination, TimeSpan? sentAgo)
    {
        var issuedAtUtc = (Now - (sentAgo ?? TimeSpan.Zero)).UtcDateTime;
        var code = LoginCode.Issue(
            destination, LoginCodePurpose.SignIn, requestedByUserId: null, "hash", issuedAtUtc, TimeSpan.FromMinutes(10), maxAttempts: 5);
        if (sentAgo is not null)
        {
            code.MarkSent(issuedAtUtc);
        }

        return code;
    }
}
