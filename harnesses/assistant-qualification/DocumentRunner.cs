using System.Diagnostics;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;

namespace AssistantQualification;

// channel-documents also keeps the copy the reading opened and the document as its owner prepared it, which its interpretation carries.
internal sealed record DocumentObservation(AssistantOperationResponse Result, TurnMetadata Metadata, double ElapsedSeconds,
    IReadOnlyList<CheckEvidence> Checks, MeasurementSnapshot Measurements, string? Key = null, StructuredDocument? Prepared = null);

// One reading of the document-reading set: the owner prepares the public bytes through the real port (as Home and WhatsApp will), the
// copy is written once under the run, and ReadDocument goes through IAssistantOperations with the ledger identity fixed before the submit.
// The candidate profile, the sealed instructions, the quota, the fence and the provider call are the engine's own.
internal sealed class DocumentRunner(IAssistantOperations operations, IStructuredAttachmentReader attachments, IPrivateFileStore files,
    IAiConnectionReader connections, SafeRunLedger ledger, SafeTurnMetadata metadata, QualificationCurrentUser currentUser, TimeProvider clock,
    QualificationMeasurements measurements)
{
    // channelCaseId: a channel-documents case reads its document as WhatsApp does (QualificationChannelDocuments.ReadingContext), with the
    // ledger and evidence of that case (<case>-document); its checks are the same document-reading ones.
    // channelName: forms-documents names every document as the app names one WhatsApp sends without a name.
    public async Task<DocumentObservation> RunAsync(DocumentCase testCase, DocumentFixture fixture, QualificationActor actor, CancellationToken ct,
        string? channelCaseId = null, string? channelName = null)
    {
        if (!QualificationDocuments.IsValid(testCase) || fixture is not { Use: "provider" } || fixture.FileName != testCase.FileName
            || actor.Culture != testCase.Culture || channelName is not null && channelCaseId is null)
            throw new InvalidOperationException("Invalid public document fixture.");
        var bytes = await QualificationDocuments.ReadAsync(testCase, ct);
        // WhatsApp reads the file under the name it gives it: a photo has none and is named by its type.
        var name = channelCaseId is null ? testCase.FileName : channelName ?? QualificationChannelDocuments.ChannelName(testCase.FileName, testCase.ContentType);
        var prepared = await attachments.PrepareAsync(bytes, name, ct);
        if (!QualificationDocuments.PreparedAsExpected(testCase, prepared, name))
            throw new InvalidOperationException("Public document fixture is not prepared as its case expects.");
        var document = prepared.Document!;
        var connection = await connections.FindAsync(ct);
        if (!connection.Enabled || connection.Provider.ToString() != "OpenAI" || connection.Model != "gpt-6.1-sol")
            throw new InvalidOperationException("The existing OpenAI connection is unavailable.");
        currentUser.Set(actor.UserId);
        var context = channelCaseId is null ? QualificationDocuments.Context(actor.UserId, actor.Culture)
            : QualificationChannelDocuments.ReadingContext(actor.UserId, actor.Culture);
        var requestId = Guid.CreateVersion7();
        var entry = channelCaseId is null
            ? new QualificationLedgerEntry(testCase.Id, requestId, actor.UserId, null, null, "before-submit")
                { OperationKind = "document", ChannelId = context.ChannelId, Culture = context.Culture }
            : new QualificationLedgerEntry(channelCaseId + "-document", requestId, actor.UserId, null, null, "before-submit")
                { OperationKind = QualificationChannelDocuments.ReadingOperation, ChannelId = context.ChannelId, Culture = context.Culture,
                    SubjectUserId = actor.UserId, InteractionMode = AssistantInteractionMode.Assistant };
        await ledger.SaveAsync(entry, ct);
        var key = QualificationDocuments.Key(requestId, document.MediaType);
        using (var input = new MemoryStream(bytes, writable: false))
            if (!await files.WriteOnceAsync(key, input, document.MediaType, ct))
                throw new InvalidOperationException("A qualification object already exists; observe its request instead.");
        measurements.BeginCase();
        var elapsed = Stopwatch.StartNew();
        var now = clock.GetUtcNow().UtcDateTime;
        var accepted = await operations.SubmitAsync(new(requestId, context, string.Empty, now, now.AddDays(1))
            { Document = QualificationDocuments.Descriptor(key, document, now.AddDays(1)) }, ct);
        if (accepted.IsFailure) throw QualificationRejections.Stop("document-submit", accepted.Error);
        await ledger.SaveAsync(entry with { Phase = "accepted" }, ct);
        AssistantOperationResponse? result = null;
        var deadline = clock.GetUtcNow().AddSeconds(240);
        while (clock.GetUtcNow() < deadline)
        {
            var observed = await operations.GetAsync(context, requestId, ct);
            if (observed.IsFailure) throw QualificationRejections.Stop("document-status", observed.Error);
            result = observed.Value;
            if (result.State is not ("pending" or "running" or "queued")) break;
            await Task.Delay(TimeSpan.FromSeconds(2), clock, ct);
        }
        if (result is null || result.State is "pending" or "running" or "queued")
            throw new TimeoutException("Retain the accepted document request identity.");
        var technical = await metadata.FindByRequestAsync(actor.UserId, requestId, ct);
        var current = await connections.FindAsync(ct);
        var sameConnection = connection.Provider == current.Provider && connection.Model == current.Model && connection.Revision == current.Revision;
        return new(result, technical, elapsed.Elapsed.TotalSeconds,
            DocumentReadingOracle.Checks(testCase, fixture, result, technical, context.ChannelId, sameConnection), measurements.EndCase(), key, document);
    }
}
