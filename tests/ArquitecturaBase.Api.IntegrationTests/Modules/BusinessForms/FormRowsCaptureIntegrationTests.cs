using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Real_capture_keeps_the_items_of_a_block_and_replays_the_item_of_a_retired_option()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage,
            BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead]);
        var actorId = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        using var created = await actor.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Artículos", en = "Items" }, description = new { es = "Lista", en = "List" },
            items = new[]
            {
                new { localId = "first", code = "ROWS-A", text = new { es = "Primero" }, detail = new { es = "Detalle" }, isActive = true },
                new { localId = "second", code = "ROWS-B", text = new { es = "Segundo" }, detail = new { es = "Detalle" }, isActive = true },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalogJson = await created.ReadJsonAsync();
        var catalogId = catalogJson.GetProperty("catalog").GetProperty("id").GetGuid();
        var first = catalogJson.GetProperty("itemIdentityMap").GetProperty("first").GetGuid();
        var second = catalogJson.GetProperty("itemIdentityMap").GetProperty("second").GetGuid();
        var supplier = Field("shortText", true);
        var group = Field("group", false) with { Rows = new(0, 3) };
        var item = Field("choice", true) with { Source = new(catalogId), Repeated = true };
        var quantity = Field("decimal", true) with { Repeated = true };
        var form = await SeedVersionAsync(actorId, [supplier, group, item, quantity]);
        using var scope = factory.Services.CreateScope();
        var preparation = await scope.ServiceProvider.GetRequiredService<IStructuredFormSource>()
            .PrepareAsync(actorId, "es", form.DefinitionId, form.VersionId, false, Ct);
        var offer = Assert.Single(preparation.Catalogs);
        Assert.Equal(item.FieldId, offer.FieldId);
        StructuredRecordCaptureRequest Request(params (Guid Item, string Quantity)[] items) => new(Guid.NewGuid(), form.DefinitionId,
            form.VersionId, actorId, factory.Clock.GetUtcNow().UtcDateTime, new Dictionary<string, string>
            {
                [supplier.Key] = "Proveedor",
                [group.Key] = JsonSerializer.Serialize(items.Select(value => new Dictionary<string, string>
                {
                    [item.Key] = value.Item.ToString("D"), [quantity.Key] = value.Quantity,
                })),
            }, [offer]);
        var sink = scope.ServiceProvider.GetRequiredService<IStructuredRecordSink>();

        var request = Request((first, "200.5"), (first, "100"), (second, "7"));
        var captured = await sink.CaptureAsync(request, Ct);

        Assert.Equal("Completed", captured.State);
        var values = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordValue>().AsNoTracking()
            .Where(value => value.RecordId == captured.RecordId && value.FieldId == quantity.FieldId).OrderBy(value => value.Row)
            .Select(value => new { value.Row, value.DecimalValue }).ToListAsync(Ct));
        Assert.Equal([0, 1, 2], values.Select(value => value.Row));
        Assert.Equal([200.5m, 100m, 7m], values.Select(value => value.DecimalValue!.Value));
        var references = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordReference>().AsNoTracking()
            .Where(reference => reference.RecordId == captured.RecordId).OrderBy(reference => reference.Row)
            .Select(reference => new { reference.Row, reference.ItemId }).ToListAsync(Ct));
        Assert.Equal([0, 1, 2], references.Select(reference => reference.Row));
        Assert.Equal([first, first, second], references.Select(reference => reference.ItemId));
        using var readScope = factory.Services.CreateScope();
        var read = await readScope.ServiceProvider.GetRequiredService<IStructuredRecordSource>()
            .ReadAsync(actorId, "es", [request.SourceReceiptId], Ct);
        var reply = Assert.Single(read.Replies).Value;
        Assert.Equal([supplier.FieldId, group.FieldId], reply.Values.Select(value => value.FieldId));
        Assert.Equal(3, Assert.IsAssignableFrom<IReadOnlyList<StructuredRecordReadRow>>(reply.Values[1].Value).Count);

        using var retired = await actor.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", new
        {
            expectedRevision = 1, name = new { es = "Artículos", en = "Items" }, description = new { es = "Lista", en = "List" },
            items = new[]
            {
                new { localId = "first", itemId = first, code = "ROWS-A", text = new { es = "Primero" }, detail = new { es = "Detalle" }, isActive = true },
                new { localId = "second", itemId = second, code = "ROWS-B", text = new { es = "Segundo" }, detail = new { es = "Detalle" }, isActive = false },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
        var rejectedRequest = Request((first, "1"), (second, "2"));
        using var rejectScope = factory.Services.CreateScope();
        var rejected = await rejectScope.ServiceProvider.GetRequiredService<IStructuredRecordSink>().CaptureAsync(rejectedRequest, Ct);
        using var replayScope = factory.Services.CreateScope();
        var replay = await replayScope.ServiceProvider.GetRequiredService<IStructuredRecordSink>().CaptureAsync(rejectedRequest, Ct);

        Assert.Equal(FormRecordErrors.CatalogItemInactive.Code, rejected.ErrorCode);
        Assert.Equal(item.FieldId, rejected.RejectedFieldId);
        Assert.Equal(1, rejected.RejectedRow);
        Assert.Equal(rejected, replay);
        Assert.Equal(rejected, await replayScope.ServiceProvider.GetRequiredService<IStructuredRecordSink>().FindExistingReceiptAsync(rejectedRequest, Ct));
    }

    private async Task<(Guid DefinitionId, Guid VersionId)> SeedVersionAsync(Guid actor, IReadOnlyList<CanonicalFormField> fields)
    {
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, "Renglones", "{\"es\":\"Renglones\",\"en\":\"Rows\"}", "G" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), "es",
            "{\"es\":\"Enviar\",\"en\":\"Submit\"}", JsonSerializer.Serialize(new FormQuestionSchema(1, fields), SerializerOptions),
            fields.Count(field => field.Type != "group"), true).IsSuccess);
        var version = BusinessFormVersion.Create(definition.Id, 1, new string('a', 64));
        version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, fields), SerializerOptions));
        version.SealPresentation("{\"es\":\"Renglones\",\"en\":\"Rows\"}", "{\"es\":\"Enviar\",\"en\":\"Submit\"}", "es", fields.Count(field => field.Type != "group"));
        var operation = FormPublishedVersionFixture.Create(factory, definition, version, actor, ["es", "en"]);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormDefinition>().Add(definition);
            foreach (var field in fields) db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definition.Id, field.Key, field.Type));
            await db.SaveChangesAsync(Ct);
            db.Set<BusinessFormVersion>().Add(version);
            db.Set<BusinessFormPublicationOperation>().Add(operation);
            await db.SaveChangesAsync(Ct);
            Assert.True(definition.ActivateVersion(null, version.Id).IsSuccess);
            await db.SaveChangesAsync(Ct);
        });
        return (definition.Id, version.Id);
    }
}
