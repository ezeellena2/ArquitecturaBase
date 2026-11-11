using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The messages of an issuance to whoever loaded its response, by the first channel that reaches them and in their language: the
/// «¿La emito?» (or the summary that failed, with «Volver a probar» and «No emitir») and the notice that the issuer waits for its
/// provider. Each goes once: the channel keeps one message per id and person, so each has its own id (the run for the first
/// question, one per «Volver a probar», one per run for the notice). It runs outside any transaction, like the action.
/// </summary>
internal sealed class FormIssueMessageIssuer(IFormResponseReader records, IEnumerable<IStructuredRecordActionChannel> channels,
    IFormActionRecipientReader recipients)
{
    public Task AskAsync(Guid runId, Guid recordId, StructuredIssuePreview preview, Guid messageId, CancellationToken cancellationToken) =>
        SendOnceAsync(recordId, messageId, (author, culture) =>
        {
            var language = culture is "en" ? "en" : "es";
            var failed = preview.FailureCode is not null;
            return new StructuredRecordActionMessage(messageId, author,
                failed ? FormIssueConfirmationPolicy.Failed(preview, language) : FormIssueConfirmationPolicy.Body(preview, language))
                { Choices = FormIssueConfirmationPolicy.Choices(failed, language, messageId == runId ? null : runId) };
        }, cancellationToken);

    public Task NoticeAsync(Guid runId, Guid recordId, IReadOnlyDictionary<string, string> notice, CancellationToken cancellationToken)
    {
        var messageId = FormIssueConfirmationPolicy.NoticeId(runId);
        return SendOnceAsync(recordId, messageId, (author, culture) => new StructuredRecordActionMessage(messageId, author,
            culture is not null && notice.TryGetValue(culture, out var text) ? text : notice.Values.First()), cancellationToken);
    }

    private async Task SendOnceAsync(Guid recordId, Guid messageId, Func<Guid, string?, StructuredRecordActionMessage> message,
        CancellationToken cancellationToken)
    {
        var record = await records.FindRecordAsync(recordId, cancellationToken) ?? throw new InvalidOperationException("The action response is missing.");
        var author = record.Actor.UserId;
        foreach (var channel in channels)
        {
            if (!await channel.CanReachAsync(author, cancellationToken)) continue;
            if ((await channel.GetAsync(messageId, author, cancellationToken)).State != StructuredRecordActionState.NotFound) return;
            await channel.EnqueueAsync(message(author, (await recipients.FindAsync(author, cancellationToken))?.Culture), cancellationToken);
            return;
        }
    }
}
