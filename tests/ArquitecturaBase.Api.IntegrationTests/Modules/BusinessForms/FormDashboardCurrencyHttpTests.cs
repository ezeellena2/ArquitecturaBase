using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// DOC1 with DOC7-A on PostgreSQL: a copy of «Comprobante de transferencia» adds up its amount by the currency the standard's reading
// declares, through the copy's sealed bound map («Total de Monto (pesos)»); the same questions in an own form add up whole.
[Collection(ApiTestGroup.Name)]
public sealed class FormDashboardCurrencyHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_copy_of_the_transfer_receipt_adds_up_its_amount_by_the_currency_its_reading_declares()
    {
        var reader = await FormDashboardScenario.PersonAsync(factory, "Lectura Comprobantes", [BusinessFormsPermissions.RecordsRead], Ct);
        var (pesos, dollars) = (FormDashboardScenario.Choice("Pesos", "Pesos"), FormDashboardScenario.Choice("Dólares", "Dollars"));
        var (copyId, copyAmount, copyCurrency) = await ReceiptsAsync(reader, pesos, dollars);
        var (ownId, _, _) = await ReceiptsAsync(reader, pesos, dollars);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var copy = await db.Set<BusinessFormDefinition>().SingleAsync(definition => definition.Id == copyId, Ct);
            copy.RecordStandard("forms.transfer-receipt", 1,
                JsonSerializer.Serialize(new Dictionary<string, Guid> { ["amount"] = copyAmount.FieldId, ["currency"] = copyCurrency.FieldId }));
            await db.SaveChangesAsync(Ct);
        });
        using var client = FormDashboardScenario.Client(factory, reader);

        var copyNumbers = await NumbersAsync(client, copyId);
        var ownNumbers = await NumbersAsync(client, ownId);

        // The most answered currency first and the answers without one last; each one compares with nothing in September.
        Assert.Equal([("Monto", pesos.ChoiceId, "Pesos", 2, 150m), ("Monto", dollars.ChoiceId, "Dólares", 1, 20m), ("Monto", null, null, 1, 5m)], copyNumbers);
        Assert.Equal([("Monto", null, null, 4, 175m)], ownNumbers);
    }

    // A form with an amount and its currency, published, with four responses in October (Buenos Aires): 100 and 50 pesos, 20 dollars
    // and 5 without a currency.
    private async Task<(Guid Id, CanonicalFormField Amount, CanonicalFormField Currency)> ReceiptsAsync(Guid publisher, CanonicalFormChoice pesos,
        CanonicalFormChoice dollars)
    {
        var amount = FormDashboardScenario.Field("decimal", "Monto", "Amount");
        var currency = FormDashboardScenario.Field("choice", "Moneda", "Currency", [pesos, dollars]);
        var definitionId = await FormDashboardScenario.FormAsync(factory, publisher, [amount, currency], Ct);
        FormDashboardRecord Record(string atUtc, decimal total, CanonicalFormChoice? money) =>
            new(publisher, DateTime.Parse(atUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                money is { } choice ? [new(amount, Number: total), new(currency, Choice: choice.ChoiceId)] : [new(amount, Number: total)]);
        await FormDashboardScenario.AddAsync(factory, definitionId,
        [
            Record("2026-10-02T15:00:00Z", 100m, pesos),
            Record("2026-10-09T15:00:00Z", 50m, pesos),
            Record("2026-10-16T15:00:00Z", 20m, dollars),
            Record("2026-10-23T15:00:00Z", 5m, null),
        ], Ct);
        return (definitionId, amount, currency);
    }

    private static async Task<List<(string? Label, Guid? CurrencyId, string? CurrencyLabel, int Count, decimal Total)>> NumbersAsync(HttpClient client, Guid definitionId)
    {
        using var response = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/dashboard/search",
            new { filter = new { receivedFromUtc = "2026-10-01T03:00:00Z", receivedToUtc = "2026-11-01T03:00:00Z" }, period = "month" }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.ReadJsonAsync();
        return [.. dashboard.GetProperty("numbers").EnumerateArray().Select(number => (number.GetProperty("label").GetString(),
            number.GetProperty("currencyId").ValueKind == JsonValueKind.Null ? (Guid?)null : number.GetProperty("currencyId").GetGuid(),
            number.GetProperty("currencyLabel").GetString(), number.GetProperty("count").GetInt32(), number.GetProperty("total").GetDecimal()))];
    }
}
