using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Catalog_fields_are_sealed_at_capture_and_are_read_after_catalog_values_and_source_texts_change()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead]);
        var actorId = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var texts = new Dictionary<string, string> { ["es"] = "Campos históricos", ["en"] = "Historical fields" };
        var source = BusinessFormCatalog.CreateManual(texts, texts);
        var sourceItem = source.AddManualItem("ZONE", "Zona ofrecida", "Offered area", null, null, 1).Value;
        var catalog = BusinessFormCatalog.CreateManual(texts, texts);
        var item = catalog.AddManualItem("ITEM", "Opción ofrecida", "Offered option", null, null, 1).Value;
        var amount = new BusinessFormCatalogField(Guid.NewGuid(), "amount", new Dictionary<string, string> { ["es"] = "Importe" },
            "decimal", null, false, false, null, null, null, "data", 0, true);
        var region = new BusinessFormCatalogField(Guid.NewGuid(), "region", new Dictionary<string, string> { ["es"] = "Zona", ["en"] = "Area" },
            "catalog", source.Id, false, false, null, null, null, "filter", 1, true);
        Assert.True(catalog.SetFields([amount, region], null).IsSuccess);
        Assert.True(catalog.SetManualItemFields(item, Values("1.5")).IsSuccess);
        await factory.InTransactionAsync(async services =>
        {
            var repository = services.GetRequiredService<IFormCatalogRepository>();
            foreach (var id in new[] { source.Id, catalog.Id }.Order()) await repository.LockAsync(id, Ct);
            repository.Add(source);
            repository.Add(catalog);
            repository.AddItems([sourceItem, item]);
        });
        var form = await SeedCatalogVersionAsync(actorId, catalog.Id);
        using var scope = factory.Services.CreateScope();
        var preparation = await scope.ServiceProvider.GetRequiredService<IStructuredFormSource>()
            .PrepareAsync(actorId, "en", form.DefinitionId, form.VersionId, false, Ct);
        Assert.True(preparation.CanSend);
        Assert.Equal("en", preparation.Culture);
        var offer = Assert.Single(preparation.Catalogs);
        Assert.Equal("Offered option", Assert.Single(offer.Options).Text);

        await ChangeCatalogsAsync("2.5", "Importe al responder", "Zona al responder", "Area at capture");
        var request = new StructuredRecordCaptureRequest(Guid.NewGuid(), form.DefinitionId, form.VersionId, actorId,
            factory.Clock.GetUtcNow().UtcDateTime, new Dictionary<string, string> { [offer.Key] = item.Id.ToString("D") }, [offer]);
        var sink = scope.ServiceProvider.GetRequiredService<IStructuredRecordSink>();
        var result = await sink.CaptureAsync(request, Ct);
        Assert.Equal("Completed", result.State);
        var reference = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordReference>()
            .SingleAsync(value => value.RecordId == result.RecordId, Ct));
        Assert.Equal(1, reference.CatalogRevision);
        Assert.Equal("Offered option", reference.Text);
        Assert.NotNull(reference.FieldValuesJson);
        var snapshot = JsonSerializer.Deserialize<FormCatalogCapturedField[]>(reference.FieldValuesJson, JsonSerializerOptions.Web)!;
        Assert.Equal([amount.Id, region.Id], snapshot.Select(field => field.Id));
        Assert.Equal("2.5", snapshot[0].Value.GetString());
        Assert.Equal(JsonValueKind.String, snapshot[0].Value.ValueKind);
        Assert.Equal("Importe al responder", snapshot[0].Name["es"]);
        Assert.False(snapshot[0].Name.ContainsKey("en"));
        Assert.Equal("filter", snapshot[1].Role);
        Assert.Equal("Zona al responder", snapshot[1].Text!["es"]);
        Assert.Equal("Area at capture", snapshot[1].Text!["en"]);

        await ChangeCatalogsAsync("99.5", "Importe editado", "Zona editada", "Edited area");
        using var readScope = factory.Services.CreateScope();
        var batch = await readScope.ServiceProvider.GetRequiredService<IFormRecordReader>()
            .FindBatchAsync([request.SourceReceiptId], null, Ct);
        Assert.Equal(reference.FieldValuesJson, Assert.Single(Assert.Single(batch.Rows).References!).FieldValuesJson);
        Assert.Equal(result, await sink.CaptureAsync(request, Ct));
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>()
            .CountAsync(value => value.DefinitionId == form.DefinitionId, Ct)));

        string Values(string value) => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [amount.Id.ToString("D")] = value,
            [region.Id.ToString("D")] = sourceItem.Id.ToString("D"),
        });

        Task ChangeCatalogsAsync(string value, string fieldName, string sourceEs, string sourceEn) => factory.InTransactionAsync(async services =>
        {
            var repository = services.GetRequiredService<IFormCatalogRepository>();
            foreach (var id in new[] { source.Id, catalog.Id }.Order()) await repository.LockAsync(id, Ct);
            var current = (await repository.GetAsync(catalog.Id, Ct))!;
            var currentItem = Assert.Single(await repository.ListItemsAsync(catalog.Id, Ct));
            Assert.True(current.UpdateManual(current.Revision, texts, texts).IsSuccess);
            Assert.True(current.SetFields([amount with { Name = new Dictionary<string, string> { ["es"] = fieldName } }, region], null).IsSuccess);
            Assert.True(current.SetManualItemFields(currentItem, Values(value)).IsSuccess);
            var currentSource = (await repository.GetAsync(source.Id, Ct))!;
            var currentSourceItem = Assert.Single(await repository.ListItemsAsync(source.Id, Ct));
            Assert.True(currentSource.UpdateManual(currentSource.Revision, texts, texts).IsSuccess);
            Assert.True(currentSource.UpdateManualItem(currentSourceItem, currentSourceItem.Code, sourceEs, sourceEn,
                null, null, currentSourceItem.Position).IsSuccess);
        });
    }
}
