using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Integrations.Security;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;

internal sealed class InMemoryLoginCodeRepository : ILoginCodeRepository
{
    public List<LoginCode> Codes { get; } = [];

    public List<string> LockedDestinations { get; } = [];

    /// <summary>Si no es null, tomar el lock fuera de la transacción lanza (ver <see cref="TransactionGuard"/>).</summary>
    public Func<bool>? InTransaction { get; set; }

    public Task LockDestinationAsync(LoginCodeDestination destination, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        LockedDestinations.Add(destination.Value);

        return Task.CompletedTask;
    }

    public Task<LoginCode?> GetLatestAsync(
        LoginCodeDestination destination,
        LoginCodePurpose purpose,
        Guid? requestedByUserId,
        CancellationToken cancellationToken) =>
        Task.FromResult(CodesOf(destination)
            .Where(code => code.Purpose == purpose && code.RequestedByUserId == requestedByUserId && code.InvalidatedAtUtc is null)
            .MaxBy(code => code.CreatedAtUtc));

    public Task<IReadOnlyList<LoginCode>> ListActiveAsync(
        LoginCodeDestination destination,
        LoginCodePurpose purpose,
        Guid? requestedByUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LoginCode>>(CodesOf(destination)
            .Where(code => code.Purpose == purpose && code.RequestedByUserId == requestedByUserId && code.IsActive(nowUtc))
            .ToList());

    // Sin mirar el propósito: los límites son por destino.
    public Task<IReadOnlyList<DateTime>> ListRequestTimesSinceAsync(
        LoginCodeDestination destination,
        DateTime sinceUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DateTime>>(CodesOf(destination)
            .Where(code => code.CreatedAtUtc > sinceUtc)
            .Select(code => code.CreatedAtUtc)
            .Order()
            .ToList());

    // A cualquier destino y con cualquier propósito: el tope diario es por canal.
    public Task<IReadOnlyList<DateTime>> ListLatestSentTimesAsync(
        LoginCodeChannel channel,
        DateTime sinceUtc,
        int count,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DateTime>>(Codes
            .Where(code => code.Channel == channel && code.SentAtUtc > sinceUtc)
            .Select(code => code.SentAtUtc!.Value)
            .OrderDescending()
            .Take(count)
            .ToList());

    public void Add(LoginCode loginCode) => Codes.Add(loginCode);

    private IEnumerable<LoginCode> CodesOf(LoginCodeDestination destination) =>
        Codes.Where(code => code.Destination == destination.Value);
}

internal sealed class InMemoryLoginAuditRepository : ILoginAuditRepository
{
    public List<LoginAudit> Audits { get; } = [];

    public void Add(LoginAudit audit) => Audits.Add(audit);

    public Task<DateTime?> FindLastSuccessAtUtcAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Audits
            .Where(audit => audit.UserId == userId && audit.Succeeded)
            .Select(audit => (DateTime?)audit.OccurredAtUtc)
            .Max());
}

internal sealed class InMemoryLoginLinkRepository : ILoginLinkRepository
{
    public List<LoginLink> Links { get; } = [];

    public List<Guid> LockedAccounts { get; } = [];

    /// <summary>
    /// Si no es null, cada lock se anota también acá ("login-link:" y la cuenta): compartida con otros dobles, un test ve
    /// el orden de todos los locks.
    /// </summary>
    public List<string>? Events { get; init; }

    /// <summary>
    /// Lo que hace otro pedido mientras el caso de uso espera el primer lock de una cuenta: el que lo tenía cambia la
    /// cuenta y confirma. Corre una sola vez, con el Id de la cuenta. Así un test cruza al caso de uso con otro pedido en
    /// el orden que quiere.
    /// </summary>
    public Func<Guid, Task>? WhileWaitingForTheLock { get; set; }

    /// <summary>Si no es null, tomar el lock fuera de la transacción lanza (ver <see cref="TransactionGuard"/>).</summary>
    public Func<bool>? InTransaction { get; set; }

    public async Task LockAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);

        if (WhileWaitingForTheLock is { } whileWaiting)
        {
            WhileWaitingForTheLock = null;
            await whileWaiting(userId);
        }

        LockedAccounts.Add(userId);
        Events?.Add("login-link:" + userId);
    }

    public Task<Guid?> FindUserIdAsync(string tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(Links.SingleOrDefault(link => link.TokenHash == tokenHash)?.UserId);

    public Task<LoginLink?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(Links.SingleOrDefault(link => link.TokenHash == tokenHash));

    public Task<IReadOnlyList<LoginLink>> ListActiveAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LoginLink>>(Links.Where(link => link.UserId == userId && link.IsActive(nowUtc)).ToList());

    public Task<IReadOnlyList<LoginLink>> ListPendingAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LoginLink>>(Links
            .Where(link => link.UserId == userId && link.ConsumedAtUtc is null && link.InvalidatedAtUtc is null)
            .ToList());

    public Task<IReadOnlyList<DateTime>> ListIssueTimesSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DateTime>>(Links
            .Where(link => link.UserId == userId && link.CreatedAtUtc > sinceUtc)
            .Select(link => link.CreatedAtUtc)
            .Order()
            .ToList());

    public void Add(LoginLink loginLink) => Links.Add(loginLink);
}

/// <summary>Tokens previsibles ("token-1", "token-2"…) y un hash que se reconoce a simple vista.</summary>
internal sealed class FakeSecureTokenGenerator : ISecureTokenGenerator
{
    private int _issued;

    public static string HashOf(string token) => "sha:" + token;

    public string Generate() => "token-" + ++_issued;

    public string Hash(string token) => HashOf(token);
}

internal sealed record FakePublicOrigin(Uri? Value) : IPublicOrigin;

internal sealed class FakeLoginCodeGenerator : ILoginCodeGenerator
{
    public const string Code = "123456";

    public string Generate() => Code;
}

internal sealed class FakeLoginCodeHasher : ILoginCodeHasher
{
    public static string HashOf(string destination, LoginCodePurpose purpose, string code) => $"hash:{destination}:{purpose}:{code}";

    public string Hash(LoginCodeDestination destination, LoginCodePurpose purpose, string code) =>
        HashOf(destination.Value, purpose, code);
}

internal sealed class FakeEmailTemplateRenderer : IEmailTemplateRenderer
{
    public string? LastCode { get; private set; }

    public CultureInfo? LastCulture { get; private set; }

    /// <summary>El botón de la última invitación por correo.</summary>
    public string? LastLoginUrl { get; private set; }

    public EmailMessage RenderLoginCode(string to, string code, int lifetimeMinutes, CultureInfo culture)
    {
        LastCode = code;
        LastCulture = culture;

        return new EmailMessage(to, "subject", "<p>html</p>", "text");
    }

    public EmailMessage RenderEmailVerificationCode(string to, string code, int lifetimeMinutes, CultureInfo culture)
    {
        LastCode = code;
        LastCulture = culture;

        return new EmailMessage(to, "verification subject", "<p>html</p>", "text");
    }

    public EmailMessage RenderInvitation(string to, string? displayName, string loginUrl, CultureInfo culture)
    {
        LastCulture = culture;
        LastLoginUrl = loginUrl;

        return new EmailMessage(to, "invitation subject", "<p>html</p>", "text");
    }
}

/// <summary>Guarda lo que se encoló. Con <see cref="Accepts"/> en false, hace de cola llena.</summary>
internal sealed class FakeEmailQueue : IEmailQueue
{
    public List<EmailMessage> Messages { get; } = [];

    public bool Accepts { get; set; } = true;

    /// <summary>Corre al encolar, antes de aceptar o no el mensaje: así un test ve qué había pasado hasta ese momento.</summary>
    public Action<EmailMessage>? WhenEnqueued { get; set; }

    public bool TryEnqueue(EmailMessage message)
    {
        WhenEnqueued?.Invoke(message);

        if (Accepts)
        {
            Messages.Add(message);
        }

        return Accepts;
    }
}

internal sealed class FakeRequestInfo : IRequestInfo
{
    public string? IpAddress { get; init; } = "203.0.113.10";

    public string? UserAgent { get; init; } = "unit-tests";
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }

    public bool IsAuthenticated => UserId is not null;
}

internal sealed class FakePermissionService : IPermissionService
{
    public Dictionary<Guid, string[]> Permissions { get; } = [];

    public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>(Permissions.GetValueOrDefault(userId) ?? []);

    public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
        Task.FromResult(Permissions.GetValueOrDefault(userId)?.Contains(permission) ?? false);

    public Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// IPhoneNumberParser sin libphonenumber: acepta solo números ya en formato internacional y sabe el país de unos
/// pocos prefijos. Las reglas de verdad se prueban en LibPhoneNumberParserTests.
/// </summary>
internal sealed class FakePhoneNumberParser : IPhoneNumberParser
{
    private static readonly (string Prefix, string Region)[] Regions = [("+598", "UY"), ("+54", "AR"), ("+55", "BR")];

    public Result<PhoneNumber> Parse(string? country, string? number) => PhoneNumber.Create(number);

    public string Mask(PhoneNumber phone) => "masked " + phone.Value[^4..];

    public string FormatInternational(PhoneNumber phone) => "formatted " + phone.Value;

    public string? RegionOf(PhoneNumber phone) =>
        Regions.FirstOrDefault(entry => phone.Value.StartsWith(entry.Prefix, StringComparison.Ordinal)).Region;
}

internal sealed record FakeGoogleAvailability(bool IsEnabled) : IGoogleAvailability;

internal sealed class FakeSystemSettingsReader : ISystemSettingsReader
{
    public ArquitecturaBase.Application.Models.Settings.SystemPresentationResponse Presentation { get; set; } =
        ArquitecturaBase.Application.Models.Settings.SystemPresentationResponse.Defaults;
    public Task<ArquitecturaBase.Application.Models.Settings.SystemPresentationResponse> FindPresentationAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Presentation);
    public RegistrationMode Mode { get; set; } = RegistrationMode.Open;

    public Task<RegistrationMode> FindRegistrationModeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Mode);
}

/// <summary>
/// El administrador inicial (Seed:AdminEmail). Viene configurado, como en una instalación real, y con un correo que no
/// usan los demás tests: así los de InviteOnly prueban de paso que la excepción es solo para él.
/// </summary>
internal sealed class FakeInitialAdmin : IInitialAdmin
{
    public const string DefaultEmail = "admin@example.com";

    /// <summary>Null es Seed:AdminEmail vacío: no hay administrador inicial.</summary>
    public string? AdminEmail { get; set; } = DefaultEmail;

    public bool IsInitialAdmin(Email email) => email.Value == AdminEmail;
}
