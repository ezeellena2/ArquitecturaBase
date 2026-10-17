using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;

namespace AssistantQualification;

internal sealed record QualificationActor(Guid UserId, string Alias, string Culture, string RoleName);
internal sealed record QualificationFixture(IReadOnlyList<QualificationActor> Actors, string SupervisorRoleName)
{
    public Guid SupervisorRoleId { get; init; }
    public IReadOnlyList<QualificationActor> AudioActors { get; init; } = [];
    public IReadOnlyList<QualificationActor> Subjects { get; init; } = [];
    public IReadOnlyList<QualificationActor> Operators { get; init; } = [];
}

internal sealed class QualificationSeed(IUnitOfWork unitOfWork, IUserRepository users, IRoleRepository roles,
    ISystemSettingsRepository settings, IAiBusinessInformationRepository business,
    IEnumerable<IAssistantCapabilitySource> querySources)
{
    private static readonly string[] HomeSupervisorRoles = ["QualificationHome", "Supervisor"];
    private static readonly string[] Cultures = ["es", "en"];
    private static readonly string[] HomeRoles = ["QualificationHome"];
    internal static readonly string[] HomeCapabilities = ["business.information", "roles.list", "roles.permissions", "system.help", "users.summary"];

    // Execute once against a verified EMPTY, newly created evaluation database before starting the host.
    // These are fictitious accounts; no administration service, invitation, email, Meta call or production seed is used.
    public async Task<QualificationFixture> CreateAsync(CancellationToken cancellationToken)
    {
        var permissions = querySources.SelectMany(source => source.Definitions).Where(definition => HomeCapabilities.Contains(definition.Id))
            .SelectMany(definition => definition.RequiredPermissions.Actor)
            .Append(ArtificialIntelligencePermissions.AssistantUse).Distinct(StringComparer.Ordinal).ToArray();
        var actors = new List<QualificationActor>();
        Guid supervisorRoleId = default;
        var audioActors = new List<QualificationActor>();
        var subjects = new List<QualificationActor>();
        var operators = new List<QualificationActor>();
        var result = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await roles.CreateAsync(SystemRoles.User, "Qualification base role", [], token);
            supervisorRoleId = await roles.CreateAsync("Supervisor", "Fictitious role for exact selector arguments", [Permissions.Users.Read], token);
            await roles.CreateAsync("QualificationHome", "Only the five Home read capabilities", permissions, token);
            await roles.CreateAsync("QualificationAudio", "Only private chat audio reading", [WhatsAppPermissions.Read], token);
            await roles.CreateAsync("QualificationSubject", "Only fictitious form capture", [BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead], token);
            await roles.CreateAsync("QualificationOperator", "Fictitious forms administration without Home", BusinessFormsPermissions.All.Concat(WhatsAppPermissions.All).ToArray(), token);
            settings.Add(SystemSettings.Create(RegistrationMode.InviteOnly));
            var information = AiBusinessInformation.Create();
            var updated = information.Update("Taller de prueba Norte", "Fictitious qualification fixture.",
                "Monday-Friday 09:00-17:00", "Qualification site A", "Synthetic inspections", "No real customers", "");
            if (updated.IsFailure) return updated;
            business.Add(information);
            for (var index = 0; index < 8; index++)
            {
                var culture = index % 2 == 0 ? "es" : "en";
                var actor = await users.CreateAsync(Email.Create($"actor{index}@example.invalid").Value,
                    null, false, $"Qualification actor {index}", culture, token);
                await users.SetRolesAsync(actor.Id, index == 0 ? HomeSupervisorRoles : HomeRoles, token);
                actors.Add(new(actor.Id, $"home-{index}", culture, "QualificationHome"));
            }
            foreach (var culture in Cultures)
            {
                var suffix = culture == "es" ? "01" : "02";
                var audioActor = await users.CreateAsync(Email.Create($"audio-{culture}@example.invalid").Value,
                    PhoneNumber.Create("+54935155504" + suffix).Value, true, $"Audio reader {culture}", culture, token);
                await users.SetRolesAsync(audioActor.Id, ["QualificationAudio"], token);
                audioActors.Add(new(audioActor.Id, $"audio-{culture}", culture, "QualificationAudio"));
                var subject = await users.CreateAsync(Email.Create($"subject-{culture}@example.invalid").Value,
                    PhoneNumber.Create("+54935155502" + suffix).Value, true, $"Form recipient {culture}", culture, token);
                await users.SetRolesAsync(subject.Id, ["QualificationSubject"], token);
                subjects.Add(new(subject.Id, $"subject-{culture}", culture, "QualificationSubject"));
                var actor = await users.CreateAsync(Email.Create($"operator-{culture}@example.invalid").Value,
                    PhoneNumber.Create("+54935155503" + suffix).Value, true, $"Form operator {culture}", culture, token);
                await users.SetRolesAsync(actor.Id, ["QualificationOperator"], token);
                operators.Add(new(actor.Id, $"operator-{culture}", culture, "QualificationOperator"));
            }
            var inactive = await users.CreateAsync(Email.Create("inactive@example.invalid").Value, null,
                false, "Inactive fixture account", "es", token);
            await users.SetActiveAsync(inactive.Id, false, token);
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);
        if (result.IsFailure) throw new InvalidOperationException("Qualification seed validation failed.");
        return new(actors, "Supervisor") { SupervisorRoleId=supervisorRoleId, AudioActors=audioActors, Subjects=subjects, Operators=operators };
    }
}
