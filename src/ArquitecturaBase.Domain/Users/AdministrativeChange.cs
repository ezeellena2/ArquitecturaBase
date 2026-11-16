using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Users;

/// <summary>
/// Un cambio administrativo sobre un rol, una cuenta o la configuración (N13 de la revisión del núcleo, 2026-11-14): quién
/// lo hizo (<see cref="ActorUserId"/>, vacío en el seed), sobre qué (<see cref="TargetId"/> y, en un rol, su nombre), qué
/// se agregó y qué se quitó (permisos o roles) y el estado antes y después. Cada fila es un dato que cambió (ver
/// <see cref="AdministrativeChangeKind"/>), se escribe en el mismo límite que el cambio y no se modifica nunca. Del correo
/// y del número guarda sólo el estado (<see cref="ContactState"/>), nunca el dato, como los logs.
/// </summary>
public sealed class AdministrativeChange : Entity
{
    /// <summary>El nombre de un rol o un estado; alcanza para la descripción de un rol, la más larga.</summary>
    public const int TextMaxLength = 256;

    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Deleted = "Deleted";
    public const string None = "None";
    public const string Unverified = "Unverified";
    public const string Verified = "Verified";

    // Para EF Core.
    private AdministrativeChange()
    {
        Added = [];
        Removed = [];
    }

    private AdministrativeChange(
        AdministrativeChangeKind kind,
        Guid? actorUserId,
        Guid targetId,
        DateTime occurredAtUtc,
        string? targetName,
        string[] added,
        string[] removed,
        string? before,
        string? after)
    {
        Kind = kind;
        ActorUserId = actorUserId;
        TargetId = targetId;
        OccurredAtUtc = occurredAtUtc;
        TargetName = targetName;
        Added = added;
        Removed = removed;
        Before = before;
        After = after;
    }

    public AdministrativeChangeKind Kind { get; private set; }

    /// <summary>Quién lo hizo; null, el seed.</summary>
    public Guid? ActorUserId { get; private set; }

    /// <summary>El id del rol, de la cuenta o de la fila de la configuración.</summary>
    public Guid TargetId { get; private set; }

    /// <summary>El nombre del rol en ese momento, que sobrevive a su borrado; null en una cuenta o la configuración.</summary>
    public string? TargetName { get; private set; }

    /// <summary>Los permisos o roles que se agregaron, sin repetir y en orden ordinal.</summary>
    public string[] Added { get; private set; }

    /// <summary>Los permisos o roles que se quitaron, sin repetir y en orden ordinal.</summary>
    public string[] Removed { get; private set; }

    public string? Before { get; private set; }

    public string? After { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>
    /// Un cambio. Un tipo desconocido, un objeto o un actor vacíos, o un texto más largo que
    /// <see cref="TextMaxLength"/> son errores de programación y lanzan.
    /// </summary>
    public static AdministrativeChange Create(
        AdministrativeChangeKind kind,
        Guid? actorUserId,
        Guid targetId,
        DateTime occurredAtUtc,
        string? targetName = null,
        IEnumerable<string>? added = null,
        IEnumerable<string>? removed = null,
        string? before = null,
        string? after = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown administrative change.");
        }

        if (targetId == Guid.Empty)
        {
            throw new ArgumentException("The target cannot be empty.", nameof(targetId));
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("The actor is null for the seed, never empty.", nameof(actorUserId));
        }

        return new(
            kind,
            actorUserId,
            targetId,
            occurredAtUtc,
            Fit(targetName, nameof(targetName)),
            Normalize(added),
            Normalize(removed),
            Fit(before, nameof(before)),
            Fit(after, nameof(after)));
    }

    /// <summary>El estado de un correo o un número, sin el dato.</summary>
    public static string ContactState(bool present, bool verified) =>
        !present ? None : verified ? Verified : Unverified;

    public static string Status(bool isActive) => isActive ? Active : Inactive;

    private static string[] Normalize(IEnumerable<string>? values) =>
        values is null ? [] : [.. values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static string? Fit(string? value, string name) =>
        value is { Length: > TextMaxLength }
            ? throw new ArgumentException($"The text cannot be longer than {TextMaxLength}.", name)
            : value;
}
