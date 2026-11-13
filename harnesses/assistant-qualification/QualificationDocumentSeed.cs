using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace AssistantQualification;

// channel-documents only, after QualificationSeed and QualificationFormSeed in the same isolated database: the loaders of the subjects' role
// who send the spreadsheets (the subjects already make their ten interpretations with the documents) and the forms with document reading
// (QualificationFormSeed.CreateReadingFormsAsync). The other sets never create them, so their reads and counts do not change.
internal sealed class QualificationDocumentSeed(IUnitOfWork unitOfWork, IUserRepository users, QualificationFormSeed forms)
{
    private static readonly string[] Cultures = ["es", "en"];

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
}
