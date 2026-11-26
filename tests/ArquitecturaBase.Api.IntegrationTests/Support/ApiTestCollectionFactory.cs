using System.Collections.Concurrent;
using System.Reflection;
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
/// uno por [Fact] y uno por fila de [InlineData]. Las demás clases quedan como siempre, una colección por clase.
/// </summary>
public sealed class ApiTestCollectionFactory(IXunitTestAssembly testAssembly) : IXunitTestCollectionFactory
{
    /// <summary>Más colecciones que hilos, para que el final se reparta; cada una paga el arranque de sus contenedores.</summary>
    public const int Count = 32;

    private static readonly ConcurrentDictionary<string, int> Weights = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Type, byte> WeighedClasses = new();

    private readonly CollectionPerClassTestCollectionFactory _default = new(testAssembly);
    private readonly Lazy<ApiCollectionPlan> _plan = new(() => ApiCollectionPlan.For(testAssembly.Assembly));
    private readonly ConcurrentDictionary<int, IXunitTestCollection> _apiCollections = new();

    public string DisplayName => $"collection-per-class, with {ApiTestGroup.Name} split in {Count}";

    public IXunitTestCollection Get(Type testClass)
    {
        var collection = _default.Get(testClass);

        if (collection.CollectionDefinition == typeof(ApiTestGroup) && _plan.Value.Collections.TryGetValue(testClass, out var index))
        {
            collection = _apiCollections.GetOrAdd(index, number => new XunitTestCollection(
                testAssembly, typeof(ApiTestGroup), disableParallelization: false, $"{ApiTestGroup.Name} {number + 1:00}"));
            Weights[collection.UniqueID] = _plan.Value.Cases[index];
        }
        else if (WeighedClasses.TryAdd(testClass, 0))
        {
            Weights.AddOrUpdate(collection.UniqueID, _ => CountCases(testClass), (_, cases) => cases + CountCases(testClass));
        }

        return collection;
    }

    /// <summary>Los casos que se esperan en la colección; 0 si no la armó esta fábrica.</summary>
    internal static int CasesOf(ITestCollection collection) => Weights.GetValueOrDefault(collection.UniqueID);

    private static int CountCases(Type testClass) => testClass
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
        .Where(method => method.IsDefined(typeof(FactAttribute), inherit: true))
        .Sum(method => Math.Max(1, method.GetCustomAttributes<InlineDataAttribute>(inherit: true).Count()));

    private sealed record ApiCollectionPlan(IReadOnlyDictionary<Type, int> Collections, int[] Cases)
    {
        public static ApiCollectionPlan For(Assembly assembly)
        {
            var cases = new int[Count];
            var collections = new Dictionary<Type, int>();
            var classes = assembly.GetTypes()
                .Where(type => type.GetCustomAttributes<CollectionAttribute>(inherit: false)
                    .Any(attribute => attribute.Name == ApiTestGroup.Name))
                .Select(type => (Type: type, Cases: CountCases(type)))
                .OrderByDescending(item => item.Cases)
                .ThenBy(item => item.Type.FullName, StringComparer.Ordinal);

            foreach (var (type, count) in classes)
            {
                var lightest = Array.IndexOf(cases, cases.Min());
                cases[lightest] += count;
                collections[type] = lightest;
            }

            return new(collections, cases);
        }
    }
}
