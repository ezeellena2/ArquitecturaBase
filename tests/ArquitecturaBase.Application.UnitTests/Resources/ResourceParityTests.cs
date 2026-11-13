using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using ArquitecturaBase.Application.Resources;

namespace ArquitecturaBase.Application.UnitTests.Resources;

/// <summary>
/// Cada texto en español tiene su traducción al inglés y viceversa, en todos los .resx de Application. La lista sale de los
/// recursos del ensamblado, no de una lista a mano: un .resx nuevo, también de un módulo, entra solo, y sin su .en.resx no pasa.
/// </summary>
public sealed partial class ResourceParityTests
{
    private const string CompiledResourceSuffix = ".resources";
    private static readonly Assembly Application = typeof(ErrorMessages).Assembly;
    private static readonly string Prefix = Application.GetName().Name + ".";

    /// <summary>Each compiled resource by its name under the assembly's, so a failure names it.</summary>
    public static TheoryData<string> ApplicationResources()
    {
        var resources = new TheoryData<string>();
        foreach (var name in CompiledBaseNames().Order(StringComparer.Ordinal)) resources.Add(name[Prefix.Length..]);

        return resources;
    }

    [Theory]
    [MemberData(nameof(ApplicationResources))]
    public void Every_resource_has_the_same_keys_in_spanish_and_english(string resource)
    {
        var baseName = Prefix + resource;
        var manager = new ResourceManager(baseName, Application);
        var spanish = Keys(manager, CultureInfo.InvariantCulture, baseName);
        var english = Keys(manager, CultureInfo.GetCultureInfo("en"), baseName);

        Assert.NotEmpty(spanish);
        var onlySpanish = spanish.Except(english, StringComparer.Ordinal).ToArray();
        var onlyEnglish = english.Except(spanish, StringComparer.Ordinal).ToArray();
        Assert.True(onlySpanish.Length == 0 && onlyEnglish.Length == 0,
            $"{resource}: only in Spanish [{string.Join(", ", onlySpanish)}]; only in English [{string.Join(", ", onlyEnglish)}].");
    }

    /// <summary>
    /// The code names each resource with a string: one that is moved or renamed, with its .en.resx, still has the same keys in both
    /// languages, but its manager would fail at run time.
    /// </summary>
    [Fact]
    public void Every_resource_manager_of_application_names_a_compiled_resource()
    {
        var compiled = CompiledBaseNames().ToHashSet(StringComparer.Ordinal);
        var managers = Application.GetTypes().Where(type => !type.ContainsGenericParameters)
            .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(field => field.FieldType == typeof(ResourceManager))
            .Select(field => (Owner: field.DeclaringType!.FullName, Manager: (ResourceManager?)field.GetValue(null)))
            .ToArray();

        Assert.NotEmpty(managers);
        Assert.Empty(managers.Where(item => item.Manager is null || !compiled.Contains(item.Manager.BaseName))
            .Select(item => $"{item.Owner}: {item.Manager?.BaseName}"));
    }

    private static IEnumerable<string> CompiledBaseNames() => Application.GetManifestResourceNames()
        .Where(name => name.EndsWith(CompiledResourceSuffix, StringComparison.Ordinal))
        .Select(name => name[..^CompiledResourceSuffix.Length]);

    private static string[] Keys(ResourceManager resourceManager, CultureInfo culture, string baseName)
    {
        var resourceSet = resourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"{baseName} has no resources for culture '{culture.Name}'.");

        return resourceSet.Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
