using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// Task 7: the form the server fixes for an extraction of the inbox (FocusDefinitionId) binds the operation's fingerprint only when it exists,
// so the same request id for another form is another request and every request without it keeps its fingerprint. No database.
public sealed class AssistantFocusDefinitionFingerprintTests
{
    private static readonly HmacAiAssistantRequestFingerprint Fingerprint = new(new AiKeyRing(Options.Create(new AiSecretProtectionOptions
    {
        InstallationId = "fictitious-installation", ActiveKeyId = "one", Keys = new() { ["one"] = Convert.ToBase64String(new byte[32]) },
    })));

    private static readonly AssistantOperationRequest Request = new(Guid.NewGuid(), new("forms-documents", Guid.NewGuid(), null, "es", "UTC",
        new(["formdocuments.review"], []), [new("forms.documents.form", 1), new("forms.documents.propose-entry", 1)], [new("assistant_proposal", 1)]),
        "Leé el documento adjunto.", new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void The_fixed_form_binds_the_request_only_when_there_is_one()
    {
        var first = Request with { Context = Request.Context with { FocusDefinitionId = Guid.NewGuid() } };
        var second = Request with { Context = Request.Context with { FocusDefinitionId = Guid.NewGuid() } };
        var withFirst = Fingerprint.Create(first, "one");
        Assert.True(Fingerprint.Matches(first, "one", withFirst));
        Assert.False(Fingerprint.Matches(second, "one", withFirst));
        Assert.False(Fingerprint.Matches(Request, "one", withFirst));
        Assert.NotEqual(Fingerprint.Create(Request, "one"), withFirst);
    }
}
