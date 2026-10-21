using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormDefinitionLifecyclePolicy
{
    public static FormDefinitionLifecycleResponse View(FormLifecycleStoredRow row, bool canPublish, bool canReadRecords) =>
        new(row.Revision, row.DeactivatedAtUtc, row.DeactivatedBy, canReadRecords ? row.LastRecordNumber : null,
            canReadRecords ? row.PendingApprovalCount : null,
            canReadRecords ? row.Prefix + "-" + checked(row.LastRecordNumber + 1).ToString("D6", CultureInfo.InvariantCulture) : null,
            row.NextVersionNumber, canPublish && !row.PublicationInProgress && row.ActiveVersionId is not null && row.DeactivatedAtUtc is null,
            canPublish && !row.PublicationInProgress && row.DeactivatedAtUtc is not null,
            row.PublicationInProgress ? "PublicationInProgress" : row.ActiveVersionId is null ? "NotPublished" : null);
}
