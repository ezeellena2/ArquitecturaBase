using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Catalog_capture_preserves_the_offer_after_rename_and_rejects_deactivation_without_numbering()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage,
            BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead]);
        var actorId = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        using var created = await actor.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Personas", en = "People" }, description = new { es = "Lista", en = "List" },
            items = new[] { new { localId = "item", code = "PERSON", text = new { es = "Texto original" },
                detail = new { es = "Detalle original" }, isActive = true } },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalogJson = await created.ReadJsonAsync();
        var catalogId = catalogJson.GetProperty("catalog").GetProperty("id").GetGuid();
        var itemId = catalogJson.GetProperty("itemIdentityMap").GetProperty("item").GetGuid();
        var form = await SeedCatalogVersionAsync(actorId, catalogId);
        using var scope = factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IStructuredFormSource>();
        var preparation = await source.PrepareAsync(actorId, "en", form.DefinitionId, form.VersionId, false, Ct);
        Assert.True(preparation.CanSend);
        var offer = Assert.Single(preparation.Catalogs);
        Assert.Equal("Texto original", Assert.Single(offer.Options).Text);
        Assert.Equal("Detalle original", offer.Options[0].Detail);
        Assert.Equal("People", offer.CatalogName);
        using var changed = await actor.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", CatalogEdit(1, true, "Texto nuevo"), Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var request = new StructuredRecordCaptureRequest(Guid.NewGuid(), form.DefinitionId, form.VersionId, actorId,
            factory.Clock.GetUtcNow().UtcDateTime, new Dictionary<string, string> { [offer.Key] = itemId.ToString("D") }, [offer]);
        var sink = scope.ServiceProvider.GetRequiredService<IStructuredRecordSink>();
        var captured = await sink.CaptureAsync(request, Ct);
        Assert.Equal("Completed", captured.State);
        Assert.Equal(captured, await sink.CaptureAsync(request, Ct));
        var reference = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordReference>()
            .SingleAsync(value => value.RecordId == captured.RecordId, Ct));
        Assert.Equal(catalogId, reference.CatalogId);
        Assert.Equal(1, reference.CatalogRevision);
        Assert.Equal(itemId, reference.ItemId);
        Assert.Equal("Texto original", reference.Text);
        Assert.Equal("Detalle original", reference.Detail);
        using var inactive = await actor.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", CatalogEdit(2, false, "Texto nuevo"), Ct);
        Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
        using var captureScope = factory.Services.CreateScope();
        var anotherSink = captureScope.ServiceProvider.GetRequiredService<IStructuredRecordSink>();
        var secondRequest = request with { SourceReceiptId = Guid.NewGuid() };
        var rejected = await anotherSink.CaptureAsync(secondRequest, Ct);
        Assert.Equal("Rejected", rejected.State);
        Assert.Equal(FormRecordErrors.CatalogItemInactive.Code, rejected.ErrorCode);
        Assert.Equal(offer.FieldId, rejected.RejectedFieldId);
        Assert.Equal(rejected, await anotherSink.CaptureAsync(secondRequest, Ct));
        Assert.Null(rejected.Number);
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>()
            .Where(value => value.Id == form.DefinitionId).Select(value => value.LastRecordNumber).SingleAsync(Ct)));
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>()
            .CountAsync(value => value.DefinitionId == form.DefinitionId, Ct)));
        using var reloadScope = factory.Services.CreateScope();
        var reader = reloadScope.ServiceProvider.GetRequiredService<IStructuredRecordSource>();
        var read = await reader.ReadAsync(actorId, "es", [request.SourceReceiptId], Ct);
        var answer = Assert.IsType<StructuredRecordChoiceValue>(Assert.Single(Assert.Single(read.Replies).Value.Values).Value);
        Assert.Equal("Texto original", answer.Label);
        Assert.Equal("People", answer.CatalogName);
        Assert.Equal("Detalle original", answer.Detail);

        object CatalogEdit(long revision, bool active, string text) => new
        {
            expectedRevision = revision, name = new { es = "Personas", en = "People" }, description = new { es = "Lista", en = "List" },
            items = new[] { new { itemId, code = "PERSON", text = new { es = text }, detail = new { es = "Detalle nuevo" }, isActive = active } },
        };
    }

    [Fact]
    public async Task Filters_are_prepared_per_recipient_and_capture_does_not_requery_the_source_view()
    {
        using var first = await OperatorAsync([BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead]);
        using var second = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        var firstId = Guid.Parse(first.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var secondId = Guid.Parse(second.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await factory.InTransactionAsync(async services =>
        {
            var users = services.GetRequiredService<IUserRepository>();
            await users.SetDisplayNameAsync(firstId, "First " + suffix, Ct);
            await users.SetDisplayNameAsync(secondId, "Second " + suffix, Ct);
            return true;
        });
        await BusinessFormsCatalogUsersView.EnsureCreatedAsync(factory, Ct);
        var filters = new FormCatalogFilterDefinition("name", null, [new("name", "contains", suffix)], true, "name", false);
        var catalog = BusinessFormCatalog.CreateFilters(new Dictionary<string, string> { ["es"] = "Personas", ["en"] = "People" },
            new Dictionary<string, string>(), JsonSerializer.Serialize(filters, SerializerOptions));
        await factory.ExecuteDbContextAsync(async db => { db.Set<BusinessFormCatalog>().Add(catalog); await db.SaveChangesAsync(Ct); });
        var form = await SeedCatalogVersionAsync(firstId, catalog.Id);
        using var scope = factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IStructuredFormSource>();
        var forFirst = await source.PrepareAsync(firstId, "es", form.DefinitionId, form.VersionId, false, Ct);
        var forSecond = await source.PrepareAsync(secondId, "en", form.DefinitionId, form.VersionId, false, Ct);
        Assert.True(forFirst.CanSend); Assert.True(forSecond.CanSend);
        Assert.Equal(secondId, Assert.Single(Assert.Single(forFirst.Catalogs).Options).Id);
        Assert.Equal(firstId, Assert.Single(Assert.Single(forSecond.Catalogs).Options).Id);
        var offer = Assert.Single(forFirst.Catalogs);
        await factory.ExecuteDbContextAsync(db => db.Database.ExecuteSqlRawAsync("DROP VIEW \"BusinessFormCatalogUsers\"", Ct));
        try
        {
            using var captureScope = factory.Services.CreateScope();
            var sink = captureScope.ServiceProvider.GetRequiredService<IStructuredRecordSink>();
            var request = new StructuredRecordCaptureRequest(Guid.NewGuid(), form.DefinitionId, form.VersionId, firstId,
                factory.Clock.GetUtcNow().UtcDateTime, new Dictionary<string, string> { [offer.Key] = secondId.ToString("D") }, [offer]);
            var result = await sink.CaptureAsync(request, Ct);
            Assert.Equal("Completed", result.State);
            Assert.Equal(secondId, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordReference>()
                .Where(value => value.RecordId == result.RecordId).Select(value => value.ItemId).SingleAsync(Ct)));
            var unavailable = await source.PrepareAsync(firstId, "es", form.DefinitionId, form.VersionId, false, Ct);
            Assert.False(unavailable.CanSend);
            Assert.Equal(FormRecordErrors.CatalogUnavailable.Code, Assert.Single(unavailable.Issues).Code);
        }
        finally { await BusinessFormsCatalogUsersView.EnsureCreatedAsync(factory, Ct); }
    }

    private async Task<(Guid DefinitionId, Guid VersionId)> SeedCatalogVersionAsync(Guid actor, Guid catalogId)
    {
        var field = Field("choice", true) with { Source = new(catalogId) };
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, "Catálogo", "{\"es\":\"Catálogo\",\"en\":\"Catalog\"}", "K" + definition.Id.ToString("N")[..8], "es",
            "{\"es\":\"Enviar\",\"en\":\"Submit\"}", JsonSerializer.Serialize(new FormQuestionSchema(1, [field]), SerializerOptions), 1, true).IsSuccess);
        var version = BusinessFormVersion.Create(definition.Id, 1, new string('a', 64));
        version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, [field]), SerializerOptions));
        version.SealPresentation("{\"es\":\"Catálogo\",\"en\":\"Catalog\"}", "{\"es\":\"Enviar\",\"en\":\"Submit\"}", "es", 1);
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var operation = BusinessFormPublicationOperation.Accept(definition.Id, version.Id, actor, Guid.NewGuid(), 1, 1, null, [1], now);
        var fence = operation.Reserve(now, TimeSpan.FromMinutes(1)).Value;
        Assert.True(operation.RecordChannelResult(fence, now, "Published", "Verify").IsSuccess);
        Assert.True(operation.Complete(fence, now).IsSuccess);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormDefinition>().Add(definition);
            db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definition.Id, field.Key, field.Type));
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
