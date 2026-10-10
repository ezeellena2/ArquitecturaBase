using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormPreviewService(IFormPublicationReader reader, IFormPublicationProtector protector,
    IEnumerable<IStructuredFormChannel> channels, ICurrentUser currentUser, IPermissionService permissions, TimeProvider timeProvider) : IFormPreviewService
{
    public async Task<Result<StructuredFormPreviewResponse>> GetAsync(Guid definitionId, Guid versionId, string? culture, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        if (!await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Read, cancellationToken)) return FormDefinitionErrors.Forbidden;
        if (culture is not ("es" or "en")) return FieldErrors.On(FormPreviewErrors.CultureInvalid, "culture");
        var version = await reader.FindVersionAsync(definitionId, versionId, cancellationToken); if (version is null) return FormPublicationErrors.NotFound;
        if (version.State != "Published") return FormPreviewErrors.NotAvailable;
        var manifest = protector.ReadManifest(new(version.Id, version.Actor, version.RequestId, version.DefinitionId, version.VersionId), version.ProtectedManifest);
        var profile = manifest.Profiles.SingleOrDefault(value => value.Cultures.Any(target => target.Culture == culture));
        if (profile is null) return FormPreviewErrors.NotAvailable;
        var channel = channels.SingleOrDefault(value => value.ChannelId == profile.ChannelId); if (channel is null) return FormPreviewErrors.NotAvailable;
        var result = await channel.GetPreviewAsync(new(version.Id, definitionId, versionId, manifest.PublicationFingerprint, culture), cancellationToken);
        if (result.IsFailure) return result.Error;
        return result.Value.ExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime ? FormPreviewErrors.Expired : result;
    }
}
