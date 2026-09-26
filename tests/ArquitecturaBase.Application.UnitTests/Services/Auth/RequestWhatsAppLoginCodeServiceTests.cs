using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.Validation.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

public sealed class RequestWhatsAppLoginCodeServiceTests
{
    private const string Phone = "+5493515550101";
    private const string OtherPhone = "+5493515550202";
    private const string RawPhone = "0351 15 555 0101";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Success_uses_normalized_destination_and_enqueues_before_saving()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", RawPhone), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(("AR", RawPhone), (fixture.Parser.LastCountry, fixture.Parser.LastNumber));
        Assert.Equal(60, result.Value.ResendAfterSeconds);
        Assert.Equal(Phone, result.Value.Phone);
        Assert.Equal("masked 0101", result.Value.MaskedPhone);
        Assert.Equal([Phone], fixture.Codes.LockedDestinations);
        var code = Assert.Single(fixture.Codes.Codes);
        Assert.Equal(LoginCodeChannel.WhatsApp, code.Channel);
        Assert.Equal(LoginCodePurpose.SignIn, code.Purpose);
        Assert.Equal(Phone, code.Destination);
        Assert.Equal(FakeLoginCodeHasher.HashOf(Phone, LoginCodePurpose.SignIn, FakeLoginCodeGenerator.Code), code.CodeHash);
        Assert.Equal(fixture.Clock.GetUtcNow().UtcDateTime, code.SentAtUtc);
        var message = Assert.IsType<WhatsAppLoginCodeMessage>(Assert.Single(fixture.Outbox.Messages));
        Assert.Equal(Phone, message.To.Value);
        Assert.Equal(FakeLoginCodeGenerator.Code, message.Code);
        Assert.Equal(["enqueue", "save"], fixture.Events);
        Assert.Equal(code.SentAtUtc, fixture.UnitOfWork.SentAtSave);
        Assert.Equal(
            ["Handling RequestWhatsAppLoginCode", "Handled RequestWhatsAppLoginCode"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
        Assert.DoesNotContain(Phone, string.Join(' ', fixture.Logger.Collector.GetSnapshot().Select(record => record.Message)));
        Assert.DoesNotContain(FakeLoginCodeGenerator.Code, string.Join(' ', fixture.Logger.Collector.GetSnapshot().Select(record => record.Message)));
    }

    [Fact]
    public async Task Invalid_request_returns_localized_field_errors_before_using_parser_or_saving()
    {
        using var culture = new CultureScope("es");
        var fixture = new Fixture();

        var result = await fixture.Service.RequestWhatsAppLoginCodeAsync(new("ARG", " "), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal("Este campo es obligatorio.", Assert.Single(error.Errors["number"]));
        Assert.Equal("Elegí un país de la lista.", Assert.Single(error.Errors["country"]));
        Assert.Null(fixture.Parser.LastNumber);
        Assert.Empty(fixture.Codes.Codes);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Number_longer_than_32_characters_fails_validation_before_issuing()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", new string('1', 33)), Ct);

        Assert.Contains("number", Assert.IsType<ValidationError>(result.Error).Errors.Keys);
        Assert.Null(fixture.Parser.LastNumber);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task International_number_does_not_need_a_selected_country()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RequestWhatsAppLoginCodeAsync(new(null, Phone), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(Phone, result.Value.Phone);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Invalid_mobile_returns_phone_error_without_issuing_or_saving()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", "abc"), Ct);

        Assert.Equal(UserErrors.PhoneInvalidCode, result.Error.Code);
        Assert.Empty(fixture.Codes.LockedDestinations);
        Assert.Empty(fixture.Outbox.Messages);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Country_is_checked_from_the_parsed_phone_rather_than_the_selected_country()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", "+59899123456"), Ct);

        Assert.Equal(WhatsAppErrors.CountryNotSupportedCode, result.Error.Code);
        Assert.Empty(fixture.Codes.LockedDestinations);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Configured_country_is_accepted()
    {
        var fixture = new Fixture(allowedCountries: ["AR", "UY"]);

        var result = await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", "+59899123456"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("+59899123456", result.Value.Phone);
        Assert.Single(fixture.Outbox.Messages);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Disabled_whatsapp_is_a_programming_error_after_request_validation()
    {
        var fixture = new Fixture(enabled: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", Phone), Ct));

        Assert.Empty(fixture.Codes.Codes);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Invite_only_saves_an_unsent_code_for_unknown_number_and_sends_for_existing_account()
    {
        var fixture = new Fixture();
        fixture.Settings.Mode = RegistrationMode.InviteOnly;
        fixture.Identity.AddUser(email: null, phoneNumber: OtherPhone);

        var unknown = await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", Phone), Ct);
        var known = await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", OtherPhone), Ct);

        Assert.True(unknown.IsSuccess);
        Assert.True(known.IsSuccess);
        Assert.Equal(known.Value.ResendAfterSeconds, unknown.Value.ResendAfterSeconds);
        Assert.Null(fixture.Codes.Codes[0].SentAtUtc);
        Assert.NotNull(fixture.Codes.Codes[1].SentAtUtc);
        Assert.Equal(OtherPhone, Assert.Single(fixture.Outbox.Messages).To.Value);
        Assert.Equal(2, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(["save", "enqueue", "save"], fixture.Events);
    }

    [Fact]
    public async Task Invite_only_treats_deleted_account_number_as_unknown()
    {
        var fixture = new Fixture();
        fixture.Settings.Mode = RegistrationMode.InviteOnly;
        var user = fixture.Identity.AddUser(email: null, phoneNumber: Phone);
        await fixture.Identity.DeleteAsync(user.Id, Ct);

        var result = await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", Phone), Ct);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(fixture.Codes.Codes).SentAtUtc);
        Assert.Empty(fixture.Outbox.Messages);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Declined_outbox_keeps_code_unsent_but_saves_the_successful_request()
    {
        var fixture = new Fixture();
        fixture.Outbox.Accepts = false;

        var result = await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", Phone), Ct);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(fixture.Codes.Codes).SentAtUtc);
        Assert.Empty(fixture.Outbox.Messages);
        Assert.Equal(["enqueue", "save"], fixture.Events);
    }

    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("fr", "es")]
    public async Task New_account_uses_supported_request_language_or_spanish(string requestCulture, string expected)
    {
        using var culture = new CultureScope(requestCulture);
        var fixture = new Fixture();

        await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", Phone), Ct);

        Assert.Equal(expected, Assert.IsType<WhatsAppLoginCodeMessage>(Assert.Single(fixture.Outbox.Messages)).LanguageCode);
    }

    [Fact]
    public async Task Existing_account_uses_its_language()
    {
        using var culture = new CultureScope("es");
        var fixture = new Fixture();
        fixture.Identity.AddUser(email: null, culture: "en", phoneNumber: Phone);

        await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", Phone), Ct);

        Assert.Equal("en", Assert.IsType<WhatsAppLoginCodeMessage>(Assert.Single(fixture.Outbox.Messages)).LanguageCode);
    }

    [Fact]
    public async Task Cooldown_is_shared_with_a_recent_verification_code_for_the_same_number()
    {
        var fixture = new Fixture();
        fixture.AddVerificationCode(Phone);
        fixture.Clock.Advance(TimeSpan.FromSeconds(20));

        var result = await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", Phone), Ct);

        Assert.Equal(LoginCodeErrors.ResendTooSoonCode, result.Error.Code);
        Assert.Equal(40, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Single(fixture.Codes.Codes);
        Assert.Empty(fixture.Outbox.Messages);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task New_sign_in_code_invalidates_the_previous_sign_in_code()
    {
        var fixture = new Fixture();
        await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", Phone), Ct);
        fixture.Clock.Advance(TimeSpan.FromSeconds(60));

        var result = await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", Phone), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, fixture.Codes.Codes.Count);
        Assert.NotNull(fixture.Codes.Codes[0].InvalidatedAtUtc);
        Assert.Null(fixture.Codes.Codes[1].InvalidatedAtUtc);
        Assert.Equal(2, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Daily_quota_is_a_moving_window_of_24_hours()
    {
        var fixture = new Fixture(dailyLimit: 1);
        await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", Phone), Ct);
        fixture.Clock.Advance(TimeSpan.FromHours(24));

        var result = await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", OtherPhone), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, fixture.Outbox.Messages.Count);
        Assert.Equal(2, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Daily_quota_ignores_unsent_whatsapp_and_sent_email_codes()
    {
        var fixture = new Fixture(dailyLimit: 1);
        fixture.Settings.Mode = RegistrationMode.InviteOnly;
        await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", OtherPhone), Ct);

        var emailCode = LoginCode.Issue(
            LoginCodeDestination.ForEmail(Email.Create("ana@example.com").Value),
            LoginCodePurpose.SignIn,
            requestedByUserId: null,
            "hash-email",
            fixture.Clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(10),
            maxAttempts: 5);
        emailCode.MarkSent(fixture.Clock.GetUtcNow().UtcDateTime);
        fixture.Codes.Add(emailCode);
        fixture.Settings.Mode = RegistrationMode.Open;

        var result = await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", Phone), Ct);

        Assert.True(result.IsSuccess);
        Assert.Null(fixture.Codes.Codes[0].SentAtUtc);
        Assert.Equal(Phone, Assert.Single(fixture.Outbox.Messages).To.Value);
        Assert.Equal(2, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Daily_quota_counts_sent_verification_codes_too()
    {
        var fixture = new Fixture(dailyLimit: 1);
        fixture.AddVerificationCode(OtherPhone).MarkSent(fixture.Clock.GetUtcNow().UtcDateTime);

        var result = await fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", Phone), Ct);

        Assert.Equal(LoginCodeErrors.TooManyRequestsCode, result.Error.Code);
        Assert.Empty(fixture.Codes.LockedDestinations);
        Assert.Empty(fixture.Outbox.Messages);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Save_failure_propagates_after_enqueue_without_a_success_log()
    {
        var fixture = new Fixture();
        fixture.UnitOfWork.Failure = new InvalidOperationException("Save failed.");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RequestWhatsAppLoginCodeAsync(new("AR", Phone), Ct));

        Assert.Single(fixture.Outbox.Messages);
        Assert.NotNull(Assert.Single(fixture.Codes.Codes).SentAtUtc);
        Assert.Equal(["enqueue", "save"], fixture.Events);
        Assert.Equal(["Handling RequestWhatsAppLoginCode"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public void Request_and_response_string_representations_hide_the_phone()
    {
        Assert.Equal(nameof(RequestWhatsAppLoginCodeRequest), new RequestWhatsAppLoginCodeRequest("AR", Phone).ToString());
        Assert.Equal(nameof(RequestWhatsAppLoginCodeResponse), new RequestWhatsAppLoginCodeResponse(60, Phone, "masked 0101").ToString());
    }

    private sealed class Fixture
    {
        public Fixture(bool enabled = true, int dailyLimit = 100, string[]? allowedCountries = null)
        {
            var codeOptions = Options.Create(new LoginCodeOptions());
            var whatsAppOptions = Options.Create(new WhatsAppLoginOptions
            {
                DailyAuthCodeLimit = dailyLimit,
                AllowedCountries = allowedCountries,
            });
            var accountCreation = new AccountCreationPolicy(Settings, new FakeInitialAdmin());
            Outbox = new RecordingOutbox(Events);
            UnitOfWork = new RecordingUnitOfWork(Events, Codes);
            Service = new AccountService(
                new FakeGoogleAvailability(false),
                new FakeWhatsAppAvailability(enabled),
                whatsAppOptions,
                new LoginCodeIssuer(
                    Codes,
                    new FakeLoginCodeGenerator(),
                    new FakeLoginCodeHasher(),
                    codeOptions,
                    whatsAppOptions,
                    Clock,
                    NullLogger<LoginCodeIssuer>.Instance),
                new LoginCodeVerifier(
                    Codes,
                    new InMemoryLoginAuditRepository(),
                    Identity,
                    new FakeLoginCodeHasher(),
                    accountCreation,
                    new FakeRequestInfo(),
                    Clock),
                Identity,
                Parser,
                Outbox,
                new FakeEmailTemplateRenderer(),
                new FakeEmailQueue(),
                accountCreation,
                codeOptions,
                new ServiceRequestValidator<RequestLoginCodeRequest>([new RequestLoginCodeRequestValidator()]),
                new ServiceRequestValidator<RequestWhatsAppLoginCodeRequest>([new RequestWhatsAppLoginCodeRequestValidator()]),
                new ServiceRequestValidator<VerifyLoginCodeRequest>([new VerifyLoginCodeRequestValidator(codeOptions)]),
                UnitOfWork,
                Logger);
        }

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));

        public InMemoryLoginCodeRepository Codes { get; } = new();

        public FakeIdentityService Identity { get; } = new();

        public FakeSystemSettingsReader Settings { get; } = new();

        public RecordingParser Parser { get; } = new();

        public RecordingOutbox Outbox { get; }

        public RecordingUnitOfWork UnitOfWork { get; }

        public FakeLogger<AccountService> Logger { get; } = new();

        public List<string> Events { get; } = [];

        public AccountService Service { get; }

        public LoginCode AddVerificationCode(string number)
        {
            var code = LoginCode.Issue(
                LoginCodeDestination.ForPhone(PhoneNumber.Create(number).Value),
                LoginCodePurpose.VerifyDestination,
                Guid.CreateVersion7(),
                "hash-verify",
                Clock.GetUtcNow().UtcDateTime,
                TimeSpan.FromMinutes(10),
                maxAttempts: 5);
            Codes.Add(code);
            return code;
        }
    }

    private sealed class RecordingParser : IPhoneNumberParser
    {
        private readonly FakePhoneNumberParser _inner = new();

        public string? LastCountry { get; private set; }

        public string? LastNumber { get; private set; }

        public Result<PhoneNumber> Parse(string? country, string? number)
        {
            LastCountry = country;
            LastNumber = number;
            return _inner.Parse(country, number == RawPhone ? Phone : number);
        }

        public Result<PhoneNumber> FromWhatsAppId(string? waId) => _inner.FromWhatsAppId(waId);

        public string Mask(PhoneNumber phone) => _inner.Mask(phone);

        public string FormatInternational(PhoneNumber phone) => _inner.FormatInternational(phone);

        public string? RegionOf(PhoneNumber phone) => _inner.RegionOf(phone);
    }

    private sealed class RecordingOutbox(List<string> events) : IWhatsAppOutbox
    {
        public List<WhatsAppOutboundMessage> Messages { get; } = [];

        public bool Accepts { get; set; } = true;

        public bool TryEnqueue(WhatsAppOutboundMessage message)
        {
            events.Add("enqueue");
            if (Accepts)
            {
                Messages.Add(message);
            }

            return Accepts;
        }
    }

    private sealed class RecordingUnitOfWork(List<string> events, InMemoryLoginCodeRepository codes) : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public DateTime? SentAtSave { get; private set; }

        public Exception? Failure { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            events.Add("save");
            SaveCalls++;
            SentAtSave = codes.Codes.LastOrDefault()?.SentAtUtc;
            return Failure is { } error ? Task.FromException<int>(error) : Task.FromResult(1);
        }
    }
}
