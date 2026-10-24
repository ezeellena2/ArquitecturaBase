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
    public string? TextEs { get; private set; }
    public string? TextEn { get; private set; }
    public string? DetailEs { get; private set; }
    public string? DetailEn { get; private set; }
    public int Position { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static BusinessFormCatalogItem Create(Guid catalogId, string code, string? textEs, string? textEn,
        string? detailEs, string? detailEn, int position, string? value = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(catalogId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        RequireText(textEs, textEn);
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

    // The text in the requested culture or, without a translation, in the other one: the catalog's original is always present.
    public string TextFor(string culture) => In(TextEs, TextEn, culture) ?? In(TextEs, TextEn, Other(culture))!;

    public string? DetailFor(string culture) => In(DetailEs, DetailEn, culture) ?? In(DetailEs, DetailEn, Other(culture));

    internal void Update(string code, string? textEs, string? textEn, string? detailEs, string? detailEn, int position, string? value = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        RequireText(textEs, textEn);
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

    private static void RequireText(string? textEs, string? textEn)
    {
        if (string.IsNullOrWhiteSpace(textEs) && string.IsNullOrWhiteSpace(textEn))
            throw new ArgumentException("The item needs a text in at least one culture.", nameof(textEs));
    }

    private static string Other(string culture) => culture == "en" ? "es" : "en";

    private static string? In(string? spanish, string? english, string culture)
    {
        var text = culture == "en" ? english : spanish;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
