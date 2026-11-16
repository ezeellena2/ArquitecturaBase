using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Channels;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

/// <summary>
/// Lo que comparten los cambios del número de una cuenta, sin saber qué ata un módulo al número: avisa a los
/// participantes (<see cref="IPhoneLinkParticipant"/>). Sus locks van antes del de los enlaces de la cuenta, el orden del
/// bot: al revés, Postgres corta a uno con un deadlock. Sin participantes (el núcleo sin módulo), solo toma el de la
/// cuenta. Qué hace el participante de WhatsApp lo prueba WhatsAppPhoneLinkParticipantTests.
/// </summary>
public sealed class PhoneNumberLinkerTests
{
    private const string Code = FakeLoginCodeGenerator.Code;

    private static readonly PhoneNumber Phone = PhoneNumber.Create("+5493515550101").Value;

    private static readonly PhoneNumber OtherPhone = PhoneNumber.Create("+5493515550102").Value;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly List<string> _events = [];
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryUserAccounts _accounts = new();
    private readonly InMemoryLoginCodeRepository _codes = new();
    private readonly InMemoryLoginLinkRepository _links;

    public PhoneNumberLinkerTests() => _links = new InMemoryLoginLinkRepository { Events = _events };

    [Fact]
    public async Task Participants_lock_before_the_account_links()
    {
        var participant = new RecordingPhoneLinkParticipant(_events);
        var userId = Guid.CreateVersion7();

        await Linker(participant).LockAsync(userId, Phone, Ct);

        Assert.Equal(["participant-lock:" + userId, "login-link:" + userId], _events);
        Assert.Equal([Phone], participant.LockedPhones);
    }

    [Fact]
    public async Task Without_a_new_phone_the_participant_locks_only_what_the_account_has()
    {
        var participant = new RecordingPhoneLinkParticipant(_events);

        await Linker(participant).LockAsync(Guid.CreateVersion7(), newPhone: null, Ct);

        Assert.Equal([null], participant.LockedPhones);
    }

    [Fact]
    public async Task Without_participants_only_the_account_links_are_locked()
    {
        var userId = Guid.CreateVersion7();

        await Linker().LockAsync(userId, Phone, Ct);

        Assert.Equal(["login-link:" + userId], _events);
    }

    [Fact]
    public async Task Participants_are_called_in_registration_order()
    {
        var userId = Guid.CreateVersion7();
        var linker = Linker(
            new RecordingPhoneLinkParticipant(_events, "first"), new RecordingPhoneLinkParticipant(_events, "second"));

        await linker.LockAsync(userId, Phone, Ct);
        await linker.ReleasePhoneAsync(userId, Ct);

        Assert.Equal(
            ["first-lock:" + userId, "second-lock:" + userId, "login-link:" + userId,
                "first-released:" + userId, "second-released:" + userId],
            _events);
    }

    [Fact]
    public async Task Confirming_the_own_phone_tells_the_participants_after_saving()
    {
        var participant = new RecordingPhoneLinkParticipant(_events);
        var user = _accounts.AddUser(email: null);
        AddVerificationCode(user.Id);
        string? phoneWhenTold = null;
        participant.WhenConfirmed = async () => phoneWhenTold = (await _accounts.FindByIdAsync(user.Id, Ct))?.PhoneNumber;

        var result = await Linker(participant).ConfirmOwnPhoneAsync(user.Id, Phone, Code, emailCode: null, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(Phone.Value, phoneWhenTold);
        Assert.Equal(
            ["participant-lock:" + user.Id, "login-link:" + user.Id, $"participant-confirmed:{user.Id}:{Phone.Value}"],
            _events);
    }

    [Fact]
    public async Task A_confirmation_that_fails_does_not_tell_the_participants()
    {
        // Con el código equivocado no se toma ningún lock; con el número de otra cuenta, sí, pero no se guarda nada.
        var participant = new RecordingPhoneLinkParticipant(_events);
        var user = _accounts.AddUser(email: null);
        var owner = _accounts.AddUser("otra@example.com", phoneNumber: Phone.Value);
        AddVerificationCode(user.Id);

        var wrongCode = await Linker(participant).ConfirmOwnPhoneAsync(user.Id, Phone, "000000", emailCode: null, Ct);
        var taken = await Linker(participant).ConfirmOwnPhoneAsync(user.Id, Phone, Code, emailCode: null, Ct);

        Assert.True(wrongCode.IsFailure);
        Assert.Equal(UserErrors.PhoneAlreadyExists, taken.Error);
        Assert.DoesNotContain(_events, entry => entry.StartsWith("participant-confirmed:", StringComparison.Ordinal));
        Assert.Equal(Phone.Value, (await _accounts.FindByIdAsync(owner.Id, Ct))?.PhoneNumber);
    }

    [Fact]
    public async Task A_verified_number_is_not_replaced_from_the_profile_and_no_code_is_looked_at()
    {
        // Cada medio confirma los cambios del otro (2026-11-16): para cambiar un número verificado, se desvincula con el
        // código del correo y se vincula el nuevo.
        var user = _accounts.AddUser("ana@example.com", phoneNumber: OtherPhone.Value);
        var phoneCode = AddVerificationCode(user.Id);
        var emailCode = AddEmailCode(user.Id);

        var result = await Linker(new RecordingPhoneLinkParticipant(_events))
            .ConfirmOwnPhoneAsync(user.Id, Phone, Code, Code, Ct);

        Assert.Equal(UserErrors.PhoneAlreadySet, result.Error);
        Assert.Null(phoneCode.ConsumedAtUtc);
        Assert.Null(emailCode.ConsumedAtUtc);
        Assert.Empty(_codes.LockedDestinations);
        Assert.Empty(_events);
        Assert.Equal(OtherPhone.Value, (await _accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
    }

    [Fact]
    public async Task With_a_verified_email_linking_needs_the_code_sent_to_it()
    {
        var user = _accounts.AddUser("ana@example.com");
        var phoneCode = AddVerificationCode(user.Id);

        var result = await Linker().ConfirmOwnPhoneAsync(user.Id, Phone, Code, emailCode: null, Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(UserErrors.ConfirmationCodeRequiredCode, error.Code);
        Assert.Equal(["emailCode"], error.Errors.Keys);
        Assert.Null(phoneCode.ConsumedAtUtc);
        Assert.Empty(_codes.LockedDestinations);
        Assert.Null((await _accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
    }

    [Fact]
    public async Task A_wrong_email_code_counts_its_attempt_on_that_field_and_keeps_the_number_code_usable()
    {
        var user = _accounts.AddUser("ana@example.com");
        var phoneCode = AddVerificationCode(user.Id);
        var emailCode = AddEmailCode(user.Id);

        var result = await Linker().ConfirmOwnPhoneAsync(user.Id, Phone, Code, "000000", Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(LoginCodeErrors.InvalidCode, error.Code);
        Assert.Equal(["emailCode"], error.Errors.Keys);
        Assert.Equal(1, emailCode.FailedAttempts);
        Assert.Null(phoneCode.ConsumedAtUtc);
        Assert.Equal(0, phoneCode.FailedAttempts);
        Assert.Null((await _accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
    }

    [Fact]
    public async Task A_code_to_verify_the_email_does_not_confirm_linking_a_number()
    {
        var user = _accounts.AddUser("ana@example.com");
        var phoneCode = AddVerificationCode(user.Id);
        var emailCode = AddEmailCode(user.Id, LoginCodePurpose.VerifyDestination);

        var result = await Linker().ConfirmOwnPhoneAsync(user.Id, Phone, Code, Code, Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Null(emailCode.ConsumedAtUtc);
        Assert.Null(phoneCode.ConsumedAtUtc);
        Assert.Null((await _accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
    }

    [Fact]
    public async Task With_both_codes_linking_locks_the_email_then_the_number_then_the_participants_and_the_account()
    {
        // El orden único, el de la edición de la administración: correo, número, participantes y cuenta.
        _codes.Events = _events;
        var user = _accounts.AddUser("ana@example.com");
        var phoneCode = AddVerificationCode(user.Id);
        var emailCode = AddEmailCode(user.Id);

        var result = await Linker(new RecordingPhoneLinkParticipant(_events))
            .ConfirmOwnPhoneAsync(user.Id, Phone, Code, Code, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                "login-code:ana@example.com", "login-code:" + Phone.Value, "participant-lock:" + user.Id,
                "login-link:" + user.Id, $"participant-confirmed:{user.Id}:{Phone.Value}",
            ],
            _events);
        Assert.NotNull(phoneCode.ConsumedAtUtc);
        Assert.NotNull(emailCode.ConsumedAtUtc);
        var updated = await _accounts.FindByIdAsync(user.Id, Ct);
        Assert.Equal(Phone.Value, updated!.PhoneNumber);
        Assert.True(updated.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task An_unverified_number_loaded_by_an_administrator_is_replaced_with_both_codes()
    {
        var user = _accounts.AddUser("ana@example.com");
        await _accounts.ArrangeAsync(accounts => accounts.SetPhoneAsync(user.Id, OtherPhone, confirmed: false, Ct));
        AddVerificationCode(user.Id);
        AddEmailCode(user.Id);

        var withoutEmailCode = await Linker().ConfirmOwnPhoneAsync(user.Id, Phone, Code, emailCode: null, Ct);
        var result = await Linker().ConfirmOwnPhoneAsync(user.Id, Phone, Code, Code, Ct);

        Assert.Equal(UserErrors.ConfirmationCodeRequiredCode, withoutEmailCode.Error.Code);
        Assert.True(result.IsSuccess);
        Assert.Equal(Phone.Value, (await _accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
    }

    [Fact]
    public async Task Confirming_the_same_verified_number_again_changes_nothing_and_needs_no_email_code()
    {
        // Vuelve a atar el chat al número (PF2), sin cambiar ningún medio de ingreso.
        var user = _accounts.AddUser("ana@example.com", phoneNumber: Phone.Value);
        var phoneCode = AddVerificationCode(user.Id);

        var result = await Linker().ConfirmOwnPhoneAsync(user.Id, Phone, Code, emailCode: null, Ct);

        Assert.True(result.IsSuccess);
        Assert.NotNull(phoneCode.ConsumedAtUtc);
    }

    [Fact]
    public async Task Releasing_the_phone_tells_every_participant()
    {
        var userId = Guid.CreateVersion7();
        var linker = Linker(
            new RecordingPhoneLinkParticipant(_events, "first"), new RecordingPhoneLinkParticipant(_events, "second"));

        await linker.ReleasePhoneAsync(userId, Ct);

        Assert.Equal(["first-released:" + userId, "second-released:" + userId], _events);
    }

    private PhoneNumberLinker Linker(params IPhoneLinkParticipant[] participants) => new(
        _accounts,
        _accounts,
        new DestinationCodeVerifier(_codes, new FakeLoginCodeHasher(), _clock),
        participants,
        _links,
        _clock);

    private LoginCode AddVerificationCode(Guid userId)
    {
        var code = LoginCode.Issue(
            LoginCodeDestination.ForPhone(Phone),
            LoginCodePurpose.VerifyDestination,
            userId,
            FakeLoginCodeHasher.HashOf(Phone.Value, LoginCodePurpose.VerifyDestination, Code),
            _clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(10),
            maxAttempts: 5);
        _codes.Add(code);
        return code;
    }

    /// <summary>El código al correo de la cuenta que confirma el cambio, como el que pide el perfil con LinkPhone.</summary>
    private LoginCode AddEmailCode(Guid userId, LoginCodePurpose purpose = LoginCodePurpose.ConfirmChange)
    {
        var email = Email.Create("ana@example.com").Value;
        var code = LoginCode.Issue(
            LoginCodeDestination.ForEmail(email),
            purpose,
            userId,
            FakeLoginCodeHasher.HashOf(email.Value, purpose, Code),
            _clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(10),
            maxAttempts: 5);
        _codes.Add(code);
        return code;
    }
}
