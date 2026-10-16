using ArquitecturaBase.Domain.Common;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public sealed class BusinessFormPdfDesign : Entity, IAuditable
{
    private BusinessFormPdfDesign() { }
    public Guid DefinitionId { get; private set; }
    public long Revision { get; private set; }
    public string OriginalCulture { get; private set; } = "es";
    public string DesignJson { get; private set; } = "{}";
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static BusinessFormPdfDesign Create(Guid definitionId, long revision, string originalCulture, string designJson)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(definitionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        if (originalCulture is not ("es" or "en")) throw new ArgumentException("The original culture is invalid.", nameof(originalCulture));
        ArgumentException.ThrowIfNullOrWhiteSpace(designJson);
        return new() { DefinitionId = definitionId, Revision = revision, OriginalCulture = originalCulture, DesignJson = designJson };
    }
    public override string ToString() => $"BusinessFormPdfDesign {{ Id = {Id}, Revision = {Revision}, Content = [REDACTED] }}";
}
