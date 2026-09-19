using System.Globalization;
using System.Reflection;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Resources;

/// <summary>
/// Cada código declarado en una clase *Errors (constantes públicas terminadas en "Code") tiene su texto en español
/// y en inglés. Sin esto, el usuario vería la descripción técnica en inglés.
/// </summary>
public sealed class ErrorCodeTranslationTests
{
    public static TheoryData<string> DeclaredCodes()
    {
        var data = new TheoryData<string>();
        Assembly[] assemblies = [typeof(Error).Assembly, typeof(ErrorMessages).Assembly];

        var codes = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Name.EndsWith("Errors", StringComparison.Ordinal))
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string) && field.Name.EndsWith("Code", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);

        foreach (var code in codes)
        {
            data.Add(code);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DeclaredCodes))]
    public void Error_code_has_spanish_and_english_texts(string code)
    {
        var spanish = ErrorMessages.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!.GetString(code);
        var english = ErrorMessages.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)!.GetString(code);

        Assert.False(string.IsNullOrWhiteSpace(spanish), $"Missing Spanish text for {code}");
        Assert.False(string.IsNullOrWhiteSpace(english), $"Missing English text for {code}");
    }
}
