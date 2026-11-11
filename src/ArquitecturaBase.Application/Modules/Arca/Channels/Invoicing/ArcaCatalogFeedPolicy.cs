using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;

/// <summary>
/// The tables of WSFEv1 the invoice uses, as catalogs of the forms in the «ARCA» folder. Each item takes the ARCA code as its code
/// and, where a form needs a number, as its value; a VAT rate carries its percentage, and «Exento» and «No gravado», which are
/// not rates of ARCA, come as two more items. Only the rows valid today come: what stops coming is deactivated in forms.
/// </summary>
internal static class ArcaCatalogFeedPolicy
{
    public const string Exempt = "exempt", NotTaxed = "not-taxed";
    private static readonly string[] Cultures = ["es", "en"];
    private static readonly ResourceManager Texts = new("ArquitecturaBase.Application.Modules.Arca.Resources.Invoicing", typeof(ArcaCatalogFeedPolicy).Assembly);
    private static readonly StructuredFormStandardFolder Folder = new("arca", new Dictionary<string, string> { ["es"] = "ARCA", ["en"] = "ARCA" });

    private static readonly (string Key, ArcaParameterTable Table, string ValueType)[] Tables =
    [
        ("arca.vat-conditions", ArcaParameterTable.ReceiverVatConditions, "Integer"),
        ("arca.document-types", ArcaParameterTable.DocumentTypes, "Integer"),
        ("arca.concepts", ArcaParameterTable.Concepts, "Integer"),
        ("arca.currencies", ArcaParameterTable.Currencies, "Text"),
        ("arca.vat-rates", ArcaParameterTable.VatRates, "Decimal"),
    ];

    public static IReadOnlyList<StructuredFeedCatalog> Catalogs { get; } = Tables.Select(table => new StructuredFeedCatalog(table.Key,
        Localized($"Catalog.{table.Key}.Name"), Localized("Catalog.Description"), table.ValueType, Folder)).ToArray();

    public static ArcaParameterTable? Table(string catalogKey) =>
        Tables.Where(table => table.Key == catalogKey).Select(table => (ArcaParameterTable?)table.Table).SingleOrDefault();

    public static IReadOnlyList<StructuredFeedItem> Items(string catalogKey, IReadOnlyList<ArcaParameter> rows, DateOnly today)
    {
        var table = Tables.Single(value => value.Key == catalogKey);
        var items = rows.Where(row => (row.ValidFrom is null || row.ValidFrom <= today) && (row.ValidTo is null || row.ValidTo >= today))
            .DistinctBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
            .Select(row => new StructuredFeedItem(row.Id.Trim(), new Dictionary<string, string> { ["es"] = row.Description.Trim() }, table.ValueType switch
            {
                "Integer" => row.Id.Trim(),
                "Decimal" => Percentage(row.Description),
                _ => null,
            })).ToList();
        if (table.Table == ArcaParameterTable.VatRates)
        {
            items.Add(new StructuredFeedItem(Exempt, Localized("Catalog.Exempt"), "0"));
            items.Add(new StructuredFeedItem(NotTaxed, Localized("Catalog.NotTaxed"), "0"));
        }
        return items;
    }

    // «21%» or «10.5%»: the percentage in canonical form, as the forms catalogs keep a decimal.
    private static string Percentage(string description) =>
        decimal.Parse(description.Replace("%", "", StringComparison.Ordinal).Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
            .ToString("0.############################", CultureInfo.InvariantCulture);

    private static Dictionary<string, string> Localized(string key) => Cultures.ToDictionary(culture => culture, culture =>
        Texts.GetString(key, CultureInfo.GetCultureInfo(culture)) ?? throw new InvalidOperationException("The ARCA catalog text is missing."), StringComparer.Ordinal);
}
