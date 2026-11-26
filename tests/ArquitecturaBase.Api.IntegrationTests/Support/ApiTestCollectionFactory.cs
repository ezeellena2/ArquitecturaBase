using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Xunit.Sdk;
using Xunit.v3;

[assembly: CollectionBehavior(typeof(ApiTestCollectionFactory))]
[assembly: TestCollectionOrderer(typeof(ApiTestCollectionOrderer))]

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// Reparte las clases de <see cref="ApiTestGroup"/> en <see cref="Count"/> colecciones. Cada una tiene su propio
/// <see cref="ApiFactory"/>, con su Postgres, su Redis, su reloj y sus correos: adentro los tests siguen en serie, como con
/// una sola colección, y entre colecciones no comparten datos. Corren a la vez tantas como hilos tenga xUnit. El reparto es
/// fijo para un mismo código: de la clase más pesada a la más liviana, cada una va a la colección con menos casos, contando
/// uno por [Fact] y uno por fila de [InlineData]. Las demás clases quedan como siempre, una colección por clase. Cada
/// colección pesa lo que tardaron sus clases en la última medición (<c>api-test-durations.json</c>), para que
/// <see cref="ApiTestCollectionOrderer"/> largue primero las más lentas.
/// </summary>
public sealed class ApiTestCollectionFactory(IXunitTestAssembly testAssembly) : IXunitTestCollectionFactory
{
    /// <summary>Más colecciones que hilos, para que el final se reparta; cada una paga el arranque de sus contenedores.</summary>
    public const int Count = 32;

    private const string ProjectNamespace = "ArquitecturaBase.Api.IntegrationTests.";

    private static readonly ConcurrentDictionary<string, double> Weights = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Type, byte> WeighedClasses = new();

    private readonly CollectionPerClassTestCollectionFactory _default = new(testAssembly);
    private readonly Lazy<ApiCollectionPlan> _plan = new(() => ApiCollectionPlan.For(ApiClasses(testAssembly.Assembly), Count));
    private readonly ConcurrentDictionary<int, IXunitTestCollection> _apiCollections = new();

    public string DisplayName => $"collection-per-class, with {ApiTestGroup.Name} split in {Count}";

    /// <summary>Los segundos de cada clase en la última medición, por su nombre sin el espacio del proyecto.</summary>
    internal static IReadOnlyDictionary<string, double> Measured { get; } = LoadMeasured();

    public IXunitTestCollection Get(Type testClass)
    {
        var collection = _default.Get(testClass);

        if (collection.CollectionDefinition == typeof(ApiTestGroup) && _plan.Value.Collections.TryGetValue(NameOf(testClass), out var index))
        {
            collection = _apiCollections.GetOrAdd(index, number => new XunitTestCollection(
                testAssembly, typeof(ApiTestGroup), disableParallelization: false, $"{ApiTestGroup.Name} {number + 1:00}"));
            Weights[collection.UniqueID] = _plan.Value.Seconds[index];
        }
        else if (WeighedClasses.TryAdd(testClass, 0))
        {
            var seconds = _plan.Value.SecondsOf(ClassOf(testClass));
            Weights.AddOrUpdate(collection.UniqueID, _ => seconds, (_, total) => total + seconds);
        }

        return collection;
    }

    /// <summary>Los segundos que se esperan de la colección; 0 si no la armó esta fábrica.</summary>
    internal static double SecondsOf(ITestCollection collection) => Weights.GetValueOrDefault(collection.UniqueID);

    /// <summary>El nombre de la clase en la medición: el completo sin el espacio del proyecto.</summary>
    internal static string NameOf(Type testClass) => testClass.FullName!.StartsWith(ProjectNamespace, StringComparison.Ordinal)
        ? testClass.FullName[ProjectNamespace.Length..]
        : testClass.FullName;

    private static List<ApiTestClass> ApiClasses(Assembly assembly) => [.. assembly.GetTypes()
        .Where(type => type.GetCustomAttributes<CollectionAttribute>(inherit: false).Any(attribute => attribute.Name == ApiTestGroup.Name))
        .Select(ClassOf)];

    private static ApiTestClass ClassOf(Type testClass) =>
        new(NameOf(testClass), CountCases(testClass), Measured.TryGetValue(NameOf(testClass), out var seconds) ? seconds : null);

    private static int CountCases(Type testClass) => testClass
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
        .Where(method => method.IsDefined(typeof(FactAttribute), inherit: true))
        .Sum(method => Math.Max(1, method.GetCustomAttributes<InlineDataAttribute>(inherit: true).Count()));

    private static Dictionary<string, double> LoadMeasured()
    {
        using var stream = typeof(ApiTestCollectionFactory).Assembly.GetManifestResourceStream("ApiTestDurations.json");
        return stream is null ? [] : JsonSerializer.Deserialize<Dictionary<string, double>>(stream) ?? [];
    }

    /// <summary>Una clase de tests: sus casos y, si figura en la última medición, los segundos que tardó.</summary>
    internal sealed record ApiTestClass(string Name, int Cases, double? Measured);

    /// <summary>
    /// El reparto por casos y lo que se espera que tarde cada colección: lo medido de sus clases y, para la que no figura en
    /// la medición, sus casos al promedio por caso de las medidas (sin ninguna medida, un segundo por caso).
    /// </summary>
    internal sealed record ApiCollectionPlan(IReadOnlyDictionary<string, int> Collections, double[] Seconds, double SecondsPerCase)
    {
        public double SecondsOf(ApiTestClass testClass) => Estimate(testClass, SecondsPerCase);

        public static ApiCollectionPlan For(IReadOnlyCollection<ApiTestClass> classes, int count)
        {
            var measured = classes.Where(item => item.Measured is not null).ToList();
            var measuredCases = measured.Sum(item => item.Cases);
            var secondsPerCase = measuredCases == 0 ? 1 : measured.Sum(item => item.Measured!.Value) / measuredCases;
            var cases = new int[count];
            var seconds = new double[count];
            var collections = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var item in classes.OrderByDescending(item => item.Cases).ThenBy(item => item.Name, StringComparer.Ordinal))
            {
                var lightest = Array.IndexOf(cases, cases.Min());
                cases[lightest] += item.Cases;
                seconds[lightest] += Estimate(item, secondsPerCase);
                collections[item.Name] = lightest;
            }

            return new(collections, seconds, secondsPerCase);
        }

        private static double Estimate(ApiTestClass testClass, double secondsPerCase) =>
            testClass.Measured ?? testClass.Cases * secondsPerCase;
    }
}
