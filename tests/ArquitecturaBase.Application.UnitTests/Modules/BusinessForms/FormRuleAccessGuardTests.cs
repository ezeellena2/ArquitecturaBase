using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// H6: each issuer an action names asks for its own permission, as voiding an issued response does; one that is not installed is
// unavailable, not a missing permission, and an action without an issuer asks for nothing here (the rule checks refuse it).
public sealed class FormRuleAccessGuardTests
{
    [Fact]
    public async Task Each_issuer_named_asks_for_its_own_permission_and_one_not_installed_is_unavailable()
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        var guard = new FormRuleAccessGuard(new FakeCurrentUser { UserId = actor }, permissions, [new Issuer("a.issuer", "a.issue"), new Issuer("b.issuer", "b.issue")]);
        permissions.Permissions[actor] = ["a.issue"];

        Assert.True((await guard.MayIssueAsync(actor, [null, "a.issuer", "a.issuer"], CancellationToken.None)).IsSuccess);
        Assert.Equal(FormRuleErrors.IssuerPermissionRequired.Code, (await guard.MayIssueAsync(actor, ["a.issuer", "b.issuer"], CancellationToken.None)).Error.Code);
        Assert.Equal(FormActionErrors.IssuerUnavailable.Code, (await guard.MayIssueAsync(actor, ["c.issuer"], CancellationToken.None)).Error.Code);
        permissions.Permissions[actor] = ["a.issue", "b.issue"];
        Assert.True((await guard.MayIssueAsync(actor, ["a.issuer", "b.issuer"], CancellationToken.None)).IsSuccess);
    }

    private sealed class Issuer(string id, string permission) : IStructuredRecordIssuer
    {
        public string IssuerId => id;
        public IReadOnlyDictionary<string, string> Name { get; } = new Dictionary<string, string> { ["es"] = "Emitir" };
        public string StandardKey => id + ".standard";
        public string ReviewPermission => permission;
        public IReadOnlyList<StructuredRecordResultField> ResultFields { get; } = [];
        public string? CopyRecipient(IReadOnlyList<StructuredIssueDatum> data) => null;
        public Task<StructuredIssuePreview> PreviewAsync(IReadOnlyList<StructuredIssueDatum> data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredIssueResponse> EnqueueAsync(StructuredIssueRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredIssueResponse> GetAsync(Guid runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredIssueResponse> ReviewAsync(Guid runId, StructuredIssueReview review, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredIssueResponse> CancelAsync(Guid runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredIssueResponse> EnqueueReversalAsync(Guid reversalRunId, Guid issuedRunId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
