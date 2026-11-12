using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The library data formularios brings itself (DOC1), as resources embedded from <c>Resources/Standards</c>: «Comprobante de
/// transferencia» (<c>transfer-receipt.json</c>), the standards of the next stages (<c>upcoming.json</c>) and the table of entities
/// of the BCRA (<c>bank-entities.json</c>, with its date and source), whose codes and names are the options of the catalog «Bancos y
/// billeteras» the standard brings. Each is read once; a missing or malformed resource is a fault of the build, never of a request.
/// </summary>
internal static class FormStandardResources
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly Lazy<FormBankEntityTable> Table = new(() => Read<FormBankEntityTable>("bank-entities.json"));

    /// <summary>The entities a transfer names by the code in its account: the banks (CBU) and the payment service providers (CVU).</summary>
    public static FormBankEntityTable BankEntities => Table.Value;

    public static StructuredFormStandard TransferReceipt()
    {
        var file = Read<StandardFile>("transfer-receipt.json");
        // The option's text is the short name; the denomination the BCRA publishes goes as its detail.
        var items = BankEntities.Banks.Concat(BankEntities.PaymentProviders).Select(entity => new StructuredFormStandardCatalogItem(entity.Code,
            new Dictionary<string, string> { ["es"] = entity.Name }, new Dictionary<string, string> { ["es"] = entity.OfficialName })).ToArray();
        return new(file.Key, file.Version, file.Name, file.Description, file.Prefix, file.Draft.GetRawText(), file.Bindings, file.Catalogs, file.Folder,
            [new(file.Catalog.Key, file.Catalog.OriginalCulture, file.Catalog.Name, file.Catalog.Description, [], items)], Reading: file.Reading);
    }

    public static IReadOnlyList<StructuredFormUpcomingStandard> Upcoming() => Read<StructuredFormUpcomingStandard[]>("upcoming.json");

    private static T Read<T>(string name)
    {
        using var stream = typeof(FormStandardResources).Assembly.GetManifestResourceStream("BusinessForms.Standards." + name)
            ?? throw new InvalidOperationException($"The library resource {name} is missing.");
        return JsonSerializer.Deserialize<T>(stream, Options) ?? throw new InvalidOperationException($"The library resource {name} is empty.");
    }

    // The shape of transfer-receipt.json: the standard with its draft as an object and its own catalog, whose options are the table.
    private sealed class StandardFile
    {
        public required string Key { get; init; }
        public required int Version { get; init; }
        public required Dictionary<string, string> Name { get; init; }
        public required Dictionary<string, string> Description { get; init; }
        public required string Prefix { get; init; }
        public required StructuredFormStandardFolder Folder { get; init; }
        public required JsonElement Draft { get; init; }
        public required Dictionary<string, string> Bindings { get; init; }
        public required Dictionary<string, string> Catalogs { get; init; }
        public required CatalogFile Catalog { get; init; }
        public required StructuredFormStandardReading Reading { get; init; }
    }

    private sealed record CatalogFile(string Key, string OriginalCulture, Dictionary<string, string> Name, Dictionary<string, string> Description);
}

/// <summary>
/// The table of entities (DOC1): banks by the three digits a CBU starts with and payment service providers by the four after the 000
/// of a CVU, only those confirmed in an official source, with the date it was read, its sources and a note on what it leaves out.
/// </summary>
internal sealed record FormBankEntityTable(DateOnly Date, IReadOnlyList<string> Sources, string Note, IReadOnlyList<FormBankEntity> Banks,
    IReadOnlyList<FormBankEntity> PaymentProviders);

/// <summary>An entity of the table: its code, the short name an option shows and the denomination the official source publishes.</summary>
internal sealed record FormBankEntity(string Code, string Name, string OfficialName);
