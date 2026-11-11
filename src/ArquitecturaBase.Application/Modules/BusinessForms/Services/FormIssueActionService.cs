using System.Net;
using System.Security.Cryptography;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The issuer is asked first: enqueueing is idempotent by run and local to the issuer, so a run that lost its outcome asks again
/// without issuing twice. Only a response that still stands is queued; once queued, it is followed to its end. While it waits
/// for its provider, whoever loaded the response is told once, by the first channel that reaches them: the channel keeps one
/// message per id and person, and the run is the id. One from a channel asks its author first («¿La emito?»), also after a
/// summary that failed. The copy by email («SendCopy») goes where the issuer says, with the document of the issuance.
/// </summary>
internal sealed class FormIssueActionService(IEnumerable<IStructuredRecordIssuer> issuers, IFormResponseReader records, IFormRuleReader definitions,
    IEnumerable<IStructuredRecordActionChannel> channels, IFormActionRecipientReader recipients, IFormCatalogItemCodeReader codes,
    IPrivateFileStore files, IFormRecordResultReader results) : IFormIssueActionService
{
    public async Task<FormActionOutcomeResponse> ExecuteAsync(FormActionWorkResponse work, CancellationToken cancellationToken)
    {
        if (issuers.FirstOrDefault(issuer => issuer.IssuerId == work.Snapshot.IssuerId) is not { } issuer)
            return new(FormActionRunState.Failed, FormActionErrors.IssuerUnavailable.Code, true);
        if (work.Snapshot.ReversesRunId is { } issuedRunId) return await ReverseAsync(work, issuer, issuedRunId, cancellationToken);
        var status = await issuer.GetAsync(work.RunId, cancellationToken);
        if (status.State == StructuredIssueState.NotFound)
        {
            if (!work.RecordStands) return new(FormActionRunState.NotApplicable);
            if (await RequestAsync(work, issuer, cancellationToken) is not var (request, _))
                return new(FormActionRunState.Failed, FormActionErrors.IssueDataUnavailable.Code);
            // EM2-A: one loaded in the app was confirmed before sending; one from a channel asks its author first, and again after
            // «Volver a probar» on a summary that failed (decision 4), with a message of its own for that decision.
            if (!work.RecordEnteredInApp && work.DeliveryReview?.WasSent != true)
                return await AskAsync(work, issuer, request, work.RunId, cancellationToken);
            if (!work.RecordEnteredInApp && work.PriorPreview?.FailureCode is not null)
                return await AskAsync(work, issuer, request, FormIssueConfirmationPolicy.MessageId(work.RunId, work.DeliveryReview!.ExpectedRevision), cancellationToken);
            status = await issuer.EnqueueAsync(request, cancellationToken);
        }
        // The person decided on the issuance as the run last saw it: a newer revision makes the issuer refuse and ask again.
        if (status.State == StructuredIssueState.NeedsReview && work.DeliveryReview is { } review)
            status = await issuer.ReviewAsync(work.RunId, new StructuredIssueReview(work.PriorIssue?.Revision ?? status.Revision, review.WasSent,
                review.ReviewedByUserId, review.ReviewedAtUtc), cancellationToken);
        if (status is { State: StructuredIssueState.Pending, WaitingForProvider: true, WaitingNotice: { } notice })
            await NoticeAsync(work, notice, cancellationToken);
        return Outcome(status);
    }

    // The copy goes to the receiver the issuer reads from the bound data, in the language of the form, with the document of the
    // issuance of its rule attached; without a receiver or a document it does not apply. Nothing leaves here: the email is queued
    // with the confirmation of the run, so it is queued once.
    public async Task<FormActionOutcomeResponse> CopyAsync(FormActionWorkResponse work, CancellationToken cancellationToken)
    {
        if (issuers.FirstOrDefault(issuer => issuer.IssuerId == work.Snapshot.IssuerId) is not { } issuer)
            return new(FormActionRunState.Failed, FormActionErrors.IssuerUnavailable.Code, true);
        if (work.PdfArtifacts.Count == 0) return new(FormActionRunState.NotApplicable);
        var document = work.PdfArtifacts[0];
        if (await RequestAsync(work, issuer, cancellationToken) is not var (request, culture))
            return new(FormActionRunState.Failed, FormActionErrors.IssueDataUnavailable.Code);
        if (issuer.CopyRecipient(request.Data) is not { } address) return new(FormActionRunState.NotApplicable);
        var content = await ReadAsync(document, cancellationToken);
        var body = FormActionResultTokenPolicy.Resolve(work.Snapshot.CopyBody ?? throw new InvalidOperationException("The copy text is missing."),
            await results.ListAsync([work.RecordId], cancellationToken), culture);
        return new(FormActionRunState.Completed)
        {
            Email = new EmailMessage(address, Path.GetFileNameWithoutExtension(document.Name),
                "<p>" + WebUtility.HtmlEncode(body).Replace("\n", "<br>", StringComparison.Ordinal) + "</p>", body)
            {
                Attachments = [new EmailAttachment(document.Name, document.ContentType, content)],
            },
        };
    }

    // The document as it was confirmed: its size and its SHA-256.
    private async Task<byte[]> ReadAsync(FormPdfArtifact document, CancellationToken cancellationToken)
    {
        await using var stream = await files.OpenReadAsync(document.StorageKey, cancellationToken)
            ?? throw new InvalidOperationException("The document of the issuance is missing from the private store.");
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        var content = copy.ToArray();
        if (content.LongLength != document.Size || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(content)), document.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The document of the issuance does not match what was confirmed.");
        return content;
    }

    // NC1-A: what was never issued is cancelled or left alone, and the response is voided; what was issued is reversed for its
    // whole amount, and the response is voided when the reversal is issued. One with a number still in flight waits for its end.
    private static async Task<FormActionOutcomeResponse> ReverseAsync(FormActionWorkResponse work, IStructuredRecordIssuer issuer, Guid issuedRunId,
        CancellationToken cancellationToken)
    {
        var issued = await issuer.GetAsync(issuedRunId, cancellationToken);
        if (issued.State is StructuredIssueState.Pending or StructuredIssueState.NeedsReview)
            issued = await issuer.CancelAsync(issuedRunId, cancellationToken);
        switch (issued.State)
        {
            case StructuredIssueState.NotFound or StructuredIssueState.Rejected or StructuredIssueState.Cancelled:
                return new(FormActionRunState.NotApplicable);
            case StructuredIssueState.Pending or StructuredIssueState.NeedsReview:
                return new(FormActionRunState.Running, Issue: issued with { Result = null, Document = null, WaitingNotice = null });
        }
        var reversal = await issuer.GetAsync(work.RunId, cancellationToken);
        if (reversal.State == StructuredIssueState.NotFound)
            reversal = await issuer.EnqueueReversalAsync(work.RunId, issuedRunId, cancellationToken);
        return Outcome(reversal with { WaitingNotice = null });
    }

    // The summary goes to whoever loaded it with «Emitir» and «No emitir»; one that failed (a new customer the registry does not
    // have, or a registry that does not answer), with its reason, «Volver a probar» and «No emitir». Each goes once: the channel
    // keeps one message per id and person, and each decision has its own id. The run waits, uncertain, until that person decides.
    private async Task<FormActionOutcomeResponse> AskAsync(FormActionWorkResponse work, IStructuredRecordIssuer issuer, StructuredIssueRequest request,
        Guid messageId, CancellationToken cancellationToken)
    {
        var preview = await issuer.PreviewAsync(request.Data, cancellationToken);
        var failed = preview.FailureCode is not null;
        var record = await records.FindRecordAsync(work.RecordId, cancellationToken) ?? throw new InvalidOperationException("The action response is missing.");
        var author = record.Actor.UserId;
        var culture = (await recipients.FindAsync(author, cancellationToken))?.Culture is "en" ? "en" : "es";
        foreach (var channel in channels)
        {
            if (!await channel.CanReachAsync(author, cancellationToken)) continue;
            if ((await channel.GetAsync(messageId, author, cancellationToken)).State == StructuredRecordActionState.NotFound)
                await channel.EnqueueAsync(new StructuredRecordActionMessage(messageId, author,
                    failed ? FormIssueConfirmationPolicy.Failed(preview, culture) : FormIssueConfirmationPolicy.Body(preview, culture))
                    { Choices = FormIssueConfirmationPolicy.Choices(failed, culture, messageId == work.RunId ? null : work.RunId) }, cancellationToken);
            break;
        }
        return new(FormActionRunState.Uncertain, FormActionErrors.AwaitingConfirmation.Code, IssuePreview: preview);
    }

    private async Task NoticeAsync(FormActionWorkResponse work, IReadOnlyDictionary<string, string> notice, CancellationToken cancellationToken)
    {
        var record = await records.FindRecordAsync(work.RecordId, cancellationToken)
            ?? throw new InvalidOperationException("The action response is missing.");
        var author = record.Actor.UserId;
        foreach (var channel in channels)
        {
            if (!await channel.CanReachAsync(author, cancellationToken)) continue;
            if ((await channel.GetAsync(work.RunId, author, cancellationToken)).State != StructuredRecordActionState.NotFound) return;
            var culture = (await recipients.FindAsync(author, cancellationToken))?.Culture;
            var body = culture is not null && notice.TryGetValue(culture, out var text) ? text : notice.Values.First();
            await channel.EnqueueAsync(new StructuredRecordActionMessage(work.RunId, author, body), cancellationToken);
            return;
        }
    }

    // The request with the bound data of the response, and the language of its form.
    private async Task<(StructuredIssueRequest Request, string Culture)?> RequestAsync(FormActionWorkResponse work, IStructuredRecordIssuer issuer,
        CancellationToken cancellationToken)
    {
        var record = await records.FindRecordAsync(work.RecordId, cancellationToken)
            ?? throw new InvalidOperationException("The action response is missing.");
        var definition = await definitions.FindDefinitionAsync(record.Record.DefinitionId, cancellationToken);
        if (definition?.StandardKey != issuer.StandardKey || definition.StandardBindingsJson is not { } bindings) return null;
        var bound = FormDraftPolicy.Read<Dictionary<string, Guid>>(bindings);
        var itemCodes = await codes.ListAsync(FormIssueDataPolicy.ReferencedItemIds(record.Record), cancellationToken);
        return (new(work.RunId, work.RecordId, FormIssueDataPolicy.Read(record.Record, bound, definition.OriginalCulture, itemCodes)), definition.OriginalCulture);
    }

    private static FormActionOutcomeResponse Outcome(StructuredIssueResponse status) => status.State switch
    {
        StructuredIssueState.Pending => new(FormActionRunState.Running, Issue: status),
        StructuredIssueState.Issued => new(FormActionRunState.Completed, Issue: status),
        StructuredIssueState.Rejected => new(FormActionRunState.Failed, status.FailureCode ?? FormActionErrors.NotIssued.Code, status.CanRetry, Issue: status),
        StructuredIssueState.NeedsReview => new(FormActionRunState.Uncertain, status.ReviewReason ?? FormActionErrors.EffectUnconfirmed.Code, Issue: status),
        StructuredIssueState.Cancelled => new(FormActionRunState.Failed, FormActionErrors.NotIssued.Code, Issue: status),
        _ => new(FormActionRunState.Uncertain, FormActionErrors.EffectUnconfirmed.Code, Issue: status),
    };
}
