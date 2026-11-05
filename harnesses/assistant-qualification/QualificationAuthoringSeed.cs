using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace AssistantQualification;

// Fictitious accounts for the authoring sets only (IA1-A/IA3-A), after QualificationSeed and QualificationFormSeed in the same
// isolated database. The existing sets never create them: their users.summary facts count exactly the base accounts.
internal sealed class QualificationAuthoringSeed(IUnitOfWork unitOfWork, IUserRepository users, IRoleRepository roles,
    IEnumerable<IAssistantCapabilitySource> sources)
{
    private static readonly string[] Cultures = ["es", "en"];
    private static readonly string[] Building = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage,
        BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];

    public async Task<QualificationFixture> CreateAsync(QualificationFixture fixture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var home = sources.SelectMany(source => source.Definitions).Where(definition => QualificationSeed.HomeCapabilities.Contains(definition.Id))
            .SelectMany(definition => definition.RequiredPermissions.Actor).Append(ArtificialIntelligencePermissions.AssistantUse);
        var authors = new List<QualificationActor>();
        var channelAuthors = new List<QualificationActor>();
        var result = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Home: the five reads plus designing forms and managing catalogs, so Home offers the authoring and H4 capabilities.
            await roles.CreateAsync("QualificationAuthor", "Home with fictitious form and catalog authoring",
                home.Concat(Building).Distinct(StringComparer.Ordinal).ToArray(), token);
            // WhatsApp automatic: the recipient's own conversation, where Actor equals Subject.
            await roles.CreateAsync("QualificationChannelAuthor", "Own automatic conversation with fictitious form and catalog authoring",
                [..Building, BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead], token);
            for (var index = 0; index < 4; index++)
            {
                var culture = Cultures[index % 2];
                var author = await users.CreateAsync(Email.Create($"author{index}@example.invalid").Value, null, false,
                    $"Qualification author {index}", culture, token);
                await users.SetRolesAsync(author.Id, ["QualificationAuthor"], token);
                authors.Add(new(author.Id, $"author-{index}", culture, "QualificationAuthor"));
            }
            foreach (var culture in Cultures)
            {
                var recipient = await users.CreateAsync(Email.Create($"author-subject-{culture}@example.invalid").Value, null, false,
                    $"Authoring recipient {culture}", culture, token);
                await users.SetRolesAsync(recipient.Id, ["QualificationChannelAuthor"], token);
                channelAuthors.Add(new(recipient.Id, $"author-subject-{culture}", culture, "QualificationChannelAuthor"));
            }
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);
        if (result.IsFailure) throw new InvalidOperationException("Qualification authoring seed validation failed.");
        return fixture with { Authors = authors, ChannelAuthors = channelAuthors };
    }
}
