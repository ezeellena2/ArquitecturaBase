using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Repeated_module_registration_keeps_one_technical_presentation_seed_participant()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddBusinessFormsInfrastructure().AddBusinessFormsInfrastructure()));
        await using var scope = host.Services.CreateAsyncScope();
        var participants = scope.ServiceProvider.GetServices<IDatabaseSeedParticipant>();
        Assert.Single(participants.OfType<ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Seed.FormVersionPresentationSeeder>());
    }

    [Theory]
    [InlineData("es", false, 0)]
    [InlineData("en", true, 0)]
    [InlineData("es", false, 1)]
    [InlineData("en", true, 1)]
    [InlineData("es", true, 2)]
    [InlineData("en", false, 2)]
    public async Task Version_presentation_uses_the_accepted_snapshot_and_legacy_backfill_is_idempotent(string original, bool bilingual, int legacyShape)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var payload = Payload("Sealed presentation");
        payload["draft"]!["culture"] = original;
        if (!bilingual) payload["draft"]!["submitLabel"]!.AsObject().Remove(original == "es" ? "en" : "es");
        var saved = await CreateAsync(actor, payload);
        string[] publishedCultures = bilingual ? ["es", "en"] : [original];
        var definitionId = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await using var host = PublicationHost(); using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var attempt = await PublicationAttemptAsync(client, definitionId, saved);
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync(); var versionId = receipt.GetProperty("versionId").GetGuid();
        var operationId = receipt.GetProperty("operationId").GetGuid();
        var before = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().SingleAsync(value => value.Id == versionId, Ct));
        var manifest = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPublicationOperation>().Where(value => value.Id == operationId).Select(value => value.ProtectedManifest).SingleAsync(Ct));
        Assert.NotNull(before.PublishedCultures);
        Assert.Equal(publishedCultures, before.PublishedCultures);
        if (legacyShape == 1)
            await factory.ExecuteDbContextAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"BusinessFormVersions\" SET \"NameJson\"=NULL, \"SubmitLabelJson\"=NULL, \"Culture\"=NULL, \"QuestionCount\"=NULL, \"PublishedCultures\"=NULL WHERE \"Id\"={versionId}", Ct));
        else if (legacyShape == 2)
            await factory.ExecuteDbContextAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"BusinessFormVersions\" SET \"PublishedCultures\"=NULL WHERE \"Id\"={versionId}", Ct));
        var edit = EditPayload(saved); edit["name"]!["es"] = "New draft"; edit["name"]!["en"] = "New draft";
        using var edited = await client.PutAsJsonAsync($"/api/forms/definitions/{definitionId}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        await host.Services.SeedDatabaseAsync(Ct);
        await host.Services.SeedDatabaseAsync(Ct);
        var after = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().SingleAsync(value => value.Id == versionId, Ct));
        Assert.NotNull(after.NameJson); Assert.NotNull(after.SubmitLabelJson);
        using var name = JsonDocument.Parse(after.NameJson); using var submit = JsonDocument.Parse(after.SubmitLabelJson);
        Assert.Equal("Sealed presentation", name.RootElement.GetProperty("es").GetString());
        Assert.Equal("Sealed presentation", name.RootElement.GetProperty("en").GetString());
        Assert.Equal(original == "es" ? "Enviar" : "Submit", submit.RootElement.GetProperty(original).GetString());
        Assert.Equal(original, after.Culture);
        Assert.NotNull(after.PublishedCultures);
        Assert.Equal(publishedCultures, after.PublishedCultures);
        Assert.Equal(saved.GetProperty("definition").GetProperty("draft").GetProperty("fields").GetArrayLength(), after.QuestionCount);
        Assert.Equal(before.SchemaJson, after.SchemaJson); Assert.Equal(before.ContentHash, after.ContentHash); Assert.Equal(before.Number, after.Number);
        Assert.Equal(before.CreatedAtUtc, after.CreatedAtUtc);
        Assert.Equal(manifest, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPublicationOperation>().Where(value => value.Id == operationId).Select(value => value.ProtectedManifest).SingleAsync(Ct)));
        using var current = await client.GetAsync($"/api/forms/definitions/{definitionId}", Ct);
        Assert.Equal("New draft", (await current.ReadJsonAsync()).GetProperty("definition").GetProperty("name").GetProperty("es").GetString());
    }
}
