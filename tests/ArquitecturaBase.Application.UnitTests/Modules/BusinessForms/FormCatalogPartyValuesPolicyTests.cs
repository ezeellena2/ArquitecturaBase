using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

/// <summary>What a registry knows of a party, in the fields of a catalog: «Completar» and the party an issuer completed share it.</summary>
public sealed class FormCatalogPartyValuesPolicyTests
{
    private static readonly Guid Conditions = Guid.NewGuid();

    [Fact]
    public async Task Each_active_field_gets_its_value_by_key_and_a_catalog_field_the_active_option_of_the_code()
    {
        var codes = new Codes { Active = { [(Conditions, "1")] = Guid.Parse("0f9b5d6e-0000-0000-0000-000000000001") } };
        var cuit = Field("tax_id", "text");
        var condition = Field("vat_condition", "catalog", Conditions);
        var oldAddress = Field("address", "text") with { IsActive = false };

        var mapped = await new FormCatalogPartyValuesPolicy(codes).MapAsync([cuit, condition, oldAddress],
            new Dictionary<string, string> { ["tax_id"] = "30500010912", ["vat_condition"] = "1", ["address"] = "Ruta 9", ["unknown"] = "x" },
            CancellationToken.None);

        Assert.Equal(new[] { (cuit, "30500010912"), (condition, "0f9b5d6e-0000-0000-0000-000000000001") }, mapped);
        Assert.Equal(1, codes.Reads);
    }

    [Fact]
    public async Task A_code_the_source_lacks_and_a_party_without_values_bring_nothing()
    {
        var codes = new Codes();
        var policy = new FormCatalogPartyValuesPolicy(codes);
        var condition = Field("vat_condition", "catalog", Conditions);

        Assert.Empty(await policy.MapAsync([condition], new Dictionary<string, string> { ["vat_condition"] = "9" }, CancellationToken.None));
        Assert.Empty(await policy.MapAsync([condition], null, CancellationToken.None));
    }

    private static BusinessFormCatalogField Field(string key, string type, Guid? source = null) =>
        new(Guid.NewGuid(), key, new Dictionary<string, string> { ["es"] = key }, type, source, false, false, null, null, null, "None", 0, true);

    private sealed class Codes : IFormCatalogItemCodeReader
    {
        public Dictionary<(Guid, string), Guid> Active { get; } = [];
        public int Reads { get; private set; }
        public Task<IReadOnlyDictionary<Guid, string>> ListAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, Guid>> ListActiveAsync(Guid catalogId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<IReadOnlyDictionary<string, Guid>>(codes.Where(code => Active.ContainsKey((catalogId, code)))
                .ToDictionary(code => code, code => Active[(catalogId, code)]));
        }
    }
}
