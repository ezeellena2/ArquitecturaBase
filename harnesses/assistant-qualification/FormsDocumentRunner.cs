using System.Diagnostics;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;

namespace AssistantQualification;

internal sealed record FormsDocumentObservation(Guid RequestId, AssistantOperationResponse Result, TurnMetadata Metadata, double ElapsedSeconds,
    IReadOnlyList<CheckEvidence> Checks, MeasurementSnapshot Measurements);

// forms-documents (task 17b): «Armar formulario con la IA» as FormDocumentAuthoringService submits it. The reviewer asks with the context the
// inbox seals (a copy of FormDocumentAuthoringPolicy.Context), the request's text and the saved readings of up to five documents, the first
// one with its copy; nothing reads a document again. The proposal is the one the page shows. Payload facts: AuthoringOracle.FormsDocumentPayloadChecks.
internal sealed class FormsDocumentRunner(IAssistantOperations operations, IStructuredAttachmentReader attachments, SafeRunLedger ledger,
    SafeTurnMetadata metadata, QualificationCurrentUser currentUser, TimeProvider clock, QualificationMeasurements measurements,
    IEnumerable<IAssistantCapabilitySource> sources, IUserReader users)
{
    // A Word of the inbox carries what its owner reads on the server (WhatsAppReceivedDocumentPreparationPolicy): no model reads it, so it is
    // prepared again, under the name the app gives it, each time a case attaches it.
    public async Task<FormsSavedReading> PrepareWordAsync(HomeDocumentFixture fixture, DocumentFixture printed, CancellationToken ct)
    {
        if (!QualificationHomeDocuments.IsWord(fixture) || fixture.Inherited) throw new InvalidOperationException("Only a Word is read on the server.");
        var bytes = await QualificationHomeDocuments.ReadAsync(fixture, printed, ct);
        var prepared = await attachments.PrepareAsync(bytes, QualificationFormsDocuments.ChannelName(fixture.ContentType), ct);
        return WordReading(fixture, prepared) ?? throw new InvalidOperationException("Public Word fixture is not read on the server as its case expects.");
    }

    // The Word as its owner prepared it: whole, readable, under the name the app gives it and with the bytes the case pins; null otherwise.
    internal static FormsSavedReading? WordReading(HomeDocumentFixture fixture, StructuredAttachmentPreparation prepared) =>
        prepared is { Rejection: null, Document: { Kind: StructuredDocumentKind.Docx, Reading: { Readability: StructuredDocumentReadability.Readable, Truncated: false } } document }
        && document.FileName == QualificationFormsDocuments.ChannelName(fixture.ContentType) && document.Sha256 == fixture.Sha256 && document.Size == fixture.ByteCount
            ? new(null, document) : null;

    public async Task<FormsDocumentObservation> RunAsync(FormsDocumentCase testCase, QualificationActor reviewer, IReadOnlyList<FormsSavedReading> readings,
        VerifiedFormsDocumentSet set, QualificationChannelFixture forms, IReadOnlySet<Guid> earlierConversations, CancellationToken ct)
    {
        if (!QualificationFormsDocuments.IsValid(testCase) || reviewer.Culture != testCase.Culture || readings.Count != testCase.Documents.Length
            || readings[0].Key is null || readings.Where((reading, index) => reading.Document.Sha256 != testCase.Documents[index].Sha256
                || reading.Document.Reading is null || reading.Document.FileName != QualificationFormsDocuments.ChannelName(testCase.Documents[index].ContentType)).Any())
            throw new InvalidOperationException("A forms-documents case needs exactly the saved reading of each of its documents.");
        // The inbox seals the reviewer's own zone; the fixture accounts keep the default one the copy names.
        var account = await users.FindByIdAsync(reviewer.UserId, ct) ?? throw new InvalidOperationException("Unknown forms-documents reviewer.");
        if (account.TimeZoneId != QualificationFormsDocuments.TimeZoneId) throw new InvalidOperationException("The reviewer's zone is not the copy's.");
        currentUser.Set(reviewer.UserId);
        var requestId = Guid.CreateVersion7();
        var context = QualificationFormsDocuments.Context(reviewer.UserId, testCase.Culture, requestId);
        var entry = new QualificationLedgerEntry(testCase.Id, requestId, reviewer.UserId, null, null, "before-submit")
            { OperationKind = QualificationFormsDocuments.Operation, ChannelId = context.ChannelId, Culture = context.Culture };
        await ledger.SaveAsync(entry, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var expiresAtUtc = now.AddDays(QualificationFormsDocuments.ContentDays);
        var attachment = QualificationFormsDocuments.Attachment(readings[0].Key!, readings.Select(reading => reading.Document).ToArray(), expiresAtUtc);
        measurements.BeginCase();
        var elapsed = Stopwatch.StartNew();
        var accepted = await operations.SubmitAsync(new(requestId, context, QualificationFormsDocuments.RequestText(testCase.Culture), now, expiresAtUtc)
            { Attachment = attachment }, ct);
        if (accepted.IsFailure) throw QualificationRejections.Stop("forms-documents-submit", accepted.Error);
        await ledger.SaveAsync(entry with { Phase = "accepted" }, ct);
        AssistantOperationResponse? outcome = null;
        var deadline = clock.GetUtcNow().AddSeconds(240);
        while (clock.GetUtcNow() < deadline)
        {
            var read = await operations.GetAsync(context, requestId, ct);
            if (read.IsFailure) throw QualificationRejections.Stop("forms-documents-status", read.Error);
            outcome = read.Value;
            if (outcome.State is not ("pending" or "running" or "queued")) break;
            await Task.Delay(TimeSpan.FromSeconds(2), clock, ct);
        }
        if (outcome is null || outcome.State is "pending" or "running" or "queued") throw new TimeoutException("Retain the accepted forms-documents request identity.");
        var technical = await metadata.FindByRequestAsync(reviewer.UserId, requestId, ct);
        var proposals = sources.SelectMany(source => source.Definitions).Where(definition => definition.Kind == AssistantCapabilityKind.Proposal)
            .Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
        var checks = TurnChecks(testCase, context, outcome, technical, readings, set, forms, proposals, earlierConversations);
        return new(requestId, outcome, technical, elapsed.Elapsed.TotalSeconds, checks, measurements.EndCase());
    }

    // The turn as the inbox would read it: completed in the channel, within the engine's bounds, with the sealed context the copy builds, a
    // conversation of its own, the first document's copy, and the form the page shows resting on reads of the same turn, recovered after a
    // rejection and alone in its turn; then what «Crear borrador» would keep of it.
    internal static IReadOnlyList<CheckEvidence> TurnChecks(FormsDocumentCase testCase, AssistantChannelContext context, AssistantOperationResponse outcome,
        TurnMetadata technical, IReadOnlyList<FormsSavedReading> readings, VerifiedFormsDocumentSet set, QualificationChannelFixture forms,
        IReadOnlySet<string> proposalCapabilities, IReadOnlySet<Guid> earlierConversations)
    {
        var limits = new AiAssistantLimits();
        var observed = technical.Capabilities.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var proposal = QualificationFormsDocuments.Proposal(outcome);
        var first = readings[0].Document;
        var checks = new List<CheckEvidence>
        {
            new("completed", outcome.State == "completed"),
            new("real-channel-provenance", outcome.Provenance?.ChannelId == QualificationFormsDocuments.ChannelId),
            new("required-tools-observed", testCase.RequiredTools.All(observed.Contains)),
            new("no-home-tools", !technical.Capabilities.Any(item => QualificationSeed.HomeCapabilities.Contains(item.Id))),
            new("bounded-provider-steps", technical.ProviderSteps >= 1 && technical.ProviderSteps <= limits.ProviderSteps),
            new("bounded-tool-calls", technical.ToolCalls <= limits.ToolCalls),
            new("bounded-tool-bytes", technical.ToolBytes <= limits.ToolAggregateBytes && technical.Capabilities.All(item => item.Bytes <= limits.ToolResultBytes)),
            new("inbox-context-matches-the-app", technical.Stamps.ChannelId == QualificationFormsDocuments.ChannelId
                && technical.OfferedReferences.SequenceEqual(context.AllowedCapabilities) && technical.PreloadedReferences.Count == 0
                && technical.Terminals.SequenceEqual(context.AllowedTerminals) && technical.FocusDefinitionId is null
                && technical.InteractionMode == AssistantInteractionMode.Unspecified && technical.SubjectUserId is null),
            new("request-in-its-own-conversation", technical.ConversationId != Guid.Empty && !earlierConversations.Contains(technical.ConversationId)),
            new("turn-carried-the-first-reading", technical.AttachmentKey == readings[0].Key && technical.AttachmentSha256 == first.Sha256
                && technical.AttachmentSize == first.Size),
            new("proposal-is-the-form-the-page-shows", outcome.State == "completed" && proposal is not null && outcome.Proposal is not null
                && observed.Contains(outcome.Proposal.Capability.Id)),
            new("authoring-reads-before-proposal", AuthoringOracle.ReadsBeforeProposal(proposal, testCase.RequiredTools)),
            new("rejection-recovered-if-any", AuthoringOracle.RejectionsRecovered(technical.Capabilities, AuthoringOracle.ProposeForm, AuthoringOracle.DocumentFormVersion)),
            new("only-expected-proposal-capability", AuthoringOracle.OnlyProposalsOf(technical.Capabilities, proposalCapabilities, AuthoringOracle.ProposeForm)),
        };
        checks.AddRange(AuthoringOracle.FormsDocumentPayloadChecks(testCase.Expected, testCase.Culture, proposal, forms,
            [..testCase.Documents.Select(set.Document)]));
        return checks;
    }
}
