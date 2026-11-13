namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

/// <summary>The bytes each Flows profile compiled, as embedded fixtures (<c>Fixtures/flow-static-7.3-r*-*.json</c>).</summary>
internal static class FlowProfileFixtures
{
    // As the compiled JSON writes them: each accented letter escaped.
    private static readonly Dictionary<string, (string Before, string After)> DecimalHelp = new(StringComparer.Ordinal)
    {
        ["es"] = (Escaped("Ingresá un número; usá punto para los decimales."), Escaped("Ingresá el número sin separador de miles; usá coma para los decimales.")),
        ["en"] = (Escaped("Enter a number; use a dot for decimals."), Escaped("Enter the number without thousands separators; use a dot for decimals.")),
    };

    /// <summary>A fixture by its file name without the extension, for example <c>flow-static-7.3-r14-es</c>.</summary>
    public static string Read(string name)
    {
        using var stream = typeof(FlowProfileFixtures).Assembly.GetManifestResourceStream($"FlowProfile.{name}.json")
            ?? throw new InvalidOperationException($"The Flows fixture {name} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// What r15 compiles where an earlier profile compiled <paramref name="json"/>: the same bytes, except the help of a decimal
    /// without its own, which asks for no thousands separator and, in Spanish, for a comma (H18).
    /// </summary>
    public static string AsR15(string json, string culture) =>
        json.Replace(DecimalHelp[culture].Before, DecimalHelp[culture].After, StringComparison.Ordinal);

    private static string Escaped(string text) => System.Text.Json.JsonSerializer.Serialize(text)[1..^1];
}
