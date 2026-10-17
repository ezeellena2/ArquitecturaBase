using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace AssistantQualification;

internal sealed record QualificationLedgerEntry(string CaseId, Guid RequestId, Guid ActorUserId,
    Guid? ConversationId, long? ConversationRevision, string Phase)
{
    public string OperationKind { get; init; } = "home";
    public string? ChannelId { get; init; }
    public string? Culture { get; init; }
    public Guid? SubjectUserId { get; init; }
    public string? ChannelKind { get; init; }
}

internal sealed record QualificationRunBinding(string RunId, string EvaluationIdentitySha256);

internal sealed class SafeRunLedger(QualificationSettings settings)
{
    public static async Task BindAsync(QualificationSettings settings, bool resume, CancellationToken cancellationToken)
    {
        var parsed = new NpgsqlConnectionStringBuilder(settings.EvaluationConnectionString);
        // Hash infrastructure identity only; never the connection string, password, key or ciphertext.
        var identity = EvidenceWriter.HashPublicText(JsonSerializer.Serialize(new { parsed.Host, parsed.Port, parsed.Database }));
        var expected = new QualificationRunBinding(settings.RunId, identity);
        var path = Path.Combine(settings.OutputDirectory, "run.public.json");
        if (resume)
        {
            var actual = JsonSerializer.Deserialize<QualificationRunBinding>(await File.ReadAllBytesAsync(path, cancellationToken), JsonSerializerOptions.Web);
            if (actual != expected) throw new InvalidOperationException("Qualification run binding does not match.");
            return;
        }
        if (File.Exists(path)) throw new InvalidOperationException("Qualification run already exists; observe status instead of restarting.");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(expected, JsonSerializerOptions.Web), cancellationToken);
    }

    public async Task SaveAsync(QualificationLedgerEntry entry, CancellationToken cancellationToken)
    {
        if (!Regex.IsMatch(entry.CaseId, "^[a-z0-9-]{1,100}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Invalid qualification case identifier.");
        var directory = Path.Combine(settings.OutputDirectory, "ledger");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, entry.CaseId + ".json");
        if (File.Exists(path))
        {
            var previous = JsonSerializer.Deserialize<QualificationLedgerEntry>(await File.ReadAllBytesAsync(path, cancellationToken), JsonSerializerOptions.Web);
            if (previous is null || previous.RequestId != entry.RequestId || previous.ActorUserId != entry.ActorUserId
                || previous.OperationKind != entry.OperationKind || previous.ChannelId != entry.ChannelId || previous.Culture != entry.Culture
                || previous.SubjectUserId != entry.SubjectUserId || previous.ChannelKind != entry.ChannelKind)
                throw new InvalidOperationException("Qualification case already has an immutable request identity and scope.");
        }
        var temporary = path + ".new";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entry, JsonSerializerOptions.Web), cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }
    // Do not remove this ledger or the evaluation DB on timeout/uncertainty. Resume status using RequestId.
}
