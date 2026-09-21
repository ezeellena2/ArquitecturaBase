using System.Collections;
using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Resources;

namespace ArquitecturaBase.Application.UnitTests.Resources;

/// <summary>Cada texto en español tiene su traducción al inglés y viceversa.</summary>
public sealed class ResourceParityTests
{
    [Fact]
    public void Errors_have_the_same_keys_in_spanish_and_english()
    {
        AssertSameKeys(ErrorMessages.ResourceManager);
    }

    [Fact]
    public void Validation_messages_have_the_same_keys_in_spanish_and_english()
    {
        AssertSameKeys(ValidationMessages.ResourceManager);
    }

    [Fact]
    public void Permission_texts_have_the_same_keys_in_spanish_and_english()
    {
        AssertSameKeys(PermissionTexts.ResourceManager);
    }

    private static void AssertSameKeys(ResourceManager resourceManager)
    {
        var spanish = Keys(resourceManager, CultureInfo.InvariantCulture);
        var english = Keys(resourceManager, CultureInfo.GetCultureInfo("en"));

        Assert.NotEmpty(spanish);
        Assert.Equal(spanish, english);
    }

    private static string[] Keys(ResourceManager resourceManager, CultureInfo culture)
    {
        var resourceSet = resourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"No resources for culture '{culture.Name}'.");

        return resourceSet.Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
