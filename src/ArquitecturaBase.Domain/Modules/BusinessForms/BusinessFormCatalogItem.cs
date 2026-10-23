using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed class BusinessFormCatalogItem : Entity, IAuditable
{
    public const int MaxTextLength = 30;
    public const int MaxDetailLength = 300;

    private BusinessFormCatalogItem()
    {
    }

    public Guid CatalogId { get; private set; }
    public string Code { get; private set; } = "";
    public string? Value { get; private set; }
    public string FieldValuesJson { get; private set; } = "{}";
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
        string? detailEs, string? detailEn, int position, string? value = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(catalogId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(textEs);
        ArgumentOutOfRangeException.ThrowIfNegative(position);

        return new()
        {
            CatalogId = catalogId,
            Code = code,
            Value = value,
            TextEs = textEs,
            TextEn = textEn,
            DetailEs = detailEs,
            DetailEn = detailEn,
            Position = position,
        };
    }

    public string TextFor(string culture) => culture == "en" && !string.IsNullOrWhiteSpace(TextEn) ? TextEn : TextEs;

    public string? DetailFor(string culture) => culture == "en" && !string.IsNullOrWhiteSpace(DetailEn) ? DetailEn : DetailEs;

    internal void Update(string code, string textEs, string? textEn, string? detailEs, string? detailEn, int position, string? value = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(textEs);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        Code = code;
        Value = value;
        TextEs = textEs;
        TextEn = textEn;
        DetailEs = detailEs;
        DetailEn = detailEn;
        Position = position;
    }

    internal void SetActive(bool isActive) => IsActive = isActive;
    internal void SetFields(string fieldValuesJson) => FieldValuesJson = fieldValuesJson;
}
