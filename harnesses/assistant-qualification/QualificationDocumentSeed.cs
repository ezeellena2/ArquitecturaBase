using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace AssistantQualification;

// channel-documents only, after QualificationSeed and QualificationFormSeed in the same isolated database: the loaders of the subjects' role
// who send the spreadsheets (the subjects already make their ten interpretations with the documents) and the forms with document reading
// (QualificationFormSeed.CreateReadingFormsAsync). The other sets never create them, so their reads and counts do not change.
// forms-documents only (task 17b), after the same two seeds: the inbox reviewers (CreateReviewersAsync), and never the forms with document
// reading, since a document the inbox leaves unclassified fits no form.
internal sealed class QualificationDocumentSeed(IUnitOfWork unitOfWork, IUserRepository users, IRoleRepository roles, QualificationFormSeed forms)
{
    private static readonly string[] Cultures = ["es", "en"];
    public const string ReviewerRole = "QualificationReviewer";

    public async Task<(QualificationFixture Fixture, QualificationChannelFixture Forms)> CreateAsync(QualificationFixture fixture,
        QualificationChannelFixture channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var loaders = new List<QualificationActor>();
        var result = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            foreach (var culture in Cultures)
            {
                var loader = await users.CreateAsync(Email.Create($"sheet-loader-{culture}@example.invalid").Value, null, false,
                    $"Spreadsheet loader {culture}", culture, token);
                await users.SetRolesAsync(loader.Id, ["QualificationSubject"], token);
                loaders.Add(new(loader.Id, $"sheet-loader-{culture}", culture, "QualificationSubject"));
            }
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);
        if (result.IsFailure) throw new InvalidOperationException("Qualification document seed failed.");
        var seeded = fixture with { SheetLoaders = loaders };
        return (seeded, await forms.CreateReadingFormsAsync(seeded, channel, cancellationToken));
    }

    // One reviewer per culture with what DOC6 asks for («Ver documentos recibidos», «Revisar documentos» and «Diseñar formularios») and
    // reading catalogs, so the inbox offers its four capabilities; the documents' senders are the base seed's subjects.
    public async Task<QualificationFixture> CreateReviewersAsync(QualificationFixture fixture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var reviewers = new List<QualificationActor>();
        var result = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await roles.CreateAsync(ReviewerRole, "Fictitious inbox review and form design", QualificationFormsDocuments.ReviewerPermissions, token);
            foreach (var culture in Cultures)
            {
                var reviewer = await users.CreateAsync(Email.Create($"inbox-reviewer-{culture}@example.invalid").Value, null, false,
                    $"Inbox reviewer {culture}", culture, token);
                await users.SetRolesAsync(reviewer.Id, [ReviewerRole], token);
                reviewers.Add(new(reviewer.Id, $"inbox-reviewer-{culture}", culture, ReviewerRole));
            }
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);
        if (result.IsFailure) throw new InvalidOperationException("Qualification reviewer seed failed.");
        return fixture with { Reviewers = reviewers };
    }
}
