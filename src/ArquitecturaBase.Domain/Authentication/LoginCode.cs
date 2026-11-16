using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authentication;

/// <summary>
/// Código de un solo uso mandado a un destino, un correo o un número de WhatsApp, para un propósito: entrar o
/// vincular ese destino a una cuenta (secciones 5.3 del spec y 6.3 del spec del ingreso con WhatsApp). Solo se
/// guarda su hash. Vence, admite una cantidad máxima de intentos y queda invalidado cuando se pide otro para el mismo
/// destino y propósito (y, si es para vincular, por la misma cuenta).
/// </summary>
public sealed class LoginCode : Entity
{
    /// <summary>Lo más largo de una dirección agrupada: una IPv6 escrita entera y su "/64".</summary>
    public const int MaxRequestedFromAddressLength = 64;

    // Para EF Core.
    private LoginCode()
    {
        Destination = string.Empty;
        CodeHash = string.Empty;
    }

    private LoginCode(
        LoginCodeDestination destination,
        LoginCodePurpose purpose,
        Guid? requestedByUserId,
        string codeHash,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        int maxAttempts,
        string? requestedFromAddress)
    {
        Destination = destination.Value;
        Channel = destination.Channel;
        Purpose = purpose;
        RequestedByUserId = requestedByUserId;
        CodeHash = codeHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        MaxAttempts = maxAttempts;
        RequestedFromAddress = requestedFromAddress;
    }

    /// <summary>El correo normalizado o el número en formato internacional.</summary>
    public string Destination { get; private set; }

    public LoginCodeChannel Channel { get; private set; }

    public LoginCodePurpose Purpose { get; private set; }

    /// <summary>
    /// La cuenta que pidió un código de <see cref="LoginCodePurpose.VerifyDestination"/> o
    /// <see cref="LoginCodePurpose.ConfirmChange"/>; null en los de ingreso.
    /// </summary>
    public Guid? RequestedByUserId { get; private set; }

    /// <summary>
    /// N7: en un código por correo, la dirección de quien lo pidió, agrupada como en los límites por IP (una IPv4 entera,
    /// una IPv6 por su /64), o null si no se conocía. La cuenta el tope diario por dirección de los códigos por correo, que
    /// lee la base: vale para todas las copias y sobrevive a un reinicio. Un código por teléfono no la guarda: nadie la lee
    /// y es un dato personal.
    /// </summary>
    public string? RequestedFromAddress { get; private set; }

    public string CodeHash { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>
    /// Cuándo se encoló el mensaje con el código, o null si no se mandó nada: en InviteOnly, a un destino sin cuenta
    /// se le emite el código igual pero no se le manda. Si la cola descarta el mensaje, la fecha queda igual: cuenta
    /// lo que se intentó mandar.
    /// </summary>
    public DateTime? SentAtUtc { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public DateTime? InvalidatedAtUtc { get; private set; }

    public int FailedAttempts { get; private set; }

    public int MaxAttempts { get; private set; }

    public int AttemptsLeft => Math.Max(0, MaxAttempts - FailedAttempts);

    /// <summary>
    /// Emite un código. La cuenta que lo pide es obligatoria con <see cref="LoginCodePurpose.VerifyDestination"/> y
    /// <see cref="LoginCodePurpose.ConfirmChange"/>, y no se admite con <see cref="LoginCodePurpose.SignIn"/>: un código
    /// para entrar todavía no es de ninguna cuenta. <paramref name="requestedFromAddress"/> es la dirección agrupada de
    /// quien lo pide (<see cref="RequestedFromAddress"/>).
    /// </summary>
    public static LoginCode Issue(
        LoginCodeDestination destination,
        LoginCodePurpose purpose,
        Guid? requestedByUserId,
        string codeHash,
        DateTime nowUtc,
        TimeSpan lifetime,
        int maxAttempts,
        string? requestedFromAddress = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        if (requestedFromAddress is { Length: > MaxRequestedFromAddressLength })
        {
            throw new ArgumentException(
                $"The address cannot be longer than {MaxRequestedFromAddressLength} characters.", nameof(requestedFromAddress));
        }

        switch (purpose)
        {
            case LoginCodePurpose.SignIn when requestedByUserId is not null:
                throw new ArgumentException("A sign-in code can't be requested by an account.", nameof(requestedByUserId));

            case LoginCodePurpose.VerifyDestination or LoginCodePurpose.ConfirmChange
                when requestedByUserId is null || requestedByUserId == Guid.Empty:
                throw new ArgumentException(
                    "A code to verify a destination or confirm a change needs the account that requested it.",
                    nameof(requestedByUserId));

            case LoginCodePurpose.SignIn or LoginCodePurpose.VerifyDestination or LoginCodePurpose.ConfirmChange:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown login code purpose.");
        }

        return new LoginCode(destination, purpose, requestedByUserId, codeHash, nowUtc, nowUtc + lifetime, maxAttempts, requestedFromAddress);
    }

    public bool IsActive(DateTime nowUtc) =>
        ConsumedAtUtc is null && InvalidatedAtUtc is null && nowUtc < ExpiresAtUtc && FailedAttempts < MaxAttempts;

    /// <summary>Lo invalida porque se pidió un código nuevo. Conserva el primer momento de invalidación.</summary>
    public void Invalidate(DateTime nowUtc)
    {
        InvalidatedAtUtc ??= nowUtc;
    }

    /// <summary>Registra que el mensaje con el código se encoló para el destino. Conserva el primer envío.</summary>
    public void MarkSent(DateTime nowUtc)
    {
        SentAtUtc ??= nowUtc;
    }

    /// <summary>
    /// Verifica un código de <see cref="LoginCodePurpose.SignIn"/>. Uno de otro propósito se rechaza igual que la
    /// falta de código, sin gastar sus intentos.
    /// </summary>
    public Result Verify(string codeHash, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(codeHash);

        return Purpose is LoginCodePurpose.SignIn ? Check(codeHash, nowUtc) : LoginCodeErrors.Invalid(attemptsLeft: null);
    }

    /// <summary>
    /// Verifica un código de una cuenta (<see cref="LoginCodePurpose.VerifyDestination"/> o
    /// <see cref="LoginCodePurpose.ConfirmChange"/>) para la cuenta <paramref name="userId"/>. Si el código es de ingreso
    /// o lo pidió otra cuenta, la respuesta es la misma que cuando no hay código y no se toca ningún contador: así no se
    /// sabe que otra cuenta está vinculando ese destino, ni se le gastan los intentos. Que el propósito sea el que se
    /// busca lo asegura quien lo trae (el repositorio busca por propósito, y el hash lo incluye).
    /// </summary>
    public Result VerifyFor(Guid userId, string codeHash, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(codeHash);

        return IsOf(userId)
            ? Check(codeHash, nowUtc)
            : LoginCodeErrors.Invalid(attemptsLeft: null);
    }

    /// <summary>
    /// Como <see cref="VerifyFor"/>, pero un código correcto no se gasta: un cambio que pide dos códigos (N11: agregar
    /// un correo con el del número que la cuenta ya tiene, o vincular un número con el del correo) revisa los dos y los
    /// gasta juntos con <see cref="Consume"/>, así un error de tipeo en uno no le gasta el otro a la persona. Uno
    /// equivocado cuenta el intento igual.
    /// </summary>
    public Result CheckFor(Guid userId, string codeHash, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(codeHash);

        return IsOf(userId)
            ? Check(codeHash, nowUtc, consume: false)
            : LoginCodeErrors.Invalid(attemptsLeft: null);
    }

    /// <summary>
    /// Gasta un código que <see cref="CheckFor"/> ya dio por bueno, con la hora de esa revisión: así uno que vence entre
    /// revisar y gastar se gasta igual. Uno que no está activo a esa hora (usado, invalidado, vencido o sin intentos) es
    /// un error de programación: nadie lo revisó antes.
    /// </summary>
    public void Consume(DateTime nowUtc)
    {
        if (!IsActive(nowUtc))
        {
            throw new InvalidOperationException("Only an active code can be consumed.");
        }

        ConsumedAtUtc = nowUtc;
    }

    // Los códigos de una cuenta: los de ingreso no son de nadie.
    private bool IsOf(Guid userId) =>
        Purpose is LoginCodePurpose.VerifyDestination or LoginCodePurpose.ConfirmChange && RequestedByUserId == userId;

    /// <remarks>
    /// Usado y vencido se dicen sólo al código que coincide (N3): con uno cualquiera, la respuesta es la de no tener
    /// código, así no se sabe si alguien ya entró con ese destino. Ninguno de los dos gasta intentos, y un código muerto
    /// no deja entrar aunque se adivine.
    /// </remarks>
    private Result Check(string codeHash, DateTime nowUtc, bool consume = true)
    {
        // Comparación en tiempo constante: no revela cuántos caracteres coinciden.
        var matches = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(CodeHash), Encoding.UTF8.GetBytes(codeHash));

        if (ConsumedAtUtc is not null)
        {
            return matches ? LoginCodeErrors.AlreadyUsed : LoginCodeErrors.Invalid(attemptsLeft: null);
        }

        if (InvalidatedAtUtc is not null)
        {
            return LoginCodeErrors.Invalid(attemptsLeft: null);
        }

        if (nowUtc >= ExpiresAtUtc)
        {
            return matches ? LoginCodeErrors.Expired : LoginCodeErrors.Invalid(attemptsLeft: null);
        }

        if (FailedAttempts >= MaxAttempts)
        {
            return LoginCodeErrors.TooManyAttempts;
        }

        if (!matches)
        {
            FailedAttempts++;

            return FailedAttempts >= MaxAttempts ? LoginCodeErrors.TooManyAttempts : LoginCodeErrors.Invalid(AttemptsLeft);
        }

        if (consume)
        {
            ConsumedAtUtc = nowUtc;
        }

        return Result.Success();
    }
}
