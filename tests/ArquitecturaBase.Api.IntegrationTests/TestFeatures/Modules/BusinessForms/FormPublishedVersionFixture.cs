using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal static class FormPublishedVersionFixture
{
    public static BusinessFormPublicationOperation Create(ApiFactory factory, BusinessFormDefinition definition,
        BusinessFormVersion version, Guid actor, IReadOnlyList<string> cultures)
    {
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var operation = BusinessFormPublicationOperation.Accept(definition.Id, version.Id, actor, Guid.NewGuid(),
            definition.Revision, version.Number, definition.ActiveVersionId, [], now);
        var manifest = new FormPublicationManifest(1, "fixture-attempt", "fixture-publication", definition.Id,
            version.Id, definition.Revision, version.ContentHash,
            JsonSerializer.Deserialize<Dictionary<string, string>>(version.NameJson!)!, definition.Prefix, version.Culture!,
            JsonSerializer.Deserialize<Dictionary<string, string>>(version.SubmitLabelJson!)!,
            [new("phone", "fixture-profile", cultures.Select(culture => new FormPublicationCultureProfile(culture, "fixture-compilation")).ToArray())]);
        using var scope = factory.Services.CreateScope();
        operation.SealManifest(scope.ServiceProvider.GetRequiredService<IFormPublicationProtector>().ProtectManifest(
            new(operation.Id, actor, operation.RequestId, definition.Id, version.Id), manifest));
        var fence = operation.Reserve(now, TimeSpan.FromMinutes(1)).Value;
        Assert.True(operation.RecordChannelResult(fence, now, "Published", "Verify").IsSuccess);
        Assert.True(operation.Complete(fence, now).IsSuccess);
        return operation;
    }
}
