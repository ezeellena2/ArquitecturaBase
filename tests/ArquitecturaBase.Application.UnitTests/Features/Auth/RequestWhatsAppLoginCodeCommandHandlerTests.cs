using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Features.Auth.RequestWhatsAppLoginCode;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

public sealed class RequestWhatsAppLoginCodeCommandHandlerTests
{
    private const string Phone = "+5493515550101";
    private const string OtherPhone = "+5493515550202";
    private const string ThirdPhone = "+5493515550303";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryLoginCodeRepository _loginCodes = new();
    private readonly FakeIdentityService _identity = new();
    private readonly FakeWhatsAppOutbox _outbox = new();
    private readonly FakeSystemSettingsReader _settings = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Issues_a_sign_in_code_for_the_number_and_queues_the_template()
    {
        var result = await Handler().Handle(Command(Phone), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.ResendAfterSeconds);
        Assert.Equal(Phone, result.Value.Phone);
        Assert.Equal("masked 0101", result.Value.MaskedPhone);
        Assert.Equal([Phone], _loginCodes.LockedDestinations);

        var code = Assert.Single(_loginCodes.Codes);
        Assert.Equal(Phone, code.Destination);
        Assert.Equal(LoginCodeChannel.WhatsApp, code.Channel);
        Assert.Equal(LoginCodePurpose.SignIn, code.Purpose);
        Assert.Equal(FakeLoginCodeHasher.HashOf(Phone, LoginCodePurpose.SignIn, FakeLoginCodeGenerator.Code), code.CodeHash);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, code.SentAtUtc);

        var message = Assert.IsType<WhatsAppLoginCodeMessage>(Assert.Single(_outbox.Messages));
        Assert.Equal(Phone, message.To.Value);
        Assert.Equal(FakeLoginCodeGenerator.Code, message.Code);
    }

    [Fact]
    public async Task Invalid_number_issues_nothing()
    {
        var result = await Handler().Handle(new RequestWhatsAppLoginCodeCommand("AR", "abc"), Ct);

        Assert.Equal(UserErrors.PhoneInvalidCode, result.Error.Code);
        Assert.Empty(_loginCodes.LockedDestinations);
        Assert.Empty(_loginCodes.Codes);
        Assert.Empty(_outbox.Messages);
    }

    [Fact]
    public async Task The_country_is_checked_with_the_number_and_not_with_the_chosen_country()
    {
        // Eligió Argentina, pero escribió un número de Uruguay con su código de país.
        var result = await Handler().Handle(new RequestWhatsAppLoginCodeCommand("AR", "+59899123456"), Ct);

        Assert.Equal(WhatsAppErrors.CountryNotSupportedCode, result.Error.Code);
        Assert.Empty(_loginCodes.LockedDestinations);
        Assert.Empty(_loginCodes.Codes);
        Assert.Empty(_outbox.Messages);
    }

    [Fact]
    public async Task A_number_whose_country_is_unknown_is_not_supported()
    {
        var result = await Handler().Handle(new RequestWhatsAppLoginCodeCommand("AR", "+99912345678"), Ct);

        Assert.Equal(WhatsAppErrors.CountryNotSupportedCode, result.Error.Code);
        Assert.Empty(_loginCodes.Codes);
    }

    [Fact]
    public async Task A_country_added_to_the_allowed_ones_is_accepted()
    {
        var result = await Handler(allowedCountries: ["AR", "UY"]).Handle(Command("+59899123456"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Single(_outbox.Messages);
    }

    [Fact]
    public async Task Invite_only_issues_the_code_but_queues_nothing_for_a_number_without_an_account()
    {
        _settings.Mode = RegistrationMode.InviteOnly;

        var result = await Handler().Handle(Command(Phone), Ct);

        // La misma respuesta de siempre, y el código emitido igual: sostiene los límites por número.
        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.ResendAfterSeconds);
        Assert.Equal(Phone, result.Value.Phone);
        Assert.Null(Assert.Single(_loginCodes.Codes).SentAtUtc);
        Assert.Empty(_outbox.Messages);
    }

    [Fact]
    public async Task Invite_only_still_queues_the_code_for_an_account_with_the_number()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        _identity.AddUser(email: null, phoneNumber: Phone);

        var result = await Handler().Handle(Command(Phone), Ct);

        Assert.True(result.IsSuccess);
        Assert.NotNull(Assert.Single(_loginCodes.Codes).SentAtUtc);
        Assert.Single(_outbox.Messages);
    }

    [Fact]
    public async Task Invite_only_queues_nothing_for_the_number_of_a_deleted_account()
    {
        // Una cuenta borrada conserva su número, pero FindByPhoneAsync no la encuentra: es como si no tuviera cuenta.
        _settings.Mode = RegistrationMode.InviteOnly;
        var user = _identity.AddUser(email: null, phoneNumber: Phone);
        await _identity.DeleteAsync(user.Id, Ct);

        var result = await Handler().Handle(Command(Phone), Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(_outbox.Messages);
    }

    [Fact]
    public async Task The_template_goes_in_the_language_of_the_account()
    {
        _identity.AddUser(email: null, culture: "en", phoneNumber: Phone);
        using var culture = new CultureScope("es");

        await Handler().Handle(Command(Phone), Ct);

        Assert.Equal("en", Assert.IsType<WhatsAppLoginCodeMessage>(Assert.Single(_outbox.Messages)).LanguageCode);
    }

    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("es-AR", "es")]
    [InlineData("fr", "es")]
    public async Task The_template_for_a_new_account_goes_in_the_language_of_the_request(string requestCulture, string expected)
    {
        using var culture = new CultureScope(requestCulture);

        await Handler().Handle(Command(Phone), Ct);

        Assert.Equal(expected, Assert.IsType<WhatsAppLoginCodeMessage>(Assert.Single(_outbox.Messages)).LanguageCode);
    }

    [Fact]
    public async Task The_code_stays_unsent_when_the_queue_does_not_take_the_message()
    {
        _outbox.Accepts = false;

        var result = await Handler().Handle(Command(Phone), Ct);

        // La persona recibe la respuesta de siempre y pide otro; lo que no salió no cuenta para el tope diario.
        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(_loginCodes.Codes).SentAtUtc);
    }

    [Fact]
    public async Task A_new_code_invalidates_the_previous_sign_in_codes_of_the_number()
    {
        await Handler().Handle(Command(Phone), Ct);
        _clock.Advance(TimeSpan.FromSeconds(60));

        var result = await Handler().Handle(Command(Phone), Ct);

        Assert.True(result.IsSuccess);
        Assert.NotNull(_loginCodes.Codes[0].InvalidatedAtUtc);
        Assert.Null(_loginCodes.Codes[1].InvalidatedAtUtc);
    }

    [Fact]
    public async Task Asking_again_before_the_cooldown_returns_the_seconds_left_like_the_email()
    {
        await Handler().Handle(Command(Phone), Ct);
        _clock.Advance(TimeSpan.FromSeconds(20));

        var result = await Handler().Handle(Command(Phone), Ct);

        Assert.Equal(LoginCodeErrors.ResendTooSoonCode, result.Error.Code);
        Assert.Equal(40, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Single(_loginCodes.Codes);
        Assert.Single(_outbox.Messages);
    }

    [Fact]
    public async Task Reaching_the_daily_limit_waits_until_the_oldest_send_leaves_the_window_and_issues_nothing()
    {
        var handler = Handler(dailyLimit: 2);
        await handler.Handle(Command(Phone), Ct);
        _clock.Advance(TimeSpan.FromHours(1));
        await handler.Handle(Command(OtherPhone), Ct);
        _clock.Advance(TimeSpan.FromHours(1));

        var result = await handler.Handle(Command(ThirdPhone), Ct);

        // El primero salió hace 2 horas: deja la ventana de 24 dentro de 22.
        Assert.Equal(LoginCodeErrors.TooManyRequestsCode, result.Error.Code);
        Assert.Equal(22 * 60 * 60, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Equal([Phone, OtherPhone], _loginCodes.LockedDestinations);
        Assert.Equal(2, _loginCodes.Codes.Count);
        Assert.Equal(2, _outbox.Messages.Count);
    }

    [Fact]
    public async Task The_daily_limit_is_a_moving_window_of_24_hours()
    {
        var handler = Handler(dailyLimit: 1);
        await handler.Handle(Command(Phone), Ct);
        _clock.Advance(TimeSpan.FromHours(24));

        var result = await handler.Handle(Command(OtherPhone), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _outbox.Messages.Count);
    }

    [Fact]
    public async Task The_daily_limit_only_counts_codes_that_went_out_by_whatsapp()
    {
        // Ni un código que no se mandó (InviteOnly) ni uno por correo le cuestan nada al sistema.
        _settings.Mode = RegistrationMode.InviteOnly;
        await Handler(dailyLimit: 1).Handle(Command(OtherPhone), Ct);
        var emailCode = LoginCode.Issue(
            LoginCodeDestination.ForEmail(Email.Create("ana@example.com").Value),
            LoginCodePurpose.SignIn,
            requestedByUserId: null,
            "hash",
            _clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(10),
            maxAttempts: 5);
        emailCode.MarkSent(_clock.GetUtcNow().UtcDateTime);
        _loginCodes.Add(emailCode);
        _identity.AddUser(email: null, phoneNumber: Phone);

        var result = await Handler(dailyLimit: 1).Handle(Command(Phone), Ct);

        Assert.True(result.IsSuccess);
        Assert.Single(_outbox.Messages);
    }

    [Fact]
    public async Task Codes_to_verify_a_number_from_the_profile_count_toward_the_daily_limit()
    {
        // La plantilla es la misma y se paga igual, sea cual sea el propósito.
        var verification = LoginCode.Issue(
            LoginCodeDestination.ForPhone(PhoneNumber.Create(OtherPhone).Value),
            LoginCodePurpose.VerifyDestination,
            Guid.CreateVersion7(),
            "hash",
            _clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(10),
            maxAttempts: 5);
        verification.MarkSent(_clock.GetUtcNow().UtcDateTime);
        _loginCodes.Add(verification);

        var result = await Handler(dailyLimit: 1).Handle(Command(Phone), Ct);

        Assert.Equal(LoginCodeErrors.TooManyRequestsCode, result.Error.Code);
        Assert.Empty(_outbox.Messages);
    }

    [Fact]
    public async Task Calling_it_with_whatsapp_off_is_a_programming_error()
    {
        // El endpoint no se mapea con WhatsApp apagado: llegar acá es un bug, no algo que decida quien pide.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(enabled: false).Handle(Command(Phone), Ct));

        Assert.Empty(_loginCodes.Codes);
    }

    private static RequestWhatsAppLoginCodeCommand Command(string number) => new("AR", number);

    private RequestWhatsAppLoginCodeCommandHandler Handler(bool enabled = true, int dailyLimit = 100, string[]? allowedCountries = null)
    {
        var loginCodeOptions = Options.Create(new LoginCodeOptions());
        var whatsAppOptions = Options.Create(new WhatsAppLoginOptions { AllowedCountries = allowedCountries, DailyAuthCodeLimit = dailyLimit });

        return new RequestWhatsAppLoginCodeCommandHandler(
            new LoginCodeIssuer(
                _loginCodes,
                new FakeLoginCodeGenerator(),
                new FakeLoginCodeHasher(),
                loginCodeOptions,
                whatsAppOptions,
                _clock,
                NullLogger<LoginCodeIssuer>.Instance),
            _identity,
            new FakePhoneNumberParser(),
            new FakeWhatsAppAvailability(enabled),
            _outbox,
            new AccountCreationPolicy(_settings, new FakeInitialAdmin()),
            whatsAppOptions,
            loginCodeOptions);
    }
}
