using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>
/// Configuración › WhatsApp (RC3-A, 2026-11-22): how long a form sent by WhatsApp accepts its answer, the countries whose numbers
/// receive codes and are accepted to sign in or link, and after how many days the content of the messages and of the assistant is
/// cleared. One row, whose key is always <see cref="SingletonId"/>; until it is saved every value is the one appsettings had before
/// (24 hours, Argentina, 90 days). Internal configuration (ADR 0018): each save advances the revision and brings the one it read.
/// </summary>
public sealed class WhatsAppSettings : Entity, IAuditable
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-00000000a9a1");

    public const int MinFlowSessionLifetimeHours = 1;
    public const int MaxFlowSessionLifetimeHours = 168;
    public const int DefaultFlowSessionLifetimeHours = 24;
    public const int MaxAllowedCountries = 250;
    public const int MinMessageRetentionDays = 1;
    public const int MaxMessageRetentionDays = 3650;
    public const int DefaultMessageRetentionDays = 90;

    /// <summary>Only Argentina, as appsettings had it.</summary>
    public static IReadOnlyList<string> DefaultAllowedCountries { get; } = ["AR"];

    private WhatsAppSettings()
        : base(SingletonId)
    {
    }

    public int FlowSessionLifetimeHours { get; private set; } = DefaultFlowSessionLifetimeHours;

    /// <summary>ISO 3166-1 alpha-2 codes in upper case, each once and in order.</summary>
    public IReadOnlyList<string> AllowedCountries { get; private set; } = DefaultAllowedCountries;

    public int MessageRetentionDays { get; private set; } = DefaultMessageRetentionDays;

    public long Revision { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    /// <summary>The settings before their first save: the values of before the screen, revision 0.</summary>
    public static WhatsAppSettings Create() => new();

    /// <summary>Whether codes go to numbers of <paramref name="region"/>, the country of the number (null: unknown).</summary>
    public bool Allows(string? region) => region is not null && AllowedCountries.Contains(region, StringComparer.Ordinal);

    /// <summary>Applies the three values with one revision; nothing changes if one of them is invalid.</summary>
    public Result Update(int flowSessionLifetimeHours, IReadOnlyList<string> allowedCountries, int messageRetentionDays)
    {
        ArgumentNullException.ThrowIfNull(allowedCountries);
        if (flowSessionLifetimeHours is < MinFlowSessionLifetimeHours or > MaxFlowSessionLifetimeHours
            || messageRetentionDays is < MinMessageRetentionDays or > MaxMessageRetentionDays
            || allowedCountries.Count is 0 or > MaxAllowedCountries || !allowedCountries.All(IsCountryCode))
        {
            return WhatsAppSettingsErrors.Invalid;
        }

        FlowSessionLifetimeHours = flowSessionLifetimeHours;
        AllowedCountries = [.. allowedCountries.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        MessageRetentionDays = messageRetentionDays;
        Revision = checked(Revision + 1);
        return Result.Success();
    }

    /// <summary>Someone saved since it was read: before the first save, the revision read is 0.</summary>
    public Result EnsureRevision(long expectedRevision) =>
        expectedRevision == Revision ? Result.Success() : WhatsAppSettingsErrors.RevisionConflict;

    /// <summary>An ISO 3166-1 alpha-2 code as the channel keeps it: two ASCII letters in upper case ("AR").</summary>
    public static bool IsCountryCode(string? country) =>
        country is { Length: 2 } && char.IsAsciiLetterUpper(country[0]) && char.IsAsciiLetterUpper(country[1]);
}
