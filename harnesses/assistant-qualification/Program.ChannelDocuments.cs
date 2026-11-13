using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AssistantQualification;

internal static partial class Program
{
    // channel-documents (task 17a): what the WhatsApp automatic assistant does with a document or a spreadsheet a person sends. Each case does
    // what the app does, in its order and each in its own scope: a voice note is normalized first (<case>-audio), a document is read with
    // ReadDocument in the person's automatic context (<case>-document, the document candidate) and then interpreted with the context the
    // channel builds; the form chosen from the list continues the operation of its start. The run stops at the first failed check.
    private static async Task<bool> RunChannelDocumentsAsync(IHost host, QualificationSettings settings, QualificationFixture fixture,
        QualificationChannelFixture forms, VerifiedChannelDocumentSet set, CancellationToken ct)
    {
        var definitions = Definitions(host, QualificationChannelDocuments.References);
        var observedCases = new Dictionary<string, ChannelObservation>(StringComparer.Ordinal);
        foreach (var testCase in set.Cases)
        {
            var person = ChannelRunner.People(testCase, fixture).Actor;
            AudioObservation? dictated = null;
            if (testCase.Audio is not null)
            {
                await using (var audioScope = host.Services.CreateAsyncScope())
                    dictated = await audioScope.ServiceProvider.GetRequiredService<AudioRunner>().NormalizeDictationAsync(testCase, person, ct);
                var heard = await EvidenceAsync(testCase.Id + "-audio", dictated.Metadata.Stamps.ChannelId, testCase.Culture, dictated.Result.State,
                    dictated.Result.FailureCode, dictated.Metadata, dictated.ElapsedSeconds, dictated.Measurements, dictated.Checks, [],
                    QualificationChannelDocuments.CaseFile, true, ct);
                if (!await SaveAsync(settings, heard, ct)) return false;
            }
            DocumentObservation? read = null;
            if (testCase.Document is not null)
            {
                await using (var documentScope = host.Services.CreateAsyncScope())
                    read = await documentScope.ServiceProvider.GetRequiredService<DocumentRunner>()
                        .RunAsync(set.Reading(testCase), set.Document(testCase), person, ct, testCase.Id);
                var reading = await EvidenceAsync(testCase.Id + "-document", read.Metadata.Stamps.ChannelId, testCase.Culture, read.Result.State,
                    read.Result.FailureCode, read.Metadata, read.ElapsedSeconds, read.Measurements, read.Checks, [], QualificationChannelDocuments.CaseFile,
                    false, ct, DocumentCandidate);
                if (!await SaveAsync(settings, reading, ct)) return false;
            }
            await using var scope = host.Services.CreateAsyncScope();
            var previous = testCase.Continues is { } continues ? observedCases[continues] : null;
            var observed = await scope.ServiceProvider.GetRequiredService<ChannelRunner>()
                .RunLoadingAsync(testCase, fixture, forms, set, ct, previous, dictated, read);
            observedCases[testCase.Id] = observed;
            var evidence = await EvidenceAsync(testCase.Id, observed.Metadata.Stamps.ChannelId, testCase.Culture, observed.Result.State,
                observed.Result.FailureCode, observed.Metadata, observed.ElapsedSeconds, observed.Measurements, observed.Checks, definitions,
                QualificationChannelDocuments.CaseFile, false, ct);
            if (!await SaveAsync(settings, evidence, ct)) return false;
        }
        return true;
    }
}
