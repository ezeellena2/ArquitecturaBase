using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFormSendingTests
{
    [Fact]
    public async Task Real_catalog_http_sending_resolves_manual_and_users_and_blocks_inactive_large_or_long_options()
    {
        Assert.SkipUnless(factory.Services.GetService<IStructuredFormPublicationDriver>() is not null,
            "The optional structured form publisher must be installed for this composition test.");
        await using var isolated = new ApiFactory();
        await isolated.InitializeAsync();
        var scenario = new WhatsAppFormSendingTests(isolated);
        foreach (var kind in new[] { "Valid", "Inactive", "TooMany", "LongText" })
            await scenario.AssertRealCatalogSendAsync(kind);
    }

    private async Task AssertRealCatalogSendAsync(string scenario)
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        var cloud = new CapturingWhatsAppCloudClient();
        await using var host = FormsHost(provider, cloud, null, clock);
        using var author = await OperatorAsync(host, "formtemplates.read", "formtemplates.manage", "formtemplates.publish",
            "formcatalogs.read", "formcatalogs.manage");
        using var sender = await OperatorAsync(host, "whatsapp.read", "whatsapp.send");
        using var employee = await OperatorAsync(host, "formrecords.create");
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var personName = (scenario == "LongText" ? new string('x', 31) : "Employee") + suffix;
        await factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IUserRepository>().SetDisplayNameAsync(employeeId, personName, Ct);
            return true;
        });
        using var manual = await author.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Manual", en = "Manual" }, description = new { es = "Lista manual", en = "Manual list" },
            items = Enumerable.Range(0, scenario == "TooMany" ? 201 : 1).Select(index => new
            {
                localId = "item-" + index, code = "CODE-" + index, text = new { es = "Offered item " + index },
                detail = new { es = "Full detail" }, isActive = true,
            }).ToArray(),
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, manual.StatusCode);
        var manualId = (await manual.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();
        using var filtered = await author.PostAsJsonAsync("/api/forms/catalogs/filters", new
        {
            name = new { es = "Personas", en = "People" }, description = new { es = "Lista de personas", en = "People list" },
            filters = new { textColumn = "name", detailColumn = "active",
                conditions = new[] { new { column = "name", @operator = "contains", value = suffix } },
                excludeRecipient = false, sortColumn = "name", sortDescending = false },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, filtered.StatusCode);
        var filteredId = (await filtered.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();
        using var created = await author.PostAsJsonAsync("/api/forms/definitions", new
        {
            requestId = Guid.NewGuid(), name = new { es = "Catálogos reales", en = "Real catalogs" },
            prefix = "C" + suffix.ToUpperInvariant(), draft = new
            {
                format = 1, culture = "es", submitLabel = new { es = "Enviar", en = "Submit" },
                fields = new[] { manualId, filteredId }.Select((id, index) => new
                {
                    localId = "question-" + index, type = "choice", label = new { es = "Lista " + index, en = "List " + index },
                    required = true, source = new { catalog = id },
                }).ToArray(),
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.ReadJsonAsync();
        var definition = saved.GetProperty("definition");
        var definitionId = definition.GetProperty("id").GetGuid();
        var checkBody = JsonNode.Parse(definition.GetRawText())!.AsObject();
        checkBody.Remove("id"); checkBody.Remove("prefixLocked"); checkBody.Remove("revision");
        checkBody["expectedRevision"] = definition.GetProperty("revision").GetInt64();
        var fields = checkBody["draft"]!["fields"]!.AsArray();
        for (var index = 0; index < fields.Count; index++)
        {
            fields[index]!["localId"] = "question-" + index;
            fields[index]!.AsObject().Remove("key");
        }
        using var check = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publication-checks", checkBody, Ct);
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var checkedBody = await check.ReadJsonAsync();
        Assert.True(checkedBody.GetProperty("canAccept").GetBoolean(), checkedBody.GetRawText());
        checkBody["requestId"] = Guid.NewGuid().ToString();
        checkBody["checkToken"] = checkedBody.GetProperty("checkToken").GetString();
        using var accepted = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", checkBody, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        var versionId = receipt.GetProperty("versionId").GetGuid();
        var operationId = receipt.GetProperty("operationId").GetGuid();
        var driver = host.Services.GetRequiredService<IStructuredFormPublicationDriver>();
        var worker = host.Services.GetRequiredService<WhatsAppFlowBackgroundService>();
        var published = false;
        for (var attempt = 0; attempt < 25; attempt++)
        {
            await driver.ProcessAsync(operationId, Ct);
            await worker.ProcessNextAsync(operationId, Ct);
            clock.Advance(TimeSpan.FromSeconds(16));
            using var status = await author.GetAsync(receipt.GetProperty("recoveryPath").GetString(), Ct);
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            if ((await status.ReadJsonAsync()).GetProperty("state").GetString() == "Published") { published = true; break; }
        }
        Assert.True(published, "The real publication must finish through both modules.");
        if (scenario == "Inactive")
        {
            using var deactivate = await author.PutAsJsonAsync($"/api/forms/catalogs/{manualId}", new
            {
                expectedRevision = 1, isActive = false, name = new { es = "Manual", en = "Manual" },
                description = new { es = "Lista manual", en = "Manual list" }, items = Array.Empty<object>(),
            }, Ct);
            Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        }
        var contactId = await CreateLinkedContactAsync(employeeId, clock);
        using var choices = await sender.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
        Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        var context = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
        using var preparation = await sender.GetAsync($"/api/whatsapp/chats/{contactId}/forms/{definitionId}/versions/{versionId}/preparation", Ct);
        Assert.Equal(HttpStatusCode.OK, preparation.StatusCode);
        var prepared = (await preparation.ReadJsonAsync()).GetProperty("preparation");
        Assert.Equal(scenario == "Valid", prepared.GetProperty("canSend").GetBoolean());
        Assert.DoesNotContain(personName, prepared.GetRawText());
        Assert.DoesNotContain("Full detail", prepared.GetRawText());
        if (scenario != "Valid")
        {
            var issue = Assert.Single(prepared.GetProperty("issues").EnumerateArray());
            Assert.Equal("Forms.Record." + (scenario == "Inactive" ? "CatalogUnavailable" : scenario == "TooMany" ? "CatalogTooLarge" : "CatalogOptionInvalid"), issue.GetProperty("code").GetString());
            if (scenario == "LongText")
            {
                Assert.Equal(1, issue.GetProperty("invalidOptionCount").GetInt32());
                Assert.Equal("Personas", issue.GetProperty("catalogName").GetString());
            }
        }
        using var send = await sender.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", new
        {
            requestId = Guid.NewGuid(), definitionId, versionId, expectedModeRevision = 0,
            expectedRecipientContextRevision = context, message = "Complete the real catalogs",
        }, Ct);
        Assert.Equal(scenario == "Valid" ? HttpStatusCode.Accepted : HttpStatusCode.Conflict, send.StatusCode);
        if (scenario != "Valid")
        {
            Assert.Equal("WhatsApp.Flow.CatalogPreparationBlocked", (await send.ReadJsonAsync()).GetProperty("code").GetString());
            Assert.Empty(cloud.Messages);
            Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<ArquitecturaBase.Domain.Modules.WhatsApp.WhatsAppFlowSession>().CountAsync(session => session.ContactId == contactId, Ct)));
            return;
        }
        Assert.All(prepared.GetProperty("catalogs").EnumerateArray(), summary => Assert.Equal(1, summary.GetProperty("count").GetInt32()));
        await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        var sent = Assert.IsType<WhatsAppFlowMessageResponse>(Assert.Single(cloud.Messages));
        Assert.Equal(2, sent.Catalogs.Count);
        Assert.Contains(sent.Catalogs, offer => offer.CatalogId == manualId && offer.Options.Single().Text == "Offered item 0");
        Assert.Contains(sent.Catalogs, offer => offer.CatalogId == filteredId && offer.Options.Single().Id == employeeId && offer.Options[0].Text == personName);
    }
}
