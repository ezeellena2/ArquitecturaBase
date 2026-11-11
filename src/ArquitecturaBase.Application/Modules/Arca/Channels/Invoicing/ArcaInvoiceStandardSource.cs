using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;

/// <summary>
/// The «Factura» of the forms library (task 11 of stage 3, BI1-A): the customer from «Clientes» (a final consumer, a customer of
/// the catalog, or one whose CUIT is written in the invoice), the concept from the ARCA table, the date (today when empty), the
/// period and the payment due date of a service, the items with their VAT rate and the total. Every question the issuer reads is
/// bound by its datum key; the copy and the «Clientes» catalog it brings go to the «ARCA» folder. «Precio unitario» has the toggle
/// «El precio incluye IVA» (IV2-A), unchecked, that the issuer reads as the datum pricesIncludeVat, and «Alícuota» starts at 21 %
/// in each new item (IV1-A). «Mandar copia al cliente» asks for the copy by email of the fiscal PDF, which goes to the email of
/// the customer of «Clientes» (without one, it does not apply). A copy starts with the rule «Cuando llega»: issue the invoice, tell
/// whoever loaded it with the fiscal PDF and send the copy; a form that asks for approval moves it to «Cuando se aprueba» by hand.
/// </summary>
internal sealed class ArcaInvoiceStandardSource : IStructuredFormStandardSource
{
    private const string Customer = "0196f2b0-0000-7000-8000-000000000001", CustomerTaxId = "0196f2b0-0000-7000-8000-000000000002",
        Concept = "0196f2b0-0000-7000-8000-000000000003", IssueDate = "0196f2b0-0000-7000-8000-000000000004",
        ServiceFrom = "0196f2b0-0000-7000-8000-000000000005", ServiceTo = "0196f2b0-0000-7000-8000-000000000006",
        PaymentDue = "0196f2b0-0000-7000-8000-000000000007", Items = "0196f2b0-0000-7000-8000-000000000008",
        Description = "0196f2b0-0000-7000-8000-000000000009", Quantity = "0196f2b0-0000-7000-8000-00000000000a",
        UnitPrice = "0196f2b0-0000-7000-8000-00000000000b", VatRate = "0196f2b0-0000-7000-8000-00000000000c",
        Amount = "0196f2b0-0000-7000-8000-00000000000d", Total = "0196f2b0-0000-7000-8000-00000000000e",
        SendCopy = "0196f2b0-0000-7000-8000-00000000000f";

    // IV1-A: «Alícuota» starts at 21 %, the ARCA code 5 of «Alícuotas de IVA».
    private const string GeneralVatRate = "5";

    // The period and the payment due date go always optional: WhatsApp cannot show a question by several options of a catalog, and
    // the queue already requires them with services («2») and products and services («3»).
    private static readonly string Draft = $$$"""
        {"format":1,"culture":"es","submitLabel":{"es":"Enviar","en":"Send"},"fields":[
        {"localId":"{{{Customer}}}","type":"choice","label":{"es":"Cliente","en":"Customer"},"required":true},
        {"localId":"{{{CustomerTaxId}}}","type":"shortText","label":{"es":"CUIT del cliente","en":"Customer CUIT"},"required":false,"maxLength":13,
         "showWhen":{"questionId":"{{{Customer}}}","values":["code:{{{ArcaInvoiceStandard.OtherCustomer}}}"]}},
        {"localId":"{{{Concept}}}","type":"choice","label":{"es":"Concepto","en":"Concept"},"required":true},
        {"localId":"{{{IssueDate}}}","type":"date","label":{"es":"Fecha","en":"Date"},"required":false},
        {"localId":"{{{ServiceFrom}}}","type":"date","label":{"es":"Período desde","en":"Service from"},"required":false},
        {"localId":"{{{ServiceTo}}}","type":"date","label":{"es":"Período hasta","en":"Service to"},"required":false},
        {"localId":"{{{PaymentDue}}}","type":"date","label":{"es":"Vencimiento del pago","en":"Payment due"},"required":false},
        {"localId":"{{{Items}}}","type":"group","label":{"es":"Ítems","en":"Items"},"required":true,"rows":{"minRows":1,"maxRows":50}},
        {"localId":"{{{Description}}}","type":"shortText","label":{"es":"Descripción","en":"Description"},"required":true,"maxLength":200,"repeated":true},
        {"localId":"{{{Quantity}}}","type":"decimal","label":{"es":"Cantidad","en":"Quantity"},"required":true,"repeated":true},
        {"localId":"{{{UnitPrice}}}","type":"decimal","label":{"es":"Precio unitario","en":"Unit price"},"required":true,"repeated":true,
         "toggles":[{"key":"{{{ArcaInvoiceStandard.PricesIncludeVat}}}","label":{"es":"El precio incluye IVA","en":"Prices include VAT"},
          "help":{"es":"Marcala si cargás precios finales, como en un mostrador: la factura separa el IVA de cada alícuota.",
           "en":"Check it if you enter final prices, as at a counter: the invoice separates the VAT of each rate."},"value":false,
          "whenOff":{"es":"Sin IVA: la factura le suma el de su alícuota.","en":"Without VAT: the invoice adds the VAT of its rate."},
          "whenOn":{"es":"Con IVA: la factura separa el de su alícuota.","en":"With VAT: the invoice separates the VAT of its rate."}}]},
        {"localId":"{{{VatRate}}}","type":"choice","label":{"es":"Alícuota","en":"VAT rate"},"required":false,"repeated":true},
        {"localId":"{{{Amount}}}","type":"decimal","label":{"es":"Importe","en":"Amount"},"required":false,"repeated":true,
         "calculation":{"operation":"multiply","operands":["{{{Quantity}}}","{{{UnitPrice}}}"]}},
        {"localId":"{{{Total}}}","type":"decimal","label":{"es":"Total","en":"Total"},"required":false,
         "calculation":{"operation":"sum","operands":["{{{Amount}}}"]}},
        {"localId":"{{{SendCopy}}}","type":"boolean","label":{"es":"Mandar copia al cliente","en":"Send a copy to the customer"},"required":false}]}
        """;

    private static readonly Dictionary<string, string> Arca = new() { ["es"] = "ARCA", ["en"] = "ARCA" };

    public IReadOnlyList<StructuredFormStandard> Standards { get; } =
    [
        new(ArcaInvoiceStandard.Key, 1, new Dictionary<string, string> { ["es"] = "Factura", ["en"] = "Invoice" },
            new Dictionary<string, string>
            {
                ["es"] = "Emití la factura en ARCA al recibir el formulario: el cliente, el concepto, los ítems y el total.",
                ["en"] = "Issue the invoice in ARCA when the form is received: the customer, the concept, the items and the total.",
            },
            "FAC", Draft, new Dictionary<string, string>
            {
                [Customer] = ArcaInvoiceStandard.Customer, [CustomerTaxId] = ArcaInvoiceStandard.CustomerTaxId, [Concept] = ArcaInvoiceStandard.Concept,
                [IssueDate] = ArcaInvoiceStandard.IssueDate, [ServiceFrom] = ArcaInvoiceStandard.ServiceFrom, [ServiceTo] = ArcaInvoiceStandard.ServiceTo,
                [PaymentDue] = ArcaInvoiceStandard.PaymentDue, [Items] = ArcaInvoiceStandard.Items, [Description] = ArcaInvoiceStandard.Description,
                [Quantity] = ArcaInvoiceStandard.Quantity, [UnitPrice] = ArcaInvoiceStandard.UnitPrice, [VatRate] = ArcaInvoiceStandard.VatRate,
                [Total] = ArcaInvoiceStandard.Total, [SendCopy] = ArcaInvoiceStandard.SendCopy,
            },
            new Dictionary<string, string> { [Customer] = ArcaCustomersCatalog.Key, [Concept] = "arca.concepts", [VatRate] = "arca.vat-rates" },
            new StructuredFormStandardFolder("arca", Arca),
            [
                new(ArcaCustomersCatalog.Key, "es", new Dictionary<string, string> { ["es"] = "Clientes", ["en"] = "Customers" },
                    new Dictionary<string, string>
                    {
                        ["es"] = "Los clientes de la factura. «Completar con ARCA» trae los datos desde la CUIT.",
                        ["en"] = "The customers of the invoice. “Complete with ARCA” brings their data from the CUIT.",
                    },
                    [
                        new(ArcaCustomersCatalog.TaxId, new Dictionary<string, string> { ["es"] = "CUIT", ["en"] = "CUIT" }, "text", MaxLength: 13),
                        // The text of an option admits 30 characters: the whole name goes here, as long as a catalog text field admits.
                        new(ArcaCustomersCatalog.LegalName, new Dictionary<string, string> { ["es"] = "Razón social", ["en"] = "Legal name" }, "text", MaxLength: 200),
                        new(ArcaCustomersCatalog.VatCondition, new Dictionary<string, string> { ["es"] = "Condición frente al IVA", ["en"] = "VAT condition" },
                            "catalog", "arca.vat-conditions"),
                        new(ArcaCustomersCatalog.Address, new Dictionary<string, string> { ["es"] = "Domicilio", ["en"] = "Address" }, "text", MaxLength: 200),
                        new(ArcaCustomersCatalog.Email, new Dictionary<string, string> { ["es"] = "Correo", ["en"] = "Email" }, "text", MaxLength: 254),
                    ],
                    [
                        new(ArcaInvoiceStandard.FinalConsumer, new Dictionary<string, string> { ["es"] = "Consumidor final", ["en"] = "Final consumer" }),
                        new(ArcaInvoiceStandard.OtherCustomer, new Dictionary<string, string> { ["es"] = "Otro: escribir la CUIT", ["en"] = "Other: write the CUIT" }),
                    ]),
            ],
            Arca, new Dictionary<string, string> { [VatRate] = GeneralVatRate }, Rules),
    ];

    // The voucher's name is a datum in Spanish, as the fiscal document: the English texts name the invoice by its number.
    private static readonly StructuredFormStandardRule[] Rules =
    [
        new("Received", [],
        [
            new("IssueDocument", IssuerId: ArcaInvoiceStandard.Key),
            new("NotifyAuthor", new Dictionary<string, string>
            {
                ["es"] = "Listo: emitimos la {result:arca.invoice.voucher} {result:arca.invoice.number}.",
                ["en"] = "Done: we issued invoice {result:arca.invoice.number}.",
            }, IncludePdf: true),
            new("SendCopy", new Dictionary<string, string>
            {
                ["es"] = "Te mandamos la {result:arca.invoice.voucher} {result:arca.invoice.number} en el PDF adjunto.",
                ["en"] = "Here is invoice {result:arca.invoice.number}, in the attached PDF.",
            }),
        ]),
    ];
}
