namespace ArquitecturaBase.Application.Models.StructuredData;

/// <summary>
/// A catalog a feed keeps: its stable key (up to 32 lowercase characters, «arca.concepts»), its name and description by
/// language (es, en; the original is Spanish), the type of its values («Text», «Integer», «Decimal» or «Date») and the system
/// folder where it goes.
/// </summary>
public sealed record StructuredFeedCatalog(string Key, IReadOnlyDictionary<string, string> Name, IReadOnlyDictionary<string, string> Description,
    string ValueType, StructuredFormStandardFolder Folder);

/// <summary>
/// An item of a fed catalog: a stable code, its text by language (up to <see cref="MaxTextLength"/> characters, what an option
/// admits) and its value in canonical form (null in a Text catalog).
/// </summary>
public sealed record StructuredFeedItem(string Code, IReadOnlyDictionary<string, string> Text, string? Value)
{
    public const int MaxTextLength = 30;
}

/// <summary>What a feed read: the items, or a stable failure code and no items.</summary>
public sealed record StructuredFeedReadResponse(IReadOnlyList<StructuredFeedItem>? Items, string? FailureCode = null)
{
    public static StructuredFeedReadResponse Failed(string failureCode) => new(null, failureCode);
}
