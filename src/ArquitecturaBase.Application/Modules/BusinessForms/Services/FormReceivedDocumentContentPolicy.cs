using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// What a received document keeps private, protected by its own purpose and bound to the document and to what it is: the reading (as the
/// engine or the Word reader gave it) and the proposal as formularios prepared it. One that cannot be read back is treated as absent.
/// </summary>
internal sealed class FormReceivedDocumentContentPolicy(IFormReceivedDocumentProtector protector)
{
    public const string ReadingKind = "reading";
    public const string ProposalKind = "proposal";
    /// <summary>VC1-A: a row's reading is its row with the headers of its sheet, under a kind of its own.</summary>
    public const string RowKind = "row";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public byte[] ProtectReading(Guid documentId, StructuredDocumentReading reading) =>
        protector.Protect(documentId, ReadingKind, JsonSerializer.Serialize(reading, Options));

    public byte[] ProtectProposal(Guid documentId, FormDocumentPreparedProposal proposal) =>
        protector.Protect(documentId, ProposalKind, JsonSerializer.Serialize(proposal, Options));

    public StructuredDocumentReading? ReadReading(Guid documentId, byte[]? value) => Read<StructuredDocumentReading>(documentId, ReadingKind, value);

    public byte[] ProtectRow(Guid documentId, FormDocumentRowReading reading) =>
        protector.Protect(documentId, RowKind, JsonSerializer.Serialize(reading, Options));

    public FormDocumentRowReading? ReadRow(Guid documentId, byte[]? value) => Read<FormDocumentRowReading>(documentId, RowKind, value);

    public FormDocumentPreparedProposal? ReadProposal(Guid documentId, byte[]? value) => Read<FormDocumentPreparedProposal>(documentId, ProposalKind, value);

    public string? ReadDetail(Guid documentId, byte[]? value) => Read<string>(documentId, FormReceivedDocumentRecorder.DetailKind, value);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    private T? Read<T>(Guid documentId, string kind, byte[]? value) where T : class
    {
        if (value is null || protector.Unprotect(documentId, kind, value) is not { } json) return null;
        try { return JsonSerializer.Deserialize<T>(json, Options); }
        catch (JsonException) { return null; }
    }
}
