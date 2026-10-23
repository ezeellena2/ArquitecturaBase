using System.Text.Json;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public enum BusinessFormPersonAttributeType { Catalog, Text }
public sealed class BusinessFormPersonAttribute : Entity, IAuditable
{
    public const int MaximumAttributes = 20;
    private BusinessFormPersonAttribute() { }
    public string Key { get; private set; } = "";
    public string NameJson { get; private set; } = "{}";
    public BusinessFormPersonAttributeType Type { get; private set; }
    public Guid? SourceCatalogId { get; private set; }
    public int Position { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long Revision { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static Result<BusinessFormPersonAttribute> Create(string key, IReadOnlyDictionary<string, string> name,
        BusinessFormPersonAttributeType type, Guid? sourceCatalogId, int position)
    {
        if (!IsKey(key) || !Valid(name, type, sourceCatalogId, position)) return FormPersonDataErrors.AttributeInvalid;
        return new BusinessFormPersonAttribute { Key = key, NameJson = Names(name), Type = type, SourceCatalogId = sourceCatalogId, Position = position };
    }
    public Result Update(long expectedRevision, IReadOnlyDictionary<string, string> name, BusinessFormPersonAttributeType type,
        Guid? sourceCatalogId, int position, bool isActive)
    {
        if (Revision != expectedRevision || Revision >= 9007199254740991) return FormPersonDataErrors.AttributeRevisionConflict;
        if (!Valid(name, type, sourceCatalogId, position)) return FormPersonDataErrors.AttributeInvalid;
        if (isActive) NameJson = Names(name);
        Type = type; SourceCatalogId = sourceCatalogId; Position = position; IsActive = isActive; Revision++;
        return Result.Success();
    }
    private static bool IsKey(string key) => key.Length is > 0 and <= 40 && key[0] is >= 'a' and <= 'z'
        && key.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
    private static bool Valid(IReadOnlyDictionary<string, string> name, BusinessFormPersonAttributeType type, Guid? source, int position) =>
        Enum.IsDefined(type) && (type == BusinessFormPersonAttributeType.Catalog ? source is { } id && id != Guid.Empty : source is null)
        && position is >= 0 and < MaximumAttributes && name.TryGetValue("es", out var original) && !string.IsNullOrWhiteSpace(original)
        && name.All(pair => pair.Key is "es" or "en" && pair.Value.Length <= 120 && StorableText.IsStorable(pair.Value));
    private static string Names(IReadOnlyDictionary<string, string> name) => JsonSerializer.Serialize(name.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
        .ToDictionary(pair => pair.Key, pair => pair.Value.Trim(), StringComparer.Ordinal), JsonSerializerOptions.Web);
}
