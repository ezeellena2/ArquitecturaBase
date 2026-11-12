using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
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
        Assert.Equal((FormIssueConfirmationPolicy.NoticeId(work.RunId), Author, "ARCA is not responding."), (message.RunId, message.RecipientUserId, message.Body));
    }

    // The notice has an id of its own: a response from a channel, whose «¿La emito?» went by the run, gets it too after «Emitir»,
    // once per run although the run loses its lease and asks again.
    [Fact]
    public async Task The_waiting_notice_reaches_the_author_of_a_response_from_a_channel_once_after_its_question()
    {
        var channel = new KeyedChannel();
        var (service, issuer, work) = Arrange(channel: channel);
        channel.Reachable.Add(Author);
        var fromChannel = work with { RecordEnteredInApp = false };
        await service.ExecuteAsync(fromChannel, CancellationToken.None);
        issuer.Next = new(StructuredIssueState.Pending, WaitingForProvider: true,
            WaitingNotice: new Dictionary<string, string> { ["es"] = "ARCA no responde.", ["en"] = "ARCA is not responding." });
        var confirmed = fromChannel with { DeliveryReview = new StructuredRecordActionReview(1, true, Author, Now), PriorPreview = issuer.Preview };

        await service.ExecuteAsync(confirmed, CancellationToken.None);
        issuer.Current = issuer.Next;
        await service.ExecuteAsync(confirmed with { Fence = 3 }, CancellationToken.None);
        await service.ExecuteAsync(fromChannel with { Fence = 4, ReconciliationOnly = true }, CancellationToken.None);

        Assert.Equal([work.RunId, FormIssueConfirmationPolicy.NoticeId(work.RunId)], channel.Messages.Select(message => message.RunId));
        Assert.Equal("ARCA is not responding.", channel.Messages[1].Body);
        Assert.Null(channel.Messages[1].Choices);
        Assert.Single(issuer.Requests);
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
    public async Task A_response_from_a_channel_asks_its_author_before_issuing_and_issues_once_confirmed()
    {
        var channel = new FakeFormActionChannel();
        var (service, issuer, work) = Arrange(channel: channel);
        channel.Reachable.Add(Author);
        var fromChannel = work with { RecordEnteredInApp = false };

        var waiting = await service.ExecuteAsync(fromChannel, CancellationToken.None);

        Assert.Equal((FormActionRunState.Uncertain, "Forms.Action.AwaitingConfirmation", "Juana Pérez"), (waiting.State, waiting.FailureCode, waiting.IssuePreview!.Receiver));
        var message = Assert.Single(channel.Messages);
        Assert.Equal(["issue", "skip"], message.Choices!.Select(choice => choice.Id));
        Assert.Equal("Invoice C for Juana Pérez, $ 3,101.50.\nShall I issue it?", message.Body);
        Assert.Empty(issuer.Requests);

        await service.ExecuteAsync(fromChannel with { DeliveryReview = new StructuredRecordActionReview(1, true, Author, Now) }, CancellationToken.None);
        Assert.Single(issuer.Requests);
    }

    [Fact]
    public async Task The_question_to_the_author_lists_the_amounts_the_document_itemizes_before_the_total()
    {
        var channel = new FakeFormActionChannel();
        var (service, issuer, work) = Arrange(channel: channel);
        channel.Reachable.Add(Author);
        issuer.Preview = new(new Dictionary<string, string> { ["es"] = "Factura A", ["en"] = "Invoice A" }, "Agro SA", "1210")
        {
            Date = new DateOnly(2026, 11, 9),
            Amounts =
            [
                new(new Dictionary<string, string> { ["es"] = "Importe neto gravado", ["en"] = "Taxed net amount" }, "1000"),
                new(new Dictionary<string, string> { ["es"] = "IVA 21%", ["en"] = "VAT 21%" }, "210"),
            ],
        };

        await service.ExecuteAsync(work with { RecordEnteredInApp = false }, CancellationToken.None);

        Assert.Equal("Invoice A for Agro SA, $ 1,210.00 (Taxed net amount: $ 1,000.00; VAT 21%: $ 210.00), dated 11/09/2026.\nShall I issue it?", Assert.Single(channel.Messages).Body);
    }

    // Decision 4: a summary that fails (the registry does not have the CUIT of a new customer, or does not answer) tells whoever
    // loaded it why, with «Volver a probar» and «No emitir», and the run waits for them as it waits for «¿La emito?».
    [Fact]
    public async Task A_summary_that_fails_tells_its_author_why_with_try_again_and_do_not_issue_and_waits()
    {
        var channel = new KeyedChannel();
        var (service, issuer, work) = Arrange(channel: channel);
        channel.Reachable.Add(Author);
        issuer.Preview = new(null, null, null, "Arca.Registry.PersonNotFound");

        var waiting = await service.ExecuteAsync(work with { RecordEnteredInApp = false }, CancellationToken.None);

        Assert.Equal((FormActionRunState.Uncertain, "Forms.Action.AwaitingConfirmation", "Arca.Registry.PersonNotFound"),
            (waiting.State, waiting.FailureCode, waiting.IssuePreview!.FailureCode));
        var message = Assert.Single(channel.Messages);
        Assert.Equal((work.RunId, "I can't issue it yet: ARCA has no registry data for that CUIT."), (message.RunId, message.Body));
        Assert.Equal(["retry", "skip"], message.Choices!.Select(choice => choice.Id));
        Assert.Empty(issuer.Requests);
    }

    // «Volver a probar» asks for the summary again and goes on as always, with a message of its own (the channel keeps one per id
    // and person, and the first one took the run): «¿La emito?» if it comes out, the same failure if not. The same decision asked
    // again (a run that lost its lease) sends nothing new, and nothing is issued without its «¿La emito?».
    [Fact]
    public async Task Trying_the_summary_again_asks_once_more_with_a_message_of_its_own_and_never_issues_without_asking()
    {
        var channel = new KeyedChannel();
        var (service, issuer, work) = Arrange(channel: channel);
        channel.Reachable.Add(Author);
        var failed = new StructuredIssuePreview(null, null, null, "Arca.Connection.Unavailable");
        var retry = work with
        {
            RecordEnteredInApp = false, DeliveryReview = new StructuredRecordActionReview(2, true, Author, Now), PriorPreview = failed,
        };
        issuer.Preview = failed;

        var again = await service.ExecuteAsync(retry, CancellationToken.None);
        await service.ExecuteAsync(retry with { Fence = 4 }, CancellationToken.None);

        Assert.Equal((FormActionRunState.Uncertain, "Arca.Connection.Unavailable"), (again.State, again.IssuePreview!.FailureCode));
        var failure = Assert.Single(channel.Messages);
        Assert.Equal(FormIssueConfirmationPolicy.MessageId(work.RunId, 2), failure.RunId);
        Assert.Equal("I can't issue it yet: ARCA did not answer. Try again in a few minutes.", failure.Body);

        issuer.Preview = new(new Dictionary<string, string> { ["es"] = "Factura C", ["en"] = "Invoice C" }, "Agro SA", "1210");
        var asked = await service.ExecuteAsync(retry with { DeliveryReview = new StructuredRecordActionReview(5, true, Author, Now) }, CancellationToken.None);

        Assert.Equal((FormActionRunState.Uncertain, "Forms.Action.AwaitingConfirmation", null), (asked.State, asked.FailureCode, asked.IssuePreview!.FailureCode));
        var question = channel.Messages[1];
        Assert.Equal((FormIssueConfirmationPolicy.MessageId(work.RunId, 5), "Invoice C for Agro SA, $ 1,210.00.\nShall I issue it?"), (question.RunId, question.Body));
        Assert.All(question.Choices!, choice => Assert.True(FormIssueConfirmationPolicy.TryReadChoice(question.RunId, choice.Id, out var named, out _) && named == work.RunId));
        Assert.Empty(issuer.Requests);

        await service.ExecuteAsync(retry with { DeliveryReview = new StructuredRecordActionReview(6, true, Author, Now), PriorPreview = issuer.Preview }, CancellationToken.None);
        Assert.Single(issuer.Requests);
        Assert.Equal(2, channel.Messages.Count);
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

    [Fact]
    public async Task A_copy_goes_by_email_to_the_receiver_its_issuer_reads_with_the_document_of_the_issuance_and_its_result()
    {
        var files = new MemoryPrivateFileStore();
        var results = new FormRecordResultReaderDouble();
        var (service, issuer, work) = Arrange(files: files, results: results);
        byte[] pdf = [37, 80, 68, 70];
        await files.WriteOnceAsync("issued/1.pdf", new MemoryStream(pdf), "application/pdf", CancellationToken.None);
        results.Rows.Add(new FormRecordResultRow
        {
            RecordId = work.RecordId, RunId = Guid.NewGuid(), Source = "test.issuer", RecordedAtUtc = Now, SourceNameJson = """{"es":"Emitir"}""",
            ValuesJson = FormDraftPolicy.Serialize(new[] { new StructuredRecordResultValue("number", Names("Número"), StructuredRecordResultField.Text, "0004-00000012") }),
        });
        issuer.Copy = "cliente@example.com";

        var outcome = await service.CopyAsync(Copy(work, pdf), CancellationToken.None);

        Assert.Equal(FormActionRunState.Completed, outcome.State);
        var email = outcome.Email!;
        Assert.Equal(("cliente@example.com", "Factura C 0004-00000012", "Te mandamos la 0004-00000012.\nGracias."),
            (email.To, email.Subject, email.TextBody));
        Assert.Equal("<p>Te mandamos la 0004-00000012.<br>Gracias.</p>", email.HtmlBody);
        var attachment = Assert.Single(email.Attachments!);
        Assert.Equal(("Factura C 0004-00000012.pdf", "application/pdf"), (attachment.Name, attachment.ContentType));
        Assert.Equal(pdf, attachment.Content);
        Assert.Equal(["customer", "items", "total"], issuer.CopyData!.Select(datum => datum.Key));
        Assert.Empty(issuer.Requests);
    }

    [Fact]
    public async Task A_copy_without_a_receiver_or_without_the_document_of_the_issuance_does_not_apply_and_a_changed_document_is_not_sent()
    {
        var files = new MemoryPrivateFileStore();
        var (service, issuer, work) = Arrange(files: files);
        byte[] pdf = [37, 80, 68, 70];
        await files.WriteOnceAsync("issued/1.pdf", new MemoryStream(pdf), "application/pdf", CancellationToken.None);

        var nowhere = await service.CopyAsync(Copy(work, pdf), CancellationToken.None);
        Assert.Equal(FormActionRunState.NotApplicable, nowhere.State);
        Assert.Null(nowhere.Email);
        issuer.Copy = "cliente@example.com";
        Assert.Equal(FormActionRunState.NotApplicable, (await service.CopyAsync(Copy(work, pdf) with { PdfArtifacts = [] }, CancellationToken.None)).State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CopyAsync(Copy(work, [37, 80, 68, 71]), CancellationToken.None));
        Assert.Equal(FormActionErrors.IssuerUnavailable.Code,
            (await service.CopyAsync(Copy(work, pdf) with { Snapshot = work.Snapshot with { IssuerId = "other" } }, CancellationToken.None)).FailureCode);
    }

    // The copy run of a rule after its issuance: its text, with the datum of the result still marked, and the fiscal PDF it attaches.
    private static FormActionWorkResponse Copy(FormActionWorkResponse work, byte[] pdf)
    {
        var artifact = new FormPdfArtifact("es", "issued/1.pdf", "Factura C 0004-00000012.pdf", "application/pdf", pdf.Length,
            Convert.ToHexStringLower(SHA256.HashData(pdf)));
        return work with
        {
            Kind = "SendCopy", PdfArtifacts = [artifact, artifact with { Culture = "en" }],
            Snapshot = work.Snapshot with { CopyBody = "Te mandamos la " + FormActionResultTokenPolicy.Mark("result:test.issuer.number") + ".\nGracias." },
        };
    }

    private static readonly Guid Author = Guid.NewGuid();

    // The issuance and its copy by email, composed as the module registers them.
    private sealed class Issue(FormIssueActionService action, FormIssueCopyService copy)
    {
        public Task<FormActionOutcomeResponse> ExecuteAsync(FormActionWorkResponse work, CancellationToken cancellationToken) => action.ExecuteAsync(work, cancellationToken);
        public Task<FormActionOutcomeResponse> CopyAsync(FormActionWorkResponse work, CancellationToken cancellationToken) => copy.CopyAsync(work, cancellationToken);
    }

    private static (Issue Service, FakeIssuer Issuer, FormActionWorkResponse Work) Arrange(string standardKey = "test.standard",
        IStructuredRecordActionChannel? channel = null, MemoryPrivateFileStore? files = null, FormRecordResultReaderDouble? results = null)
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
        var requests = new FormIssueRequestPolicy(records, rules, new NoCodes());
        var messenger = new FormIssueMessageIssuer(records, channel is null ? [] : [channel], new Recipients());
        return (new Issue(new FormIssueActionService([issuer], requests, messenger),
            new FormIssueCopyService([issuer], requests, files ?? new MemoryPrivateFileStore(), results ?? new FormRecordResultReaderDouble())), issuer, work);
    }

    private static CanonicalFormField Field(Guid id, string key, string type) => new(id, key, type, Names(key), null, false, null, null);

    private static Dictionary<string, string> Names(string text) => new() { ["es"] = text, ["en"] = text };

    private sealed class NoCodes : IFormCatalogItemCodeReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> ListAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyDictionary<string, Guid>> ListActiveAsync(Guid catalogId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, Guid>>(new Dictionary<string, Guid>());
    }

    // A channel that keeps one message per id and person, as the installed ones do.
    private sealed class KeyedChannel : IStructuredRecordActionChannel
    {
        private readonly Dictionary<(Guid, Guid), StructuredRecordActionMessage> _sent = [];
        public HashSet<Guid> Reachable { get; } = [];
        public List<StructuredRecordActionMessage> Messages { get; } = [];
        public string ChannelId => "phone";
        public Task<bool> CanReachAsync(Guid recipientUserId, CancellationToken cancellationToken) => Task.FromResult(Reachable.Contains(recipientUserId));
        public Task<StructuredRecordActionResponse> GetAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken) =>
            Task.FromResult(new StructuredRecordActionResponse(_sent.ContainsKey((runId, recipientUserId)) ? StructuredRecordActionState.Accepted : StructuredRecordActionState.NotFound));
        public Task<StructuredRecordActionResponse> EnqueueAsync(StructuredRecordActionMessage request, CancellationToken cancellationToken)
        {
            if (_sent.TryAdd((request.RunId, request.RecipientUserId), request)) Messages.Add(request);
            return Task.FromResult(new StructuredRecordActionResponse(StructuredRecordActionState.Accepted));
        }
        public Task<IReadOnlyList<StructuredRecordActionObservationResponse>> ObserveAsync(IReadOnlyCollection<StructuredRecordActionObservationRequest> requests,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredRecordActionResponse> ReviewAsync(Guid runId, Guid recipientUserId, StructuredRecordActionReview review, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<StructuredRecordActionResponse> RetryAsync(Guid runId, Guid recipientUserId, long controlRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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
        public StructuredIssuePreview Preview { get; set; } = new(new Dictionary<string, string> { ["es"] = "Factura C", ["en"] = "Invoice C" }, "Juana Pérez", "3101.5");
        public string? Copy { get; set; }
        public IReadOnlyList<StructuredIssueDatum>? CopyData { get; private set; }
        public string? CopyRecipient(IReadOnlyList<StructuredIssueDatum> data)
        {
            CopyData = data;
            return Copy;
        }
        public Task<StructuredIssuePreview> PreviewAsync(IReadOnlyList<StructuredIssueDatum> data, CancellationToken cancellationToken) => Task.FromResult(Preview);
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
