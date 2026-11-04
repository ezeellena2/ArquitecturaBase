using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// The block of rows through the real WhatsApp channel (RG3-A, RG8-A): forty questions and a block are checked, accepted
// and published, and the sealed projection keeps the block's rows, the mark of each question of a row and a screen per row.
public sealed partial class WhatsAppFormSendingTests
{
    [Fact]
    public async Task A_block_with_forty_questions_publishes_a_screen_per_row_and_seals_its_rows()
    {
        Assert.SkipUnless(factory.Services.GetService<IStructuredFormPublicationDriver>() is not null,
            "The optional structured form publisher must be installed for this composition test.");
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var host = FormsHost(new CapturingFlowProvider(clock), new CapturingWhatsAppCloudClient(), null, clock);
        using var author = await OperatorAsync(host, "formtemplates.read", "formtemplates.manage", "formtemplates.publish");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fields = new List<object>
        {
            new { localId = "supplier", type = "shortText", label = new { es = "Proveedor", en = "Supplier" }, maxLength = 40 },
            new { localId = "items", type = "group", label = new { es = "Artículos", en = "Items" }, required = false, rows = new { minRows = 0, maxRows = 20 } },
            new { localId = "quantity", type = "decimal", label = new { es = "Cantidad", en = "Quantity" }, repeated = true },
            new { localId = "unit", type = "choice", label = new { es = "Unidad", en = "Unit" }, repeated = true,
                options = new[] { new { localId = "liter", label = new { es = "Litros", en = "Liters" } } } },
        };
        fields.AddRange(Enumerable.Range(1, 37).Select(index => (object)new
        {
            localId = "datum-" + index, type = "shortText", label = new { es = "Dato " + index, en = "Datum " + index }, maxLength = 20,
        }));
        using var created = await author.PostAsJsonAsync("/api/forms/definitions", new
        {
            requestId = Guid.NewGuid(), name = new { es = "Recepción", en = "Receipt" }, prefix = "RG" + suffix.ToUpperInvariant(),
            draft = new { format = 1, culture = "es", submitLabel = new { es = "Enviar", en = "Send" }, fields },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var definition = (await created.ReadJsonAsync()).GetProperty("definition");
        var definitionId = definition.GetProperty("id").GetGuid();
        var checkBody = JsonNode.Parse(definition.GetRawText())!.AsObject();
        foreach (var key in new[] { "id", "prefixLocked", "revision", "offerWhen" }) checkBody.Remove(key);
        checkBody["expectedRevision"] = definition.GetProperty("revision").GetInt64();
        var checkFields = checkBody["draft"]!["fields"]!.AsArray();
        for (var index = 0; index < checkFields.Count; index++)
        {
            checkFields[index]!["localId"] = "field-" + index;
            checkFields[index]!.AsObject().Remove("key");
            if (checkFields[index]!["options"] is JsonArray options)
                for (var option = 0; option < options.Count; option++) options[option]!["localId"] = "option-" + option;
        }

        using var check = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publication-checks", checkBody, Ct);
        Assert.True(check.StatusCode == HttpStatusCode.OK, await check.Content.ReadAsStringAsync(Ct));
        var checkedBody = await check.ReadJsonAsync();
        Assert.True(checkedBody.GetProperty("canAccept").GetBoolean(), checkedBody.GetRawText());
        Assert.Contains(checkedBody.GetProperty("warnings").EnumerateArray(),
            warning => warning.GetProperty("code").GetString() == "Forms.Publication.RowsOverChannelLimit");
        checkBody["requestId"] = Guid.NewGuid().ToString();
        checkBody["checkToken"] = checkedBody.GetProperty("checkToken").GetString();
        using var accepted = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", checkBody, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        var versionId = receipt.GetProperty("versionId").GetGuid();
        var operationId = receipt.GetProperty("operationId").GetGuid();
        var driver = host.Services.GetRequiredService<IStructuredFormPublicationDriver>();
        var publisher = host.Services.GetRequiredService<WhatsAppFlowBackgroundService>();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await driver.ProcessAsync(operationId, Ct);
            await publisher.ProcessNextAsync(operationId, Ct);
            clock.Advance(TimeSpan.FromSeconds(16));
        }

        using var status = await author.GetAsync(receipt.GetProperty("recoveryPath").GetString(), Ct);
        Assert.Equal("Published", (await status.ReadJsonAsync()).GetProperty("state").GetString());
        var bindings = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>()
            .Where(binding => binding.VersionId == versionId).ToArrayAsync(Ct));
        Assert.Equal(2, bindings.Length);
        await using var scope = host.Services.CreateAsyncScope();
        var protector = scope.ServiceProvider.GetRequiredService<IWhatsAppFlowProtector>();
        foreach (var binding in bindings)
        {
            var projection = protector.ReadProjection(binding.OperationId, binding.Id, binding.VersionId, binding.Culture, binding.ProtectedProjection);
            Assert.Equal("flow-static-7.3-r10", projection.ProfileRevision);
            Assert.Equal(41, projection.Mapping.Count);
            Assert.Equal(new StructuredFormRowSettings(0, 20), projection.Mapping[1].Rows);
            Assert.Equal(new bool?[] { null, null, true, true, null }, projection.Mapping.Take(5).Select(field => field.Repeated));
            using var json = JsonDocument.Parse(projection.Json);
            // The supplier, «¿Agregás un renglón?», ten rows and the rest of the questions.
            Assert.Equal(13, json.RootElement.GetProperty("screens").GetArrayLength());
        }
    }
}
