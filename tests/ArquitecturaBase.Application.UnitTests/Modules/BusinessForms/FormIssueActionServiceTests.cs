using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormIssueActionServiceTests
{
    private static readonly Guid CustomerId = Guid.NewGuid(), GroupId = Guid.NewGuid(), DescriptionId = Guid.NewGuid(), QuantityId = Guid.NewGuid(),
        TotalId = Guid.NewGuid(), NotesId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_standing_response_hands_its_bound_data_to_the_issuer_once_and_waits_on_its_schedule()
    {
        var (service, issuer, work) = Arrange();
        issuer.Next = new(StructuredIssueState.Pending, NextCheckAtUtc: Now.AddMinutes(5));

        var outcome = await service.ExecuteAsync(work, CancellationToken.None);

        Assert.Equal((FormActionRunState.Running, Now.AddMinutes(5)), (outcome.State, outcome.Issue!.NextCheckAtUtc));
        var request = Assert.Single(issuer.Requests);
        Assert.Equal(work.RunId, request.RunId);
        Assert.Equal(["customer", "items", "total"], request.Data.Select(datum => datum.Key));
        Assert.Equal(("Juana Pérez", "3101.5"), (request.Data[0].Value, request.Data[2].Value));
        Assert.Equal([[("description", "Horas"), ("quantity", "2")], [("description", "Viático"), ("quantity", "1")]],
            request.Data[1].Items!.Select(item => item.Select(datum => (datum.Key, datum.Value!)).ToArray()).ToArray());

        issuer.Current = issuer.Next;
        await service.ExecuteAsync(work with { ReconciliationOnly = true }, CancellationToken.None);
        Assert.Single(issuer.Requests);
    }

    [Fact]
    public async Task An_issuance_waiting_for_its_provider_tells_whoever_loaded_it_once_in_their_language()
    {
        var channel = new FakeFormActionChannel();
        var (service, issuer, work) = Arrange(channel: channel);
        channel.Reachable.Add(Author);
        issuer.Next = new(StructuredIssueState.Pending, WaitingForProvider: true,
            WaitingNotice: new Dictionary<string, string> { ["es"] = "ARCA no responde.", ["en"] = "ARCA is not responding." });

        await service.ExecuteAsync(work, CancellationToken.None);
        issuer.Current = issuer.Next;
        await service.ExecuteAsync(work with { ReconciliationOnly = true }, CancellationToken.None);

        var message = Assert.Single(channel.Messages);
        Assert.Equal((work.RunId, Author, "ARCA is not responding."), (message.RunId, message.RecipientUserId, message.Body));
    }

    [Fact]
    public async Task Without_a_channel_that_reaches_whoever_loaded_it_the_wait_goes_without_notice()
    {
        var channel = new FakeFormActionChannel();
        var (service, issuer, work) = Arrange(channel: channel);
        issuer.Next = new(StructuredIssueState.Pending, WaitingForProvider: true, WaitingNotice: Names("ARCA no responde."));

        Assert.Equal(FormActionRunState.Running, (await service.ExecuteAsync(work, CancellationToken.None)).State);
        Assert.Empty(channel.Messages);
    }

    [Fact]
    public async Task A_response_voided_or_corrected_before_it_was_queued_does_not_issue()
    {
        var (service, issuer, work) = Arrange();

        var outcome = await service.ExecuteAsync(work with { RecordStands = false }, CancellationToken.None);

        Assert.Equal(FormActionRunState.NotApplicable, outcome.State);
        Assert.Empty(issuer.Requests);
    }

    [Fact]
    public async Task An_issued_document_completes_with_its_result_and_a_refusal_fails_with_its_code()
    {
        var (service, issuer, work) = Arrange();
        var result = new StructuredRecordResult("test.issuer", Names("Comprobante"), []);
        issuer.Next = new(StructuredIssueState.Issued, Result: result);
        Assert.Same(result, (await service.ExecuteAsync(work, CancellationToken.None)).Issue!.Result);

        var (refusing, refuser, refused) = Arrange();
        refuser.Next = new(StructuredIssueState.Rejected, "Arca.Invoicing.TotalMismatch", CanRetry: true);
        var outcome = await refusing.ExecuteAsync(refused, CancellationToken.None);
        Assert.Equal((FormActionRunState.Failed, "Arca.Invoicing.TotalMismatch", true), (outcome.State, outcome.FailureCode, outcome.CanRetry));
    }

    [Fact]
    public async Task An_issuance_in_review_waits_for_a_person_and_takes_the_decision_on_the_revision_the_run_saw()
    {
        var (service, issuer, work) = Arrange();
        issuer.Current = new(StructuredIssueState.NeedsReview, ReviewReason: "test.issuer.dateOutOfRange", Revision: 7);
        Assert.Equal((FormActionRunState.Uncertain, "test.issuer.dateOutOfRange"),
            ((await service.ExecuteAsync(work, CancellationToken.None)).State, (await service.ExecuteAsync(work, CancellationToken.None)).FailureCode));

        issuer.Next = new(StructuredIssueState.Pending);
        var reviewer = Guid.NewGuid();
        var decided = await service.ExecuteAsync(work with
        {
            DeliveryReview = new StructuredRecordActionReview(1, true, reviewer, Now),
            PriorIssue = new(StructuredIssueState.NeedsReview, Revision: 6),
        }, CancellationToken.None);

        Assert.Equal(FormActionRunState.Running, decided.State);
        Assert.Equal((6L, true, reviewer), (issuer.Reviews.Single().ExpectedRevision, issuer.Reviews.Single().Proceed, issuer.Reviews.Single().ReviewedByUserId));
        Assert.Empty(issuer.Requests);
    }

    [Fact]
    public async Task A_reversal_of_what_was_never_issued_does_not_apply_and_one_without_a_number_is_cancelled()
    {
        var (service, issuer, work) = Arrange();
        var issuedRunId = Guid.NewGuid();
        var reversal = work with { Snapshot = work.Snapshot with { ReversesRunId = issuedRunId } };

        Assert.Equal(FormActionRunState.NotApplicable, (await service.ExecuteAsync(reversal, CancellationToken.None)).State);

        issuer.ByRun[issuedRunId] = new(StructuredIssueState.Pending);
        Assert.Equal(FormActionRunState.NotApplicable, (await service.ExecuteAsync(reversal, CancellationToken.None)).State);
        Assert.Equal([issuedRunId], issuer.Cancels);

        issuer.CancelResult = new(StructuredIssueState.Pending, NextCheckAtUtc: Now.AddMinutes(2));
        var waiting = await service.ExecuteAsync(reversal, CancellationToken.None);
        Assert.Equal((FormActionRunState.Running, Now.AddMinutes(2)), (waiting.State, waiting.Issue!.NextCheckAtUtc));
        Assert.Empty(issuer.Reversals);
    }

    [Fact]
    public async Task A_reversal_of_an_issued_document_queues_its_credit_note_once_and_completes_with_its_result()
    {
        var (service, issuer, work) = Arrange();
        var issuedRunId = Guid.NewGuid();
        var reversal = work with { Snapshot = work.Snapshot with { ReversesRunId = issuedRunId } };
        issuer.ByRun[issuedRunId] = new(StructuredIssueState.Issued);
        issuer.Next = new(StructuredIssueState.Pending);

        Assert.Equal(FormActionRunState.Running, (await service.ExecuteAsync(reversal, CancellationToken.None)).State);
        Assert.Equal([(work.RunId, issuedRunId)], issuer.Reversals);

        var result = new StructuredRecordResult("test.issuer", Names("Nota de crédito"), []);
        issuer.ByRun[work.RunId] = new(StructuredIssueState.Issued, Result: result);
        var done = await service.ExecuteAsync(reversal, CancellationToken.None);
        Assert.Equal((FormActionRunState.Completed, result), (done.State, done.Issue!.Result));
        Assert.Single(issuer.Reversals);
        Assert.Empty(issuer.Requests);
    }

    [Fact]
    public async Task Without_its_issuer_or_its_standard_the_issuance_fails()
    {
        var (service, _, work) = Arrange();
        Assert.Equal(FormActionErrors.IssuerUnavailable.Code, (await service.ExecuteAsync(work with { Snapshot = work.Snapshot with { IssuerId = "other" } }, CancellationToken.None)).FailureCode);

        var (unbound, _, unboundWork) = Arrange(standardKey: "another.standard");
        Assert.Equal(FormActionErrors.IssueDataUnavailable.Code, (await unbound.ExecuteAsync(unboundWork, CancellationToken.None)).FailureCode);
    }

    private static readonly Guid Author = Guid.NewGuid();

    private static (FormIssueActionService Service, FakeIssuer Issuer, FormActionWorkResponse Work) Arrange(string standardKey = "test.standard",
        FakeFormActionChannel? channel = null)
    {
        var rules = new FormRuleFixture();
        rules.Definition.RecordStandard(standardKey, 1, JsonSerializer.Serialize(new Dictionary<string, Guid>
        {
            ["customer"] = CustomerId, ["items"] = GroupId, ["description"] = DescriptionId, ["quantity"] = QuantityId, ["total"] = TotalId,
        }));
        var recordId = Guid.NewGuid();
        var schema = new FormVersionSchema(1,
        [
            Field(CustomerId, "customer", "shortText"), Field(NotesId, "notes", "shortText"),
            Field(GroupId, "items", "group"), Field(DescriptionId, "description", "shortText") with { Repeated = true },
            Field(QuantityId, "quantity", "decimal") with { Repeated = true }, Field(TotalId, "total", "decimal"),
        ]);
        var values = JsonSerializer.Serialize(new object[]
        {
            new { fieldId = CustomerId, value = "Juana Pérez" }, new { fieldId = NotesId, value = "Sin datos" },
            new { fieldId = GroupId, value = new[]
            {
                new object[] { new { fieldId = DescriptionId, value = "Horas" }, new { fieldId = QuantityId, value = 2m } },
                new object[] { new { fieldId = DescriptionId, value = "Viático" }, new { fieldId = QuantityId, value = 1m } },
            } },
            new { fieldId = TotalId, value = 3101.5m },
        });
        var row = new FormRecordStoredRow(Guid.NewGuid(), recordId, 1, rules.Definition.Id, "FAC", Guid.NewGuid(), 1, "{\"es\":\"Factura\"}",
            JsonSerializer.Serialize(schema, JsonSerializerOptions.Web), values, []);
        var records = new FakeFormResponseReader { ExportRows = [new FormResponseStoredRow(row, Now, new FormAuthorResponse(Author, "Ana"))] };
        var issuer = new FakeIssuer();
        var work = new FormActionWorkResponse(Guid.NewGuid(), recordId, "IssueDocument", 1, false, false,
            new FormActionSnapshot(null, null, null, [], IssuerId: "test.issuer"), []);
        return (new FormIssueActionService([issuer], records, rules, channel is null ? [] : [channel], new Recipients(), new NoCodes()), issuer, work);
    }

    private static CanonicalFormField Field(Guid id, string key, string type) => new(id, key, type, Names(key), null, false, null, null);

    private static Dictionary<string, string> Names(string text) => new() { ["es"] = text, ["en"] = text };

    private sealed class NoCodes : IFormCatalogItemCodeReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> ListAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class Recipients : IFormActionRecipientReader
    {
        public Task<FormActionRecipientRow?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<FormActionRecipientRow?>(new(userId, "en", "Ana"));
        public Task<IReadOnlyList<FormActionRecipientRow>> ListForRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FormActionRecipientRow>>([]);
    }

    private sealed class FakeIssuer : IStructuredRecordIssuer
    {
        public StructuredIssueResponse Current { get; set; } = new(StructuredIssueState.NotFound);
        public StructuredIssueResponse Next { get; set; } = new(StructuredIssueState.Pending);
        public List<StructuredIssueRequest> Requests { get; } = [];
        public List<StructuredIssueReview> Reviews { get; } = [];
        public string IssuerId => "test.issuer";
        public IReadOnlyDictionary<string, string> Name => Names("Emitir");
        public string StandardKey => "test.standard";
        public string ReviewPermission => "test.issue";
        public IReadOnlyList<StructuredRecordResultField> ResultFields => [];
        public Task<StructuredIssueResponse> EnqueueAsync(StructuredIssueRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Next);
        }
        public Task<StructuredIssueResponse> GetAsync(Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult(ByRun.TryGetValue(runId, out var byRun) ? byRun : Current);
        public Task<StructuredIssueResponse> ReviewAsync(Guid runId, StructuredIssueReview review, CancellationToken cancellationToken)
        {
            Reviews.Add(review);
            return Task.FromResult(Next);
        }
        public List<Guid> Cancels { get; } = [];
        public StructuredIssueResponse CancelResult { get; set; } = new(StructuredIssueState.Cancelled);
        public Dictionary<Guid, StructuredIssueResponse> ByRun { get; } = [];
        public List<(Guid Reversal, Guid Issued)> Reversals { get; } = [];
        public Task<StructuredIssueResponse> CancelAsync(Guid runId, CancellationToken cancellationToken)
        {
            Cancels.Add(runId);
            return Task.FromResult(CancelResult);
        }
        public Task<StructuredIssueResponse> EnqueueReversalAsync(Guid reversalRunId, Guid issuedRunId, CancellationToken cancellationToken)
        {
            Reversals.Add((reversalRunId, issuedRunId));
            return Task.FromResult(Next);
        }
    }
}
