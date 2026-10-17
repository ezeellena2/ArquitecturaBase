using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;

namespace AssistantQualification;

internal sealed record QualificationActor(Guid UserId, string Alias, string Culture, string RoleName);
internal sealed record QualificationFixture(IReadOnlyList<QualificationActor> Actors, string SupervisorRoleName);

internal sealed class QualificationSeed(IUnitOfWork unitOfWork, IUserRepository users, IRoleRepository roles,
    ISystemSettingsRepository settings, IAiBusinessInformationRepository business,
    IEnumerable<IAssistantCapabilitySource> querySources)
{
    internal static readonly string[] HomeCapabilities = ["business.information", "roles.list", "roles.permissions", "system.help", "users.summary"];

    // Execute once against a verified EMPTY, newly created evaluation database before starting the host.
    // These are fictitious accounts; no administration service, invitation, email, Meta call or production seed is used.
    public async Task<QualificationFixture> CreateAsync(CancellationToken cancellationToken)
    {
        var permissions = querySources.SelectMany(source => source.Definitions).Where(definition => HomeCapabilities.Contains(definition.Id))
            .SelectMany(definition => definition.RequiredPermissions.Actor)
            .Append(ArtificialIntelligencePermissions.AssistantUse).Distinct(StringComparer.Ordinal).ToArray();
        var actors = new List<QualificationActor>();
        var result = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await roles.CreateAsync(SystemRoles.User, "Qualification base role", [], token);
            await roles.CreateAsync("Supervisor", "Fictitious role for exact selector arguments", [Permissions.Users.Read], token);
            await roles.CreateAsync("QualificationHome", "Only the five Home read capabilities", permissions, token);
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
                await users.SetRolesAsync(actor.Id, ["QualificationHome"], token);
                actors.Add(new(actor.Id, $"home-{index}", culture, "QualificationHome"));
            }
            var inactive = await users.CreateAsync(Email.Create("inactive@example.invalid").Value, null,
                false, "Inactive fixture account", "es", token);
            await users.SetActiveAsync(inactive.Id, false, token);
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);
        if (result.IsFailure) throw new InvalidOperationException("Qualification seed validation failed.");
        return new(actors, "Supervisor");
    }
}
