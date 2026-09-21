using System.Globalization;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Application.UnitTests.Resources;

/// <summary>Cada permiso del catálogo y cada área tienen nombre en español y en inglés.</summary>
public sealed class PermissionTextsTests
{
    public static TheoryData<string> Keys()
    {
        var data = new TheoryData<string>();

        foreach (var key in Permissions.All
            .Select(permission => "Area." + permission[..permission.IndexOf('.', StringComparison.Ordinal)])
            .Concat(Permissions.All.Select(permission => "Permission." + permission))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            data.Add(key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void Permission_key_has_spanish_and_english_texts(string key)
    {
        Assert.False(string.IsNullOrWhiteSpace(Text(CultureInfo.InvariantCulture, key)), $"Missing Spanish text for {key}");
        Assert.False(string.IsNullOrWhiteSpace(Text(CultureInfo.GetCultureInfo("en"), key)), $"Missing English text for {key}");
    }

    private static string? Text(CultureInfo culture, string key) =>
        PermissionTexts.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!.GetString(key);
}
