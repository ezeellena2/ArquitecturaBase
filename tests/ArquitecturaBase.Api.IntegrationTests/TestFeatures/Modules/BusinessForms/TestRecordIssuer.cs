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
    private readonly HashSet<Guid> _enqueued = [];

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
        lock (_gate)
        {
            _requests.Add(request);
            _enqueued.Add(request.RunId);
        }
        Current = Next;
        return Task.FromResult(Next);
    }

    // A run it never queued is not found: a reversal starts by asking for its own run.
    public Task<StructuredIssueResponse> GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (ByRun.TryGetValue(runId, out var byRun)) return Task.FromResult(byRun);
        lock (_gate) return Task.FromResult(_enqueued.Contains(runId) ? Current : new StructuredIssueResponse(StructuredIssueState.NotFound));
    }

    public Task<StructuredIssueResponse> ReviewAsync(Guid runId, StructuredIssueReview review, CancellationToken cancellationToken)
    {
        lock (_gate) _reviews.Add(review);
        Current = Next;
        return Task.FromResult(Next);
    }

    /// <summary>What the issuance of each run answers once set; any other run answers <see cref="Current"/>.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<Guid, StructuredIssueResponse> ByRun { get; } = new();
    public StructuredIssueResponse CancelResult { get; set; } = new(StructuredIssueState.Cancelled);
    public StructuredIssuePreview Preview { get; set; } = new(new Dictionary<string, string> { ["es"] = "Factura C", ["en"] = "Invoice C" }, "Juana Pérez", "3101.5")
    {
        Info = new Dictionary<string, string> { ["es"] = "Lo decide el sistema.", ["en"] = "The system decides it." },
    };

    public IReadOnlyList<StructuredIssueDatum>? Previewed { get; private set; }

    public Task<StructuredIssuePreview> PreviewAsync(IReadOnlyList<StructuredIssueDatum> data, CancellationToken cancellationToken)
    {
        Previewed = data;
        return Task.FromResult(Preview);
    }
    public StructuredIssueResponse ReversalResult { get; set; } = new(StructuredIssueState.Pending);

    public Task<StructuredIssueResponse> CancelAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (CancelResult.State == StructuredIssueState.Cancelled) ByRun[runId] = CancelResult;
        return Task.FromResult(CancelResult);
    }

    public Task<StructuredIssueResponse> EnqueueReversalAsync(Guid reversalRunId, Guid issuedRunId, CancellationToken cancellationToken)
    {
        ByRun[reversalRunId] = ReversalResult;
        return Task.FromResult(ReversalResult);
    }
}
