using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The issuer is asked first: enqueueing is idempotent by run and local to the issuer, so a run that lost its outcome asks again
/// without issuing twice. Only a response that still stands is queued; once queued, it is followed to its end. One from a channel
/// asks its author first («¿La emito?»), also after a summary that failed, and while it waits for its provider whoever loaded the
/// response is told once (<see cref="FormIssueMessageIssuer"/>). The data come from <see cref="FormIssueRequestPolicy"/>; the
/// copy by email is <see cref="FormIssueCopyService"/>.
/// </summary>
internal sealed class FormIssueActionService(IEnumerable<IStructuredRecordIssuer> issuers, FormIssueRequestPolicy requests, FormIssueMessageIssuer messenger)
    : IFormIssueActionService
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
            if (await requests.RequestAsync(work.RunId, work.RecordId, issuer, cancellationToken) is not var (request, _))
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
            await messenger.NoticeAsync(work.RunId, work.RecordId, notice, cancellationToken);
        return Outcome(status);
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

    // The summary, or the reason it failed, goes to whoever loaded it, and the run waits, uncertain, until that person decides.
    private async Task<FormActionOutcomeResponse> AskAsync(FormActionWorkResponse work, IStructuredRecordIssuer issuer, StructuredIssueRequest request,
        Guid messageId, CancellationToken cancellationToken)
    {
        var preview = await issuer.PreviewAsync(request.Data, cancellationToken);
        await messenger.AskAsync(work.RunId, work.RecordId, preview, messageId, cancellationToken);
        return new(FormActionRunState.Uncertain, FormActionErrors.AwaitingConfirmation.Code, IssuePreview: preview);
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
