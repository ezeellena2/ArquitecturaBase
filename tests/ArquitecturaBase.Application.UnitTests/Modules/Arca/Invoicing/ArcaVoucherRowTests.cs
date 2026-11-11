using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Invoicing;

/// <summary>
/// An issuance as it is read carries the order fixed when it was queued, with the receiver's document, name and address, and
/// ARCA's messages: printing it never shows them.
/// </summary>
public sealed class ArcaVoucherRowTests
{
    [Fact]
    public void Printing_an_issuance_does_not_show_its_order_nor_the_messages_of_arca()
    {
        const string payload = """{"receiver":{"documentType":80,"documentNumber":"30500010912","name":"Agro del Sur S.A.","address":"Ruta 5 km 3"}}""";
        var row = new ArcaVoucherRow(Guid.CreateVersion7(), Guid.CreateVersion7(), ArcaVoucherState.Queued, null, ArcaVoucherClass.A,
            ArcaVoucherKind.Invoice, 1, 4, null, null, null, null, new DateOnly(2026, 11, 11), 1210m, null, false,
            new DateTime(2026, 11, 11, 13, 0, 0, DateTimeKind.Utc), 0, null, null, null, payload, """[{"code":10015,"message":"Receptor invalido"}]""", 0);

        var printed = row.ToString();

        foreach (var hidden in new[] { "30500010912", "Agro del Sur", "Ruta 5", "Receptor invalido", "1210" })
            Assert.DoesNotContain(hidden, printed, StringComparison.Ordinal);
    }
}
