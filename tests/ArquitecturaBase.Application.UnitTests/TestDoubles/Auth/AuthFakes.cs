using System.Globalization;
using ArquitecturaBase.Application.Abstractions.Emails;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;

internal sealed class InMemoryLoginCodeRepository : ILoginCodeRepository
{
    public List<LoginCode> Codes { get; } = [];

    public List<string> LockedEmails { get; } = [];

    public Task LockEmailAsync(Email email, CancellationToken cancellationToken)
    {
        LockedEmails.Add(email.Value);

        return Task.CompletedTask;
    }

    public Task<LoginCode?> GetLatestAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(Codes
            .Where(code => code.Email == email.Value && code.InvalidatedAtUtc is null)
            .MaxBy(code => code.CreatedAtUtc));

    public Task<IReadOnlyList<LoginCode>> ListActiveAsync(Email email, DateTime nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LoginCode>>(Codes.Where(code => code.Email == email.Value && code.IsActive(nowUtc)).ToList());

    public Task<IReadOnlyList<DateTime>> ListRequestTimesSinceAsync(Email email, DateTime sinceUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DateTime>>(Codes
            .Where(code => code.Email == email.Value && code.CreatedAtUtc > sinceUtc)
            .Select(code => code.CreatedAtUtc)
            .Order()
            .ToList());

    public void Add(LoginCode loginCode) => Codes.Add(loginCode);
}

internal sealed class InMemoryLoginAuditRepository : ILoginAuditRepository
{
    public List<LoginAudit> Audits { get; } = [];

    public void Add(LoginAudit audit) => Audits.Add(audit);

    public Task<DateTime?> GetLastSuccessAtUtcAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Audits
            .Where(audit => audit.UserId == userId && audit.Succeeded)
            .Select(audit => (DateTime?)audit.OccurredAtUtc)
            .Max());
}

internal sealed class FakeLoginCodeGenerator : ILoginCodeGenerator
{
    public const string Code = "123456";

    public string Generate() => Code;
}

internal sealed class FakeLoginCodeHasher : ILoginCodeHasher
{
    public static string HashOf(string email, string code) => $"hash:{email}:{code}";

    public string Hash(Email email, string code) => HashOf(email.Value, code);
}

internal sealed class FakeEmailTemplateRenderer : IEmailTemplateRenderer
{
    public string? LastCode { get; private set; }

    public CultureInfo? LastCulture { get; private set; }

    public EmailMessage RenderLoginCode(string to, string code, int lifetimeMinutes, CultureInfo culture)
    {
        LastCode = code;
        LastCulture = culture;

        return new EmailMessage(to, "subject", "<p>html</p>", "text");
    }
}

internal sealed class FakeEmailQueue : IEmailQueue
{
    public List<EmailMessage> Messages { get; } = [];

    public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Messages.Add(message);

        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeRequestInfo : IRequestInfo
{
    public string? IpAddress { get; init; } = "203.0.113.10";

    public string? UserAgent { get; init; } = "unit-tests";
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; init; }

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

internal sealed class FakeSystemSettingsReader : ISystemSettingsReader
{
    public RegistrationMode Mode { get; set; } = RegistrationMode.Open;

    public int Invalidations { get; private set; }

    public Task<RegistrationMode> GetRegistrationModeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Mode);

    public Task InvalidateAsync(CancellationToken cancellationToken)
    {
        Invalidations++;

        return Task.CompletedTask;
    }
}
