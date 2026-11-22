using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

/// <summary>The bounds of a number of Configuración › Formularios and the value it has until someone saves another.</summary>
public readonly record struct FormSettingRange(int Minimum, int Maximum, int Default)
{
    public bool Contains(int value) => value >= Minimum && value <= Maximum;
}

/// <summary>
/// Configuración › Formularios (RC4-A, with the received documents' retention of RC1-A): the rules of the module an administrator adjusts,
/// each starting with the value it had in the code. One row, whose key is <see cref="SingletonId"/>: without it every value is its default and
/// the revision is 0. Internal configuration (ADR 0018): a save brings the revision it read and advances it. Every use reads the row again.
/// </summary>
public sealed class BusinessFormsSettings : Entity, IAuditable
{
    /// <summary>The key of the only row, as text, for the CHECK of the table.</summary>
    public const string SingletonIdValue = "00000000-0000-0000-0000-0000000000f1";
    public static readonly Guid SingletonId = new(SingletonIdValue);

    /// <summary>Each photo or document when answering, in MiB. 25 is the ceiling of WhatsApp; a version seals it when published.</summary>
    public static readonly FormSettingRange MaxFileSizeMiBRange = new(1, 25, 25);
    /// <summary>The documents read again a business day for each person who asked for «Armar formulario con la IA» (DOC6).</summary>
    public static readonly FormSettingRange DailyRereadsPerAskerRange = new(1, 100, 5);
    /// <summary>From how many similar unclassified documents the inbox suggests building a form (DOC6).</summary>
    public static readonly FormSettingRange SimilarDocumentsRange = new(2, 20, 3);
    /// <summary>Days a proposed document may wait for the team before it expires, and days a discarded one is kept after its discard (F9).</summary>
    public static readonly FormSettingRange RetentionDaysRange = new(1, 3650, 90);

    // For EF Core and Create.
    private BusinessFormsSettings() : base(SingletonId) { }

    public int MaxFileSizeMiB { get; private set; } = MaxFileSizeMiBRange.Default;
    public int DailyRereadsPerAsker { get; private set; } = DailyRereadsPerAskerRange.Default;
    public int SimilarDocumentsToSuggest { get; private set; } = SimilarDocumentsRange.Default;
    public int ProposedExpiryDays { get; private set; } = RetentionDaysRange.Default;
    public int DiscardedRetentionDays { get; private set; } = RetentionDaysRange.Default;
    public long Revision { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    /// <summary>The settings nobody saved yet: today's values, at revision 0.</summary>
    public static BusinessFormsSettings Create() => new();

    public Result Update(long expectedRevision, int maxFileSizeMiB, int dailyRereadsPerAsker, int similarDocumentsToSuggest,
        int proposedExpiryDays, int discardedRetentionDays)
    {
        if (expectedRevision != Revision) return FormSettingsErrors.RevisionConflict;
        if (!MaxFileSizeMiBRange.Contains(maxFileSizeMiB) || !DailyRereadsPerAskerRange.Contains(dailyRereadsPerAsker)
            || !SimilarDocumentsRange.Contains(similarDocumentsToSuggest) || !RetentionDaysRange.Contains(proposedExpiryDays)
            || !RetentionDaysRange.Contains(discardedRetentionDays)) return FormSettingsErrors.ContentInvalid;
        (MaxFileSizeMiB, DailyRereadsPerAsker, SimilarDocumentsToSuggest, ProposedExpiryDays, DiscardedRetentionDays) =
            (maxFileSizeMiB, dailyRereadsPerAsker, similarDocumentsToSuggest, proposedExpiryDays, discardedRetentionDays);
        Revision = checked(Revision + 1);
        return Result.Success();
    }

    public override string ToString() => $"BusinessFormsSettings {{ Revision = {Revision} }}";
}
