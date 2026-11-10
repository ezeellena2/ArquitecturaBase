using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

/// <summary>
/// An issuer that only exists to test the «IssueDocument» action: it records what it is asked, answers what the test sets and
/// asks for a permission other than managing the rules to decide on an issuance in review.
/// </summary>
internal sealed class TestRecordIssuer : IStructuredRecordIssuer
{
    public const string Key = "test.issue";
    private readonly object _gate = new();
    private readonly List<StructuredIssueRequest> _requests = [];
    private readonly List<StructuredIssueReview> _reviews = [];

    public StructuredIssueResponse Current { get; set; } = new(StructuredIssueState.NotFound);
    public StructuredIssueResponse Next { get; set; } = new(StructuredIssueState.Pending);
    public IReadOnlyList<StructuredIssueRequest> Requests { get { lock (_gate) return [.. _requests]; } }
    public IReadOnlyList<StructuredIssueReview> Reviews { get { lock (_gate) return [.. _reviews]; } }

    public string IssuerId => Key;
    public IReadOnlyDictionary<string, string> Name { get; } = new Dictionary<string, string> { ["es"] = "Emitir de prueba", ["en"] = "Test issue" };
    public string StandardKey => Key;
    public string ReviewPermission => BusinessFormsPermissions.ApprovalsDecide;
    public IReadOnlyList<StructuredRecordResultField> ResultFields { get; } =
        [new("number", new Dictionary<string, string> { ["es"] = "Número", ["en"] = "Number" }, StructuredRecordResultField.Text)];

    public Task<StructuredIssueResponse> EnqueueAsync(StructuredIssueRequest request, CancellationToken cancellationToken)
    {
        lock (_gate) _requests.Add(request);
        Current = Next;
        return Task.FromResult(Next);
    }

    public Task<StructuredIssueResponse> GetAsync(Guid runId, CancellationToken cancellationToken) => Task.FromResult(Current);

    public Task<StructuredIssueResponse> ReviewAsync(Guid runId, StructuredIssueReview review, CancellationToken cancellationToken)
    {
        lock (_gate) _reviews.Add(review);
        Current = Next;
        return Task.FromResult(Next);
    }
}
