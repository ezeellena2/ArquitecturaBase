using System.Reflection;
using System.Runtime.CompilerServices;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// Ningún constructor de <c>Application.Services</c> recibe más de 8 dependencias (Etapa 3, tarea 3 del plan maestro):
/// pasarse es la señal de una fachada que hay que partir por responsabilidad. Mira toda clase no estática del espacio,
/// puntos de entrada y helpers, y cuenta el constructor entero, incluidos <c>ILogger</c>, <c>IOptions</c> y
/// <c>TimeProvider</c>.
/// </summary>
public sealed class ServiceDependencyLimitTests
{
    private const string ServicesNamespace = "ArquitecturaBase.Application.Services";
    private const int Limit = 8;

    /// <summary>
    /// Las clases que se pasan del tope, cada una con su motivo. Tienen que seguir pasándose, o sobran. Desde la tarea 8
    /// del diseño de la Etapa 3 (el bot) está vacío: una clase nueva que se pase del tope se parte, y sumarla acá pide un
    /// motivo escrito. Que esté vacío no deja pasar la regla en silencio: el escaneo tiene que ver clases y el detector
    /// tiene su caso de control.
    /// </summary>
    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal);

    private static readonly Type[] Classes =
    [
        .. Assembly.Load("ArquitecturaBase.Application").GetTypes()
            .Where(type => type.IsClass
                && !(type.IsAbstract && type.IsSealed)
                && type.ResidesIn(ServicesNamespace)
                && !IsGenerated(type)),
    ];

    [Fact]
    public void Service_constructors_take_at_most_eight_dependencies()
    {
        // Si el escaneo no encontrara clases, la regla pasaría en silencio.
        Assert.NotEmpty(Classes);

        var overLimit = Classes
            .Where(type => DependencyCount(type) > Limit)
            .ToDictionary(type => type.FullName!, DependencyCount, StringComparer.Ordinal);

        Assert.Empty(overLimit
            .Where(pair => !Exceptions.ContainsKey(pair.Key))
            .Select(pair => $"{pair.Key}: {pair.Value}"));

        // Una excepción que ya no se pasa del tope (o que ya no existe) se borra.
        string[] stale = [.. Exceptions.Keys.Where(name => !overLimit.ContainsKey(name))];
        Assert.Empty(stale);
    }

    [Fact]
    public void Detector_counts_the_whole_constructor()
    {
        // Caso de control: una clase con nueve dependencias, entre ellas un logger y la hora, se pasa del tope. Si el
        // detector dejara de contar alguna, la regla pasaría en silencio.
        Assert.Equal(9, DependencyCount(typeof(NineDependencies)));
        Assert.True(DependencyCount(typeof(NineDependencies)) > Limit);
        Assert.Equal(0, DependencyCount(typeof(NoDependencies)));
    }

    /// <summary>Las clausuras y las máquinas de estado de los async, o lo que vive adentro de ellas: no son de nadie.</summary>
    private static bool IsGenerated(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        || (type.DeclaringType is { } declaring && IsGenerated(declaring));

    /// <summary>Los parámetros del constructor más largo, público o no: el primario, en las clases con uno.</summary>
    private static int DependencyCount(Type type) =>
        type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(constructor => constructor.GetParameters().Length)
            .DefaultIfEmpty(0)
            .Max();

#pragma warning disable CA1812, CS9113 // Solo importa la firma: nadie las crea.
    private sealed class NineDependencies(
        string a,
        string b,
        string c,
        string d,
        string e,
        string f,
        string g,
        TimeProvider timeProvider,
        Microsoft.Extensions.Logging.ILogger logger);

    private sealed class NoDependencies;
#pragma warning restore CA1812, CS9113
}
