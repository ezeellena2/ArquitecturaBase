using System.Globalization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormDefinitionLifecyclePolicy
{
    public static bool HasOpenSends(StructuredFormChannelStateRow? state, DateTime nowUtc) =>
        state?.LastOpenSessionExpiresAtUtc > nowUtc || state?.HasPendingReplies == true;

    public static FormDefinitionLifecycleResponse View(FormLifecycleStoredRow row, StructuredFormChannelStateRow? state,
        DateTime nowUtc, bool canManage, bool canPublish, bool canReadRecords)
    {
        var deletionReason = row.PublicationInProgress ? "PublicationInProgress" : row.LastRecordNumber > 0 ? "HasResponses"
            : HasOpenSends(state, nowUtc) ? "OpenSends" : null;
        return new(row.Revision, row.DeactivatedAtUtc, row.DeactivatedBy, canReadRecords ? row.LastRecordNumber : null,
            canReadRecords ? row.PendingApprovalCount : null,
            canReadRecords ? row.Prefix + "-" + checked(row.LastRecordNumber + 1).ToString("D6", CultureInfo.InvariantCulture) : null,
            row.NextVersionNumber, canPublish && !row.PublicationInProgress && row.ActiveVersionId is not null && row.DeactivatedAtUtc is null,
            canPublish && !row.PublicationInProgress && row.DeactivatedAtUtc is not null,
            row.PublicationInProgress ? "PublicationInProgress" : row.ActiveVersionId is null ? "NotPublished" : null,
            state?.LastOpenSessionExpiresAtUtc, canManage && deletionReason is null, canManage ? deletionReason : null);
    }
}
