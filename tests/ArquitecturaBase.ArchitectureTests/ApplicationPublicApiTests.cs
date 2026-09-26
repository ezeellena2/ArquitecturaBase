using System.Linq.Expressions;
using System.Reflection;

namespace ArquitecturaBase.ArchitectureTests;

public sealed class ApplicationPublicApiTests
{
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Assembly ApplicationAssembly = Assembly.Load("ArquitecturaBase.Application");

    [Fact]
    public void Public_application_signatures_do_not_expose_queryables_or_expressions()
    {
        // IQueryable nunca sale de Infrastructure. Un contrato de Application que expusiera un IQueryable, o una Expression
        // (la otra forma de pasarle una consulta a EF), dejaría que quien lo consume arme la consulta por su cuenta.
        var offending = ApplicationAssembly.GetExportedTypes()
            .SelectMany(DescribeVisibleSignatures)
            .Where(signature => ExposesQueryOrExpression(signature.Type))
            .Select(signature => signature.Description);

        Assert.Empty(offending);
    }

    // La regla de arriba hoy pasa porque nada expone esos tipos; esto comprueba que la detección de verdad los encuentra
    // cuando vienen envueltos en un Task, un delegado o un array.
    [Theory]
    [InlineData(typeof(IQueryable), true)]
    [InlineData(typeof(IQueryable<string>), true)]
    [InlineData(typeof(IOrderedQueryable<string>), true)]
    [InlineData(typeof(Expression<Func<string, bool>>), true)]
    [InlineData(typeof(Expression<Func<string, bool>>[]), true)]
    [InlineData(typeof(Task<IReadOnlyList<IQueryable<string>>>), true)]
    [InlineData(typeof(Func<IQueryable<string>, IQueryable<string>>), true)]
    [InlineData(typeof(IEnumerable<string>), false)]
    [InlineData(typeof(Task<IReadOnlyList<string>>), false)]
    [InlineData(typeof(Func<string, bool>), false)]
    public void Detection_finds_queryables_and_expressions_nested_in_a_type(Type type, bool expected) =>
        Assert.Equal(expected, ExposesQueryOrExpression(type));

    private static IEnumerable<(Type Type, string Description)> DescribeVisibleSignatures(Type type)
    {
        if (type.BaseType is { } baseType)
        {
            yield return (baseType, $"{type.FullName} (base type)");
        }

        foreach (var contract in type.GetInterfaces())
        {
            yield return (contract, $"{type.FullName} (implements {contract.Name})");
        }

        foreach (var constructor in type.GetConstructors(DeclaredMembers).Where(IsVisible))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return (parameter.ParameterType, $"{type.FullName}..ctor({parameter.Name})");
            }
        }

        // Incluye los get_ y set_ de las propiedades y los Invoke de los delegados.
        foreach (var method in type.GetMethods(DeclaredMembers).Where(IsVisible))
        {
            yield return (method.ReturnType, $"{type.FullName}.{method.Name} (return)");

            foreach (var parameter in method.GetParameters())
            {
                yield return (parameter.ParameterType, $"{type.FullName}.{method.Name}({parameter.Name})");
            }
        }

        foreach (var property in type.GetProperties(DeclaredMembers)
            .Where(property => property.GetAccessors(nonPublic: true).Any(IsVisible)))
        {
            yield return (property.PropertyType, $"{type.FullName}.{property.Name}");
        }

        foreach (var field in type.GetFields(DeclaredMembers)
            .Where(field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly))
        {
            yield return (field.FieldType, $"{type.FullName}.{field.Name}");
        }
    }

    // Public, protected o protected internal: lo que ve alguien de afuera del ensamblado.
    private static bool IsVisible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static bool ExposesQueryOrExpression(Type type)
    {
        if (type.HasElementType)
        {
            return ExposesQueryOrExpression(type.GetElementType()!);
        }

        if (type.IsGenericParameter)
        {
            return false;
        }

        if (typeof(IQueryable).IsAssignableFrom(type) || typeof(Expression).IsAssignableFrom(type))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(ExposesQueryOrExpression);
    }
}
