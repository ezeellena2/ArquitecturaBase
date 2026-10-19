using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormPublicationStatusLanguageTests
{
    [Theory]
    [InlineData("Accepted", new string[] { "es" })]
    [InlineData("Accepted", new string[] { "es", "en" })]
    [InlineData("Accepted", new string[] { "en" })]
    [InlineData("Published", new string[] { "es" })]
    [InlineData("Published", new string[] { "es", "en" })]
    [InlineData("Published", new string[] { "en" })]
    public async Task Status_exposes_the_cultures_sealed_by_its_operation_without_reading_another_version(
        string state, string[] expectedCultures)
    {
        var fixture = new FormPublicationStatusLanguageFixture(state, expectedCultures);
        var currentUser = new FakeCurrentUser { UserId = fixture.ActorId };
        var permissions = new FakePermissionService();
        permissions.Permissions[fixture.ActorId] = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Publish];
        var service = new FormPublicationStatusService(fixture, new FormPublicationAccessGuard(currentUser, permissions),
            fixture, [], NullLogger<FormPublicationStatusService>.Instance);

        var result = await service.GetAsync(fixture.DefinitionId, fixture.RequestId, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var json = JsonSerializer.SerializeToElement(result.Value, JsonSerializerOptions.Web);
        Assert.True(json.TryGetProperty("cultures", out var cultures),
            "The operation status must expose the cultures sealed by its own publication manifest.");
        Assert.Equal(expectedCultures, cultures.EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Equal(state, result.Value.State);
        Assert.Equal(fixture.Stored.VersionId, result.Value.VersionId);
        Assert.Equal(fixture.DefinitionId, fixture.QueriedDefinitionId);
        Assert.Equal(fixture.ActorId, fixture.QueriedActorId);
        Assert.Equal(fixture.RequestId, fixture.QueriedRequestId);
        Assert.Equal(fixture.Stored.Id, fixture.QueriedOperationId);
        Assert.Equal(1, fixture.ManifestReads);
        Assert.Equal(new FormPublicationProtectionContext(fixture.Stored.Id, fixture.ActorId, fixture.RequestId,
            fixture.DefinitionId, fixture.Stored.VersionId), fixture.ManifestContext);
        Assert.Same(fixture.Stored.ProtectedManifest, fixture.ManifestBytes);
    }
}
