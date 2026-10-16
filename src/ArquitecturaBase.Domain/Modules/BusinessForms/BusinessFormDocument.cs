using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed class BusinessFormDocument : Entity, IAuditable
{
    private BusinessFormDocument() { }
    public Guid DefinitionId { get; private set; }
    public Guid RecordId { get; private set; }
    public Guid RunId { get; private set; }
    public Guid FileId { get; private set; }
    public string Culture { get; private set; } = "";
    public Guid? DesignId { get; private set; }
    public long DesignRevision { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static BusinessFormDocument Create(Guid definitionId, Guid recordId, Guid runId, Guid fileId,
        string culture, Guid? designId, long designRevision)
    {
        foreach (var id in new[] { definitionId, recordId, runId, fileId }) ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (culture is not ("es" or "en")) throw new ArgumentException("The document culture is unsupported.", nameof(culture));
        ArgumentOutOfRangeException.ThrowIfNegative(designRevision);
        if (designId == Guid.Empty || designId.HasValue != (designRevision > 0)) throw new ArgumentException("The document design revision is invalid.", nameof(designRevision));
        return new() { DefinitionId = definitionId, RecordId = recordId, RunId = runId, FileId = fileId,
            Culture = culture, DesignId = designId, DesignRevision = designRevision };
    }
    public override string ToString() => "BusinessFormDocument { Content = [REDACTED] }";
}
