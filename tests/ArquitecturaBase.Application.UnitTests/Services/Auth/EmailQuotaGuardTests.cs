using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

/// <summary>
/// N7: los topes diarios de lo que sale por correo, en una ventana móvil de 24 horas: por dirección (todo código pedido
/// desde ella, haya salido o no), por cuenta (los códigos del perfil), por administrador (sus invitaciones) y el de todos
/// (códigos e invitaciones que salieron), con un aviso al 80 % una vez por día. En el de todos, los códigos frenan antes
/// del tope, en el tope menos la reserva de las invitaciones. Que el ingreso, el perfil y las invitaciones lo miren antes
/// de emitir lo prueban sus servicios y la integración (EmailQuotaEndpointsTests).
/// </summary>
public sealed class EmailQuotaGuardTests
{
    private const string Address = "2001:db8:1:2::/64";
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryLoginCodeRepository _codes = new();
    private readonly InMemoryUserInvitationRepository _invitations = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeLogger<EmailQuotaGuard> _logger = new();

    private readonly FakeEmailQuotaWarnings _warnings = new();
    private readonly Guid _account = Guid.CreateVersion7();
    private readonly Guid _administrator = Guid.CreateVersion7();

    [Fact]
    public async Task Below_every_limit_there_is_no_error_and_no_log()
    {
        AddCode("ana@example.com", TimeSpan.FromMinutes(5), sent: true, Address);

        Assert.Null(await Guard(new EmailQuotaOptions()).CheckCodeAsync(_account, Ct));
        Assert.Null(await Guard(new EmailQuotaOptions()).CheckInvitationAsync(_administrator, Ct));
        Assert.Empty(_logger.Collector.GetSnapshot());
    }

    // Todo código pedido desde la dirección cuenta, para entrar o del perfil, a cualquier destino y haya salido o no: en
    // InviteOnly uno a un correo sin cuenta no sale, y si no contara, el tope diría qué correos tienen cuenta.
    [Fact]
    public async Task Every_email_code_requested_from_the_address_counts_whether_it_went_out_or_not()
    {
        AddCode("ana@example.com", TimeSpan.FromMinutes(30), sent: true, Address);
        AddCode("nadie@example.com", TimeSpan.FromMinutes(20), sent: false, Address);
        AddCode("luis@example.com", TimeSpan.FromMinutes(10), sent: true, Address, requestedBy: Guid.CreateVersion7());

        var error = await Guard(new EmailQuotaOptions { DailyCodeLimitPerAddress = 3 }).CheckCodeAsync(requestedByUserId: null, Ct);

        Assert.NotNull(error);
        Assert.Equal(LoginCodeErrors.DailyLimitReachedCode, error.Code);
        // El más viejo de los tres se pidió hace 30 minutos: deja la ventana dentro de 23 horas y 30 minutos.
        Assert.Equal((24 * 60 - 30) * 60, error.Metadata![LoginCodeErrors.RetryAfterKey]);
    }

    [Fact]
    public async Task Codes_from_another_address_by_phone_or_older_than_a_day_do_not_count_for_the_address()
    {
        AddCode("ana@example.com", TimeSpan.FromMinutes(5), sent: true, "203.0.113.10");
        AddCode("+5493515550101", TimeSpan.FromMinutes(5), sent: true, Address);
        AddCode("luis@example.com", TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1), sent: true, Address);

        Assert.Null(await Guard(new EmailQuotaOptions { DailyCodeLimitPerAddress = 1 }).CheckCodeAsync(null, Ct));
    }

    // Un IPv6 cuenta por su /64: rotar adentro de esa red no esquiva el tope.
    [Fact]
    public async Task The_address_of_the_request_counts_by_its_network_of_64()
    {
        AddCode("ana@example.com", TimeSpan.FromMinutes(5), sent: true, Address);

        var error = await Guard(new EmailQuotaOptions { DailyCodeLimitPerAddress = 1 }, ipAddress: "2001:db8:1:2:ffff::1")
            .CheckCodeAsync(null, Ct);

        Assert.Equal(LoginCodeErrors.DailyLimitReachedCode, error?.Code);
    }

    [Fact]
    public async Task Without_a_known_address_there_is_no_limit_per_address()
    {
        AddCode("ana@example.com", TimeSpan.FromMinutes(5), sent: true, requestedFrom: null);

        Assert.Null(await Guard(new EmailQuotaOptions { DailyCodeLimitPerAddress = 1 }, ipAddress: null).CheckCodeAsync(null, Ct));
    }

    [Fact]
    public async Task An_account_reaches_its_daily_limit_of_profile_codes_counting_the_ones_that_went_out()
    {
        AddCode("ana@example.com", TimeSpan.FromMinutes(10), sent: true, "198.51.100.1", requestedBy: _account);
        AddCode("ana@example.com", TimeSpan.FromMinutes(5), sent: false, "198.51.100.1", requestedBy: _account);
        AddCode("luis@example.com", TimeSpan.FromMinutes(5), sent: true, "198.51.100.1", requestedBy: Guid.CreateVersion7());
        var options = new EmailQuotaOptions { DailyProfileCodeLimitPerAccount = 1 };

        var error = await Guard(options).CheckCodeAsync(_account, Ct);

        Assert.Equal(LoginCodeErrors.DailyLimitReachedCode, error?.Code);
        Assert.Equal((24 * 60 - 10) * 60, error!.Metadata![LoginCodeErrors.RetryAfterKey]);
        // Un código para entrar no es de ninguna cuenta: no mira ese tope.
        Assert.Null(await Guard(options).CheckCodeAsync(requestedByUserId: null, Ct));
    }

    // El de todos cuenta lo que salió por correo, códigos e invitaciones juntos: es el cupo de la cuenta que manda.
    [Fact]
    public async Task The_limit_of_everyone_counts_the_codes_and_invitations_that_went_out_by_email()
    {
        AddCode("ana@example.com", TimeSpan.FromMinutes(40), sent: true, "198.51.100.1");
        AddInvitation(UserInvitationChannel.Email, TimeSpan.FromMinutes(30), Guid.CreateVersion7());
        AddCode("luis@example.com", TimeSpan.FromMinutes(20), sent: true, "198.51.100.2");
        // Ni un código que no salió, ni uno por teléfono, ni una invitación por WhatsApp gastan el cupo del correo.
        AddCode("nadie@example.com", TimeSpan.FromMinutes(1), sent: false, "198.51.100.3");
        AddCode("+5493515550101", TimeSpan.FromMinutes(1), sent: true, "198.51.100.3");
        AddInvitation(UserInvitationChannel.WhatsApp, TimeSpan.FromMinutes(1), Guid.CreateVersion7());
        var options = new EmailQuotaOptions { DailyLimit = 3, DailyInvitationReserve = 0 };

        var codeError = await Guard(options).CheckCodeAsync(null, Ct);
        var invitationError = await Guard(options).CheckInvitationAsync(_administrator, Ct);

        Assert.Equal(LoginCodeErrors.DailyLimitReachedCode, codeError?.Code);
        Assert.Equal((24 * 60 - 40) * 60, codeError!.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Equal(UserInvitationErrors.DailyLimitReachedCode, invitationError?.Code);
        Assert.Null(await Guard(new EmailQuotaOptions { DailyLimit = 4, DailyInvitationReserve = 0 }).CheckCodeAsync(null, Ct));
    }

    // La reserva de las invitaciones: los códigos frenan en el tope menos la reserva y las invitaciones llegan al tope, así
    // un abuso anónimo de los códigos no deja a los administradores sin invitar.
    [Fact]
    public async Task Codes_stop_at_the_limit_of_everyone_minus_the_invitation_reserve_and_invitations_reach_the_limit()
    {
        AddCode("ana@example.com", TimeSpan.FromMinutes(40), sent: true, "198.51.100.1");
        AddCode("luis@example.com", TimeSpan.FromMinutes(30), sent: true, "198.51.100.2");
        AddCode("eva@example.com", TimeSpan.FromMinutes(20), sent: true, "198.51.100.3");
        var options = new EmailQuotaOptions { DailyLimit = 5, DailyInvitationReserve = 2 };

        var codeError = await Guard(options).CheckCodeAsync(null, Ct);
        var firstInvitation = await Guard(options).CheckInvitationAsync(_administrator, Ct);
        AddInvitation(UserInvitationChannel.Email, TimeSpan.FromMinutes(10), _administrator);
        var secondInvitation = await Guard(options).CheckInvitationAsync(_administrator, Ct);
        AddInvitation(UserInvitationChannel.Email, TimeSpan.FromMinutes(5), _administrator);
        var thirdInvitation = await Guard(options).CheckInvitationAsync(_administrator, Ct);

        // Tres salieron y los códigos llegan a tres: se libera un lugar cuando el tercero más nuevo, el de hace 40 minutos,
        // sale de la ventana.
        Assert.Equal(LoginCodeErrors.DailyLimitReachedCode, codeError?.Code);
        Assert.Equal((24 * 60 - 40) * 60, codeError!.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Null(firstInvitation);
        Assert.Null(secondInvitation);
        Assert.Equal(UserInvitationErrors.DailyLimitReachedCode, thirdInvitation?.Code);
        Assert.Equal((24 * 60 - 40) * 60, thirdInvitation!.Metadata![UserInvitationErrors.RetryAfterKey]);
    }

    // Los avisos siguen sobre el tope, no sobre lo que llegan los códigos: con los códigos frenados por la reserva avisa el
    // 80 %, y el del tope recién cuando lo llenan las invitaciones.
    [Fact]
    public async Task The_warnings_count_on_the_limit_of_everyone_even_when_the_reserve_stops_the_codes()
    {
        for (var index = 0; index < 8; index++) AddCode($"persona{index}@example.com", TimeSpan.FromMinutes(index + 1), sent: true, "198.51.100.1");
        var options = new EmailQuotaOptions { DailyLimit = 10, DailyInvitationReserve = 2 };

        Assert.Equal(LoginCodeErrors.DailyLimitReachedCode, (await Guard(options).CheckCodeAsync(null, Ct))?.Code);
        AddInvitation(UserInvitationChannel.Email, TimeSpan.Zero, _administrator);
        AddInvitation(UserInvitationChannel.Email, TimeSpan.Zero, _administrator);
        Assert.Equal(UserInvitationErrors.DailyLimitReachedCode, (await Guard(options).CheckInvitationAsync(_administrator, Ct))?.Code);

        Assert.Equal(["near", "reached"], _warnings.Asked);
        Assert.Equal(
            [
                "The emails with a code or an invitation of the last 24 hours reached 8 of the daily limit of 10; codes by email stop at 8 and invitations at the limit",
                "The emails with a code or an invitation of the last 24 hours reached the daily limit of 10; no code or invitation is sent by email until the oldest one leaves the window",
            ],
            _logger.Collector.GetSnapshot().Select(log => log.Message));
    }

    [Fact]
    public async Task Reaching_the_limit_of_everyone_warns_without_any_address_or_account_when_the_day_has_not_warned_yet()
    {
        AddCode("ana@example.com", TimeSpan.FromMinutes(1), sent: true, "198.51.100.1");
        var options = new EmailQuotaOptions { DailyLimit = 1, DailyInvitationReserve = 0 };

        await Guard(options).CheckCodeAsync(null, Ct);
        _warnings.Allows = false;
        await Guard(options).CheckInvitationAsync(_administrator, Ct);

        Assert.Equal(["reached", "reached"], _warnings.Asked);
        var log = Assert.Single(_logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Equal(
            "The emails with a code or an invitation of the last 24 hours reached the daily limit of 1; no code or invitation is sent by email until the oldest one leaves the window",
            log.Message);
    }

    // El aviso del 80 % lo pide cada pedido que encuentra el día arriba del 80 %; sale si es el primero del día en la copia.
    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    public async Task From_eighty_percent_of_the_limit_of_everyone_it_warns_when_the_day_has_not_warned_yet(int sent, bool warns)
    {
        for (var index = 0; index < sent; index++) AddCode($"persona{index}@example.com", TimeSpan.FromMinutes(index + 1), sent: true, "198.51.100.1");
        var options = new EmailQuotaOptions { DailyLimit = 10, DailyInvitationReserve = 0 };

        Assert.Null(await Guard(options).CheckCodeAsync(null, Ct));
        _warnings.Allows = false;
        Assert.Null(await Guard(options).CheckCodeAsync(null, Ct));

        string[] asked = warns ? ["near", "near"] : [];
        Assert.Equal(asked, _warnings.Asked);
        var logs = _logger.Collector.GetSnapshot();
        Assert.Equal(warns ? 1 : 0, logs.Count);
        if (warns)
        {
            Assert.Equal(LogLevel.Warning, logs[0].Level);
            Assert.Equal(
                $"The emails with a code or an invitation of the last 24 hours reached {sent} of the daily limit of 10; codes by email stop at 10 and invitations at the limit",
                logs[0].Message);
        }
    }

    [Fact]
    public async Task An_administrator_reaches_the_daily_limit_of_invitations_by_email()
    {
        AddInvitation(UserInvitationChannel.Email, TimeSpan.FromMinutes(15), _administrator);
        AddInvitation(UserInvitationChannel.Email, TimeSpan.FromMinutes(5), Guid.CreateVersion7());
        AddInvitation(UserInvitationChannel.WhatsApp, TimeSpan.FromMinutes(5), _administrator);
        var options = new EmailQuotaOptions { DailyInvitationLimitPerAdministrator = 1 };

        var error = await Guard(options).CheckInvitationAsync(_administrator, Ct);

        Assert.Equal(UserInvitationErrors.DailyLimitReachedCode, error?.Code);
        Assert.Equal((24 * 60 - 15) * 60, error!.Metadata![UserInvitationErrors.RetryAfterKey]);
        Assert.Null(await Guard(options).CheckInvitationAsync(Guid.CreateVersion7(), Ct));
    }

    private EmailQuotaGuard Guard(EmailQuotaOptions options, string? ipAddress = "2001:db8:1:2::10") =>
        new(
            _codes,
            _invitations,
            new FakeRequestInfo { IpAddress = ipAddress },
            Options.Create(options),
            _warnings,
            _clock,
            _logger);

    private void AddCode(string destination, TimeSpan ago, bool sent, string? requestedFrom, Guid? requestedBy = null)
    {
        var target = destination.StartsWith('+')
            ? LoginCodeDestination.ForPhone(PhoneNumber.Create(destination).Value)
            : LoginCodeDestination.ForEmail(Email.Create(destination).Value);
        var at = _clock.GetUtcNow().UtcDateTime - ago;
        var code = LoginCode.Issue(
            target,
            requestedBy is null ? LoginCodePurpose.SignIn : LoginCodePurpose.VerifyDestination,
            requestedBy,
            "hash",
            at,
            TimeSpan.FromMinutes(10),
            maxAttempts: 5,
            requestedFrom);
        if (sent) code.MarkSent(at);
        _codes.Add(code);
    }

    private void AddInvitation(UserInvitationChannel channel, TimeSpan ago, Guid sentBy) =>
        _invitations.Add(UserInvitation.Send(
            Guid.CreateVersion7(), channel, sentBy, _clock.GetUtcNow().UtcDateTime - ago, consentConfirmed: channel is UserInvitationChannel.WhatsApp));
}
