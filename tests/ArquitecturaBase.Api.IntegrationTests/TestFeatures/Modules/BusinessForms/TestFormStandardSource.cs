using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

/// <summary>
/// A standard of the library only for tests (BI1-A): a delivery note with a block of items, the amount of each item and the total,
/// where every question but «Observaciones» is bound.
/// </summary>
internal sealed class TestFormStandardSource : IStructuredFormStandardSource
{
    public const string Key = "test.delivery";
    public const string Customer = "0196f2a0-0000-7000-8000-0000000000b1", Items = "0196f2a0-0000-7000-8000-0000000000b2",
        Quantity = "0196f2a0-0000-7000-8000-0000000000b3", Price = "0196f2a0-0000-7000-8000-0000000000b4",
        Amount = "0196f2a0-0000-7000-8000-0000000000b5", Total = "0196f2a0-0000-7000-8000-0000000000b6",
        Notes = "0196f2a0-0000-7000-8000-0000000000b7";

    private static readonly string Draft = $$$"""
        {"format":1,"culture":"es","submitLabel":{"es":"Enviar","en":"Send"},"fields":[
        {"localId":"{{{Customer}}}","type":"shortText","label":{"es":"Cliente","en":"Customer"},"required":true,"maxLength":80},
        {"localId":"{{{Items}}}","type":"group","label":{"es":"Artículos","en":"Items"},"required":true,"rows":{"minRows":1,"maxRows":20}},
        {"localId":"{{{Quantity}}}","type":"decimal","label":{"es":"Cantidad","en":"Quantity"},"required":true,"repeated":true},
        {"localId":"{{{Price}}}","type":"decimal","label":{"es":"Precio unitario","en":"Unit price"},"required":true,"repeated":true},
        {"localId":"{{{Amount}}}","type":"decimal","label":{"es":"Importe","en":"Amount"},"required":false,"repeated":true,
         "calculation":{"operation":"multiply","operands":["{{{Quantity}}}","{{{Price}}}"]}},
        {"localId":"{{{Total}}}","type":"decimal","label":{"es":"Total","en":"Total"},"required":false,
         "calculation":{"operation":"sum","operands":["{{{Amount}}}"]}},
        {"localId":"{{{Notes}}}","type":"longText","label":{"es":"Observaciones","en":"Notes"},"required":false}]}
        """;

    public IReadOnlyList<StructuredFormStandard> Standards { get; } =
    [
        new(Key, 1, new Dictionary<string, string> { ["es"] = "Remito de prueba", ["en"] = "Test delivery note" },
            new Dictionary<string, string> { ["es"] = "Un remito con sus ítems y el total.", ["en"] = "A delivery note with its items and total." },
            "RTP", Draft, new Dictionary<string, string>
            {
                [Customer] = "customer", [Items] = "items", [Quantity] = "quantity", [Price] = "price", [Amount] = "amount", [Total] = "total",
            }),
        new(CustomersKey, 1, new Dictionary<string, string> { ["es"] = "Venta de prueba", ["en"] = "Test sale" },
            new Dictionary<string, string> { ["es"] = "Una venta a un cliente del catálogo que trae.", ["en"] = "A sale to a customer of the catalog it brings." },
            "VTP", $$$"""
                {"format":1,"culture":"es","submitLabel":{"es":"Enviar"},"fields":[
                {"localId":"{{{Buyer}}}","type":"choice","label":{"es":"Cliente"},"required":true}]}
                """, new Dictionary<string, string> { [Buyer] = "customer" }, new Dictionary<string, string> { [Buyer] = CustomersCatalog },
            OwnCatalogs: [new(CustomersCatalog, "es", new Dictionary<string, string> { ["es"] = "Clientes de prueba" }, new Dictionary<string, string>(),
                [new("cuit", new Dictionary<string, string> { ["es"] = "CUIT" }, "text", MaxLength: 11)])]),
        new(MissingKey, 1, new Dictionary<string, string> { ["es"] = "Sin catálogo" }, new Dictionary<string, string> { ["es"] = "Nombra un catálogo que no está." },
            "SCP", $$$"""
                {"format":1,"culture":"es","submitLabel":{"es":"Enviar"},"fields":[
                {"localId":"{{{Buyer}}}","type":"choice","label":{"es":"Cliente"},"required":true}]}
                """, new Dictionary<string, string>(), new Dictionary<string, string> { [Buyer] = "test.nowhere" }),
    ];
    public const string CustomersKey = "test.sale", MissingKey = "test.missing-catalog", CustomersCatalog = "test.customers",
        Buyer = "0196f2a0-0000-7000-8000-0000000000e1";
}
