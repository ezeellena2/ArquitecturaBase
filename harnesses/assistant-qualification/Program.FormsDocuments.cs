using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AssistantQualification;

internal static partial class Program
{
    // forms-documents (task 17b): «Armar formulario con la IA» (DOC6) from the inbox. Each photo or PDF reaches the inbox as WhatsApp reads it:
    // ReadDocument once, in its sender's automatic conversation and its own scope (<case>-<n>-document, the document candidate), the first time
    // a case of its culture attaches it; every later case inherits that saved reading and nothing reads it again. A Word carries what its owner
    // reads on the server. Then the reviewer's interpretation, each in its own scope and conversation. The run stops at the first failed check.
    private static async Task<bool> RunFormsDocumentsAsync(IHost host, QualificationSettings settings, QualificationFixture fixture,
        QualificationChannelFixture forms, VerifiedFormsDocumentSet set, CancellationToken ct)
    {
        var definitions = Definitions(host, QualificationFormsDocuments.Capabilities);
        var saved = new Dictionary<(string Culture, string File), FormsSavedReading>();
        var conversations = new HashSet<Guid>();
        foreach (var testCase in set.Cases)
        {
            var sender = fixture.Subjects.Single(person => person.Culture == testCase.Culture);
            var reviewer = fixture.Reviewers.Single(person => person.Culture == testCase.Culture);
            var readings = new List<FormsSavedReading>();
            for (var index = 0; index < testCase.Documents.Length; index++)
            {
                var document = testCase.Documents[index];
                if (QualificationHomeDocuments.IsWord(document))
                {
                    await using var wordScope = host.Services.CreateAsyncScope();
                    readings.Add(await wordScope.ServiceProvider.GetRequiredService<FormsDocumentRunner>().PrepareWordAsync(document, set.Document(document), ct));
                    continue;
                }
                if (document.Inherited)
                {
                    readings.Add(saved[(testCase.Culture, document.FileName)]);
                    continue;
                }
                var readingId = QualificationFormsDocuments.ReadingCaseId(testCase, index);
                var name = QualificationFormsDocuments.ChannelName(document.ContentType);
                DocumentObservation read;
                await using (var documentScope = host.Services.CreateAsyncScope())
                    read = await documentScope.ServiceProvider.GetRequiredService<DocumentRunner>()
                        .RunAsync(set.Reading(testCase, document), set.Document(document), sender, ct, readingId, name);
                var evidence = await EvidenceAsync(readingId + "-document", read.Metadata.Stamps.ChannelId, testCase.Culture, read.Result.State,
                    read.Result.FailureCode, read.Metadata, read.ElapsedSeconds, read.Measurements, read.Checks, [], QualificationFormsDocuments.CaseFile,
                    false, ct, DocumentCandidate);
                if (!await SaveAsync(settings, evidence, ct)) return false;
                // The inbox keeps the reading with the copy it opened, under the name the app gave the document.
                var kept = new FormsSavedReading(read.Key, read.Prepared! with { FileName = name, Reading = read.Result.DocumentReading });
                saved[(testCase.Culture, document.FileName)] = kept;
                readings.Add(kept);
            }
            await using var scope = host.Services.CreateAsyncScope();
            var observed = await scope.ServiceProvider.GetRequiredService<FormsDocumentRunner>()
                .RunAsync(testCase, reviewer, readings, set, forms, conversations, ct);
            conversations.Add(observed.Metadata.ConversationId);
            var outcome = await EvidenceAsync(testCase.Id, observed.Metadata.Stamps.ChannelId, testCase.Culture, observed.Result.State,
                observed.Result.FailureCode, observed.Metadata, observed.ElapsedSeconds, observed.Measurements, observed.Checks, definitions,
                QualificationFormsDocuments.CaseFile, false, ct);
            if (!await SaveAsync(settings, outcome, ct)) return false;
        }
        return true;
    }
}
