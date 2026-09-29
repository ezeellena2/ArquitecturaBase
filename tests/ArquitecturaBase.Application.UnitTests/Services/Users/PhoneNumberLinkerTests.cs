using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Channels;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Authentication;
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
        var user = _accounts.AddUser("ana@example.com");
        AddVerificationCode(user.Id);
        string? phoneWhenTold = null;
        participant.WhenConfirmed = async () => phoneWhenTold = (await _accounts.FindByIdAsync(user.Id, Ct))?.PhoneNumber;

        var result = await Linker(participant).ConfirmOwnPhoneAsync(user.Id, Phone, Code, Ct);

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
        var user = _accounts.AddUser("ana@example.com");
        var owner = _accounts.AddUser("otra@example.com", phoneNumber: Phone.Value);
        AddVerificationCode(user.Id);

        var wrongCode = await Linker(participant).ConfirmOwnPhoneAsync(user.Id, Phone, "000000", Ct);
        var taken = await Linker(participant).ConfirmOwnPhoneAsync(user.Id, Phone, Code, Ct);

        Assert.True(wrongCode.IsFailure);
        Assert.Equal(UserErrors.PhoneAlreadyExists, taken.Error);
        Assert.DoesNotContain(_events, entry => entry.StartsWith("participant-confirmed:", StringComparison.Ordinal));
        Assert.Equal(Phone.Value, (await _accounts.FindByIdAsync(owner.Id, Ct))?.PhoneNumber);
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

    private void AddVerificationCode(Guid userId) => _codes.Add(LoginCode.Issue(
        LoginCodeDestination.ForPhone(Phone),
        LoginCodePurpose.VerifyDestination,
        userId,
        FakeLoginCodeHasher.HashOf(Phone.Value, LoginCodePurpose.VerifyDestination, Code),
        _clock.GetUtcNow().UtcDateTime,
        TimeSpan.FromMinutes(10),
        maxAttempts: 5));
}
