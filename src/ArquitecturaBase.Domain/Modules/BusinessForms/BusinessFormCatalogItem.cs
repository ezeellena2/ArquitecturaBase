using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed class BusinessFormCatalogItem : Entity, IAuditable
{
    private BusinessFormCatalogItem()
    {
    }

    public Guid CatalogId { get; private set; }
    public string Code { get; private set; } = "";
    public string TextEs { get; private set; } = "";
    public string? TextEn { get; private set; }
    public string? DetailEs { get; private set; }
    public string? DetailEn { get; private set; }
    public int Position { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static BusinessFormCatalogItem Create(Guid catalogId, string code, string textEs, string? textEn,
        string? detailEs, string? detailEn, int position)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(catalogId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(textEs);
        ArgumentOutOfRangeException.ThrowIfNegative(position);

        return new()
        {
            CatalogId = catalogId,
            Code = code,
            TextEs = textEs,
            TextEn = textEn,
            DetailEs = detailEs,
            DetailEn = detailEn,
            Position = position,
        };
    }

    public string TextFor(string culture) => culture == "en" && !string.IsNullOrWhiteSpace(TextEn) ? TextEn : TextEs;

    public string? DetailFor(string culture) => culture == "en" && !string.IsNullOrWhiteSpace(DetailEn) ? DetailEn : DetailEs;

    internal void Update(string code, string textEs, string? textEn, string? detailEs, string? detailEn, int position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(textEs);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        Code = code;
        TextEs = textEs;
        TextEn = textEn;
        DetailEs = detailEs;
        DetailEn = detailEn;
        Position = position;
    }

    internal void SetActive(bool isActive) => IsActive = isActive;
}
