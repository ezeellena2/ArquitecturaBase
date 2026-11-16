using System.Text;
using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;
using Microsoft.AspNetCore.DataProtection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>Technical test without a database: a receipt sealed by a newer version with one more member still opens (migration guide, "Convivencia de versiones").</summary>
public sealed class FormChangeProtectorCompatibilityTests
{
    [Fact]
    public void A_sealed_receipt_with_members_this_version_does_not_know_still_opens()
    {
        var provider = new EphemeralDataProtectionProvider();
        var codec = new DataProtectionFormChangeProtector(provider);
        var requestId = Guid.NewGuid(); var definitionId = Guid.NewGuid();
        var context = new FormChangeProtectionContext(Guid.NewGuid(), Guid.NewGuid(), requestId, definitionId, "create", 1);
        var response = new FormDraftCommitResponse(requestId, new(definitionId, new Dictionary<string, string> { ["es"] = "Name" }, "PRE", false, 1, new(1, "es", new Dictionary<string, string>(), []), null),
            new([], []), new(new DateTime(2026, 12, 12, 12, 0, 0, DateTimeKind.Utc)));
        var sealedByThisVersion = codec.Protect(context, "request", response);
        var protector = provider.CreateProtector("ArquitecturaBase.BusinessForms.DraftChanges.v1", context.Id.ToString("N"), context.Actor.ToString("N"),
            context.RequestId.ToString("N"), context.DefinitionId.ToString("N"), context.Kind);
        var json = JsonNode.Parse(protector.Unprotect(sealedByThisVersion))!;
        json["addedByANewerVersion"] = 1;
        json["result"]!["addedToo"] = "x";
        var sealedByNewer = protector.Protect(Encoding.UTF8.GetBytes(json.ToJsonString()));

        var opened = codec.Unprotect(context, sealedByNewer);

        Assert.Equal(requestId, opened.Result.RequestId);
        Assert.True(codec.Matches(opened, "request"));
    }
}
