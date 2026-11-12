using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AssistantQualification;

internal static partial class Program
{
    // The isolated ReadDocument candidate (QualificationHost.DocumentProfiles): never a production declaration.
    internal static readonly (string File, string Model, string Id) DocumentCandidate =
        ("document-candidate-profile.json", "gpt-6.1-sol", DocumentReadingOracle.CandidateId);

    // Who reads a document case: its culture's Home accounts, in turns by its position among that culture's cases, so no person reads more
    // than the documents a person may read a day.
    internal static QualificationActor DocumentActor(DocumentCase[] cases, DocumentCase testCase, QualificationFixture fixture)
    {
        var pool = fixture.Actors.Where(item => item.Culture == testCase.Culture).ToArray();
        if (pool.Length == 0) throw new InvalidOperationException("Unknown document qualification actor.");
        var position = cases.Where(item => item.Culture == testCase.Culture).ToList().IndexOf(testCase);
        return pool[position % pool.Length];
    }

    // document-reading: every case of the set verified before the run (QualificationDocuments.LoadVerifiedAsync) reads its fixture once,
    // and the run stops at the first failed check, like the other sets.
    private static async Task<bool> RunDocumentsAsync(IHost host, QualificationSettings settings, QualificationFixture fixture,
        VerifiedDocumentSet documents, CancellationToken ct)
    {
        var cases = documents.Cases;
        foreach (var testCase in cases)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var document = documents.Manifest.Documents.Single(item => item.FileName == testCase.FileName);
            var observed = await scope.ServiceProvider.GetRequiredService<DocumentRunner>()
                .RunAsync(testCase, document, DocumentActor(cases, testCase, fixture), ct);
            var evidence = await EvidenceAsync(testCase.Id, QualificationDocuments.ChannelId, testCase.Culture, observed.Result.State,
                observed.Result.FailureCode, observed.Metadata, observed.ElapsedSeconds, observed.Measurements, observed.Checks, [],
                QualificationDocuments.CaseFile, false, ct, DocumentCandidate);
            if (!await SaveAsync(settings, evidence, ct)) return false;
        }
        return true;
    }
}
