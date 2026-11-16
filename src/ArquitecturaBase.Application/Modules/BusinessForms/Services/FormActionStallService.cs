using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Common.Formatting;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// F2: when an action stops its rule (failed, or in doubt waiting for a person), whoever loaded the response is told once, in their
/// language, by the first channel that reaches them. The channel keeps one message per id and person, and the id comes from the run,
/// so a retry that fails again, a consult, or a second worker repeat nothing; the run keeps the mark of having been announced. Who
/// has no channel to hear it is marked too and nothing waits for them. It runs outside any transaction, like the action, and a
/// failure leaves the run unmarked: the worker brings it back.
/// </summary>
internal sealed partial class FormActionStallService(IFormActionRunRepository runs, IFormResponseReader records,
    IEnumerable<IStructuredRecordActionChannel> channels, IFormActionRecipientReader recipients, IUnitOfWork unitOfWork,
    TimeProvider timeProvider, ILogger<FormActionStallService> logger) : IFormActionStallService
{
    public async Task NotifyAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await runs.GetAsync(runId, cancellationToken);
        if (run is null || !run.NeedsStallNotice) return;
        var stored = await records.FindRecordAsync(run.RecordId, cancellationToken);
        if (stored is not null)
        {
            var author = stored.Actor.UserId;
            var messageId = NoticeId(run.Id);
            foreach (var channel in channels)
            {
                if (!await channel.CanReachAsync(author, cancellationToken)) continue;
                if ((await channel.GetAsync(messageId, author, cancellationToken)).State == StructuredRecordActionState.NotFound)
                {
                    var culture = (await recipients.FindAsync(author, cancellationToken))?.Culture is "en" ? "en" : "es";
                    await channel.EnqueueAsync(new StructuredRecordActionMessage(messageId, author, Text(stored.Record, culture)), cancellationToken);
                    LogNoticed(logger, run.Id);
                }
                break;
            }
        }
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            (await runs.GetAsync(runId, ct))?.RecordStallNotice(timeProvider.GetUtcNow().UtcDateTime);
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);
    }

    /// <summary>The id of the notice for the channel, one per run and its own: it never collides with the run that carries a message.</summary>
    public static Guid NoticeId(Guid runId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"action-stalled:{runId:N}"))).AsSpan(0, 16));

    private static string Text(Models.FormRecordStoredRow row, string culture)
    {
        var reply = FormRecordPresentationPolicy.Project(row, culture, FormDraftPolicy.Read<Models.FormVersionSchema>(row.SchemaJson), false);
        return DisplayNumbers.Format(culture, FormResponseTexts.Get("Action.Stalled", culture), reply.Number, reply.Name);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Form action {RunId} stopped its rule and its author was told.")]
    private static partial void LogNoticed(ILogger logger, Guid runId);
}
