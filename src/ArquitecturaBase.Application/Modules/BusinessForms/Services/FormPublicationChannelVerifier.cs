using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormPublicationChannelVerifier(IEnumerable<IStructuredFormChannel> channels)
{
    public async Task<Result<IReadOnlyList<FormPublicationProfile>>> CheckAsync(FormDefinitionResponse saved, CancellationToken cancellationToken)
    {
        var installed = channels.OrderBy(channel => channel.ChannelId, StringComparer.Ordinal).ToArray();
        if (installed.Length == 0) return FormPublicationErrors.ChannelUnavailable;
        if (installed.Any(channel => string.IsNullOrWhiteSpace(channel.ChannelId))
            || installed.Select(channel => channel.ChannelId).Distinct(StringComparer.Ordinal).Count() != installed.Length)
            throw new InvalidOperationException("Structured form channel identifiers must be nonempty and unique.");
        var profiles = new List<FormPublicationProfile>();
        foreach (var channel in installed)
        {
            var result = await channel.CheckAsync(FormPublicationSnapshotPolicy.ChannelRequest(saved), cancellationToken);
            if (result.IsFailure) return result.Error;
            var check = result.Value;
            if (!check.PublicationAvailable) return FormPublicationErrors.AccountUnavailable;
            if (check.Compatibility != "LocallyValidated" || check.Issues.Count != 0) return FormPublicationErrors.CheckExpired;
            profiles.Add(new(channel.ChannelId, check.ProfileRevision,
                check.Cultures.Select(culture => new FormPublicationCultureProfile(culture.Culture, culture.CompilationFingerprint)).ToArray()));
        }
        return profiles.AsReadOnly();
    }
}
