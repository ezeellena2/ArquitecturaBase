using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
// SA1-A to SA5 (2026-11-09): a form completed only in the app publishes at once, without asking a channel, and the
// channels do not offer it. What existed keeps its exact responses: the new values are omitted unless they are true.
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task A_form_completed_only_in_the_app_publishes_at_once_and_the_channels_do_not_offer_it()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var form = Payload("App only " + Guid.NewGuid().ToString("N")[..10]); form["draft"]!["appOnly"] = true;
        var saved = await CreateAsync(client, form);
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        Assert.True(saved.GetProperty("definition").GetProperty("draft").GetProperty("appOnly").GetBoolean());

        // The channel of the tests cannot publish (no account): only in the app it does not matter.
        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var checkedResult = await check.ReadJsonAsync();
        Assert.Equal("LocallyValidated", checkedResult.GetProperty("compatibility").GetString());
        Assert.True(checkedResult.GetProperty("canAccept").GetBoolean());
        Assert.Equal("Available", checkedResult.GetProperty("acceptanceAvailability").GetString());
        Assert.Empty(checkedResult.GetProperty("channels").EnumerateArray());
        Assert.Empty(checkedResult.GetProperty("issues").EnumerateArray());

        var attempt = CheckPayload(saved); attempt["requestId"] = Guid.NewGuid().ToString();
        attempt["checkToken"] = checkedResult.GetProperty("checkToken").GetString();
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        Assert.Equal("Published", receipt.GetProperty("state").GetString());
        Assert.Equal(1, receipt.GetProperty("versionNumber").GetInt64());
        using var replay = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(receipt.GetRawText(), (await replay.ReadJsonAsync()).GetRawText());

        using var status = await client.GetAsync(receipt.GetProperty("recoveryPath").GetString(), Ct);
        var state = await status.ReadJsonAsync();
        Assert.Equal("Published", state.GetProperty("state").GetString());
        Assert.Equal(["en", "es"], state.GetProperty("cultures").EnumerateArray().Select(value => value.GetString()!).Order().ToArray());

        using var detail = await client.GetAsync($"/api/forms/definitions/{id}", Ct);
        var loaded = await detail.ReadJsonAsync();
        var publication = loaded.GetProperty("publication");
        Assert.Equal("Published", publication.GetProperty("displayState").GetString());
        Assert.Equal(1, publication.GetProperty("activeVersion").GetProperty("versionNumber").GetInt64());
        Assert.True(publication.GetProperty("activeVersion").GetProperty("appOnly").GetBoolean());
        Assert.Equal(["en", "es"], publication.GetProperty("activeVersion").GetProperty("cultures").EnumerateArray()
            .Select(value => value.GetString()!).Order().ToArray());
        Assert.False(loaded.GetProperty("capabilities").GetProperty("canPreview").GetBoolean());
        Assert.True(loaded.GetProperty("lifecycle").GetProperty("publishedOnlyInApp").GetBoolean());

        var definition = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().AsNoTracking().SingleAsync(x => x.Id == id, Ct));
        var version = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().AsNoTracking().SingleAsync(x => x.DefinitionId == id, Ct));
        var operation = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPublicationOperation>().AsNoTracking().SingleAsync(x => x.DefinitionId == id, Ct));
        Assert.Equal(version.Id, definition.ActiveVersionId);
        Assert.True(definition.PrefixLocked);
        Assert.True(version.AppOnly);
        Assert.Equal(["en", "es"], version.PublishedCultures!.Order().ToArray());
        Assert.Equal(FormPublicationState.Published, operation.State);
        Assert.False(operation.IsOpen);

        using var list = await client.GetAsync($"/api/forms/definitions?search={Uri.EscapeDataString(saved.GetProperty("definition").GetProperty("name").GetProperty("es").GetString()!)}", Ct);
        var row = Assert.Single((await list.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.True(row.GetProperty("appOnly").GetBoolean());

        var offered = await factory.ExecuteScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IFormSelectionReader>();
            return (await reader.FindAsync(id, version.Id, Ct), await reader.ListSuggestionTargetsAsync([id], Ct));
        });
        Assert.Null(offered.Item1);
        Assert.Empty(offered.Item2);
    }

    [Fact]
    public async Task A_form_completed_also_by_a_channel_keeps_every_response_as_it_was()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(client, Payload("Channel " + Guid.NewGuid().ToString("N")[..10]));
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        Assert.False(saved.GetProperty("definition").GetProperty("draft").TryGetProperty("appOnly", out _));

        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        var checkedResult = await check.ReadJsonAsync();
        Assert.False(checkedResult.GetProperty("canAccept").GetBoolean());
        Assert.Equal("Unavailable", checkedResult.GetProperty("acceptanceAvailability").GetString());
        Assert.NotEmpty(checkedResult.GetProperty("channels").EnumerateArray());

        using var detail = await client.GetAsync($"/api/forms/definitions/{id}", Ct);
        var loaded = await detail.ReadJsonAsync();
        Assert.False(loaded.GetProperty("lifecycle").TryGetProperty("publishedOnlyInApp", out _));
        using var list = await client.GetAsync($"/api/forms/definitions?search={Uri.EscapeDataString(saved.GetProperty("definition").GetProperty("name").GetProperty("es").GetString()!)}", Ct);
        Assert.False(Assert.Single((await list.ReadJsonAsync()).GetProperty("items").EnumerateArray()).TryGetProperty("appOnly", out _));
    }

    [Fact]
    public async Task Without_a_form_channel_every_form_publishes_only_in_the_app()
    {
        using var writer = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(writer, Payload("No channel " + Guid.NewGuid().ToString("N")[..10]));
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.RemoveAll<IStructuredFormChannel>()));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, writer.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));

        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        var checkedResult = await check.ReadJsonAsync();
        Assert.True(checkedResult.GetProperty("canAccept").GetBoolean());
        Assert.Empty(checkedResult.GetProperty("channels").EnumerateArray());
        Assert.Empty(checkedResult.GetProperty("issues").EnumerateArray());
        var attempt = CheckPayload(saved); attempt["requestId"] = Guid.NewGuid().ToString();
        attempt["checkToken"] = checkedResult.GetProperty("checkToken").GetString();
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal("Published", (await accepted.ReadJsonAsync()).GetProperty("state").GetString());
        Assert.True(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().AnyAsync(x => x.DefinitionId == id && x.AppOnly, Ct)));
        using var presentation = await client.GetAsync("/api/settings/presentation", Ct);
        Assert.DoesNotContain("form-channels", (await presentation.ReadJsonAsync()).GetProperty("installedFeatures").EnumerateArray().Select(value => value.GetString()));
    }
}
