namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public sealed record WhatsAppOutgoingQuickReply(string Id, string Title, long DefinitionRevision)
{
    /// <summary>
    /// Review of task 9 of the documents: only a row of a list, the line under its title (up to 72 characters, the full name of a form whose
    /// title was cut); omitted from the JSON when absent, so earlier options keep their bytes.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    public override string ToString() => "WhatsAppOutgoingQuickReply { Content = [REDACTED] }";
}
