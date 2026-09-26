using System.Reflection;
using System.Runtime.ExceptionServices;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Persistence.Readers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// Cuántas búsquedas escondió <see cref="StaleIdentityReads"/>. El test la crea, se la pasa y, junto al 409, afirma que
/// hubo al menos una: así sabe que el chequeo previo leyó por la lectura vieja y que el 409 salió del choque.
/// </summary>
public sealed class StaleReadsProbe
{
    private int _hiddenLookups;

    public int HiddenLookups => Volatile.Read(ref _hiddenLookups);

    internal void Hit() => Interlocked.Increment(ref _hiddenLookups);
}

/// <summary>
/// El IdentityService y el UserReader reales, con una lectura vieja: las búsquedas de un número o de un correo dicen
/// que no es de nadie, ni de una cuenta activa ni de una borrada, aunque ya lo sea. Es lo que ve un pedido cuando otra
/// cuenta se queda con el número justo entre su búsqueda y su guardado: así un test llega al choque con el índice único
/// sin depender de cómo se crucen dos pedidos. Todo lo demás pasa tal cual al servicio real.
/// </summary>
/// <remarks>
/// Es un <see cref="DispatchProxy"/> para no repetir a mano los métodos de las dos interfaces. Es pública y no está
/// sellada porque el proxy se arma heredando de ella. Tapa las dos puertas: Application lee las cuentas por
/// <see cref="IUserReader"/> (el alta, la edición y el perfil) y por <see cref="IIdentityService"/> (el ingreso). Si
/// quedara una abierta, el 409 saldría del chequeo previo sin pasar por el choque ni por el savepoint, y el test
/// seguiría en verde: por eso cada búsqueda escondida suma en <see cref="StaleReadsProbe"/>, y el test afirma que hubo
/// alguna. Si una búsqueda se muda a otra interfaz o cambia de nombre, la sonda queda en cero y el test falla.
/// </remarks>
public class StaleIdentityReads : DispatchProxy
{
    private static readonly string[] HiddenLookups =
    [
        nameof(IUserReader.FindByPhoneAsync),
        nameof(IUserReader.IsDeletedPhoneAsync),
        nameof(IUserReader.FindByEmailAsync),
        nameof(IUserReader.IsDeletedEmailAsync),
    ];

    private object _inner = null!;
    private object _hidden = null!;
    private StaleReadsProbe _probe = null!;

    /// <summary>
    /// Cambia el UserReader y el IdentityService de la Api por los reales con la lectura vieja de
    /// <paramref name="hidden"/>, un <see cref="PhoneNumber"/> o un <see cref="Email"/>. Cada búsqueda escondida se
    /// cuenta en <paramref name="probe"/>, que el test crea fuera de <c>ConfigureTestServices</c> para leerla después.
    /// </summary>
    public static void Replace(IServiceCollection services, object hidden, StaleReadsProbe probe)
    {
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(probe);

        services.RemoveAll<IUserReader>();
        services.AddScoped(serviceProvider =>
            Wrap<IUserReader>(ActivatorUtilities.CreateInstance<UserReader>(serviceProvider), hidden, probe));

        // El IdentityService real recibe el lector viejo de arriba: las dos puertas dicen lo mismo.
        services.RemoveAll<IIdentityService>();
        services.AddScoped(serviceProvider =>
            Wrap<IIdentityService>(ActivatorUtilities.CreateInstance<IdentityService>(serviceProvider), hidden, probe));
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (args is [{ } first, ..] && first.Equals(_hidden) && HiddenLookups.Contains(targetMethod.Name, StringComparer.Ordinal))
        {
            _probe.Hit();

            return targetMethod.ReturnType == typeof(Task<bool>) ? Task.FromResult(false) : Task.FromResult<UserAccount?>(null);
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(exception.InnerException);

            throw;
        }
    }

    private static TService Wrap<TService>(TService inner, object hidden, StaleReadsProbe probe)
        where TService : class
    {
        var proxy = Create<TService, StaleIdentityReads>();
        var reads = (StaleIdentityReads)(object)proxy;
        reads._inner = inner;
        reads._hidden = hidden;
        reads._probe = probe;

        return proxy;
    }
}
