using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class BusinessFormPublicationService : IBusinessFormPublicationService
{
    private readonly ILogger<BusinessFormPublicationService> _logger;
    private readonly IFormDefinitionReader _reader;
    private readonly IRequestValidator _validator;
    private readonly FormPublicationGuard _guard;
    private readonly IStructuredFormChannel[] _channels;
    private readonly IFormPublicationReader _publicationReader;
    private readonly IFormPublicationProtector _protector;
    private readonly TimeProvider _timeProvider;
    public BusinessFormPublicationService(IFormDefinitionReader reader, IRequestValidator validator, FormPublicationGuard guard,
        IEnumerable<IStructuredFormChannel> channels, IFormPublicationReader publicationReader,
        IFormPublicationProtector protector, TimeProvider timeProvider, ILogger<BusinessFormPublicationService> logger)
    {
        _logger = logger;
        _reader = reader; _validator = validator; _guard = guard;
        _publicationReader = publicationReader; _protector = protector; _timeProvider = timeProvider;
        _channels = channels.OrderBy(channel => channel.ChannelId, StringComparer.Ordinal).ToArray();
        if (_channels.Any(channel => string.IsNullOrWhiteSpace(channel.ChannelId))
            || _channels.Select(channel => channel.ChannelId).Distinct(StringComparer.Ordinal).Count() != _channels.Length)
            throw new InvalidOperationException("Structured form channel identifiers must be nonempty and unique.");
    }
    public Task<Result<FormPublicationCheckResponse>> CheckAsync(Guid id, CheckFormPublicationRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormPublicationCheckResponse>>(_logger, "CheckBusinessFormPublication", async () =>
    {
        var access = await _guard.CheckAsync(cancellationToken);
        if (access.IsFailure) return access.Error;
        var actor = access.Value;
        // The existing structural validator uses the actual definition ID only to validate a nonempty UUID.
        // This query has no request deduplication, receipt, identity allocation or write boundary.
        var input = new SaveFormDraftRequest(id, FormDraftPolicy.NormalizeName(request.Name)!, request.Prefix?.Trim().ToUpperInvariant()!, request.Draft, request.ExpectedRevision);
        if (await _validator.ValidateAsync(input, cancellationToken) is { } invalid) return invalid;
        var row = await _reader.FindAsync(id, cancellationToken); if (row is null) return FormDefinitionErrors.NotFound;
        if (row.Revision != request.ExpectedRevision) return FormDraftErrors.RevisionConflict;
        input = FormDraftPolicy.Normalize(input);
        var saved = FormDraftPolicy.Definition(row.Id, row.NameJson, row.Prefix, row.PrefixLocked, row.Revision, row.Culture, row.SubmitLabelJson, row.SchemaJson);
        if (!FormPublicationSnapshotPolicy.Matches(input, saved)) return FormPublicationErrors.DraftMustBeSaved;
        var channels = new List<FormPublicationChannelResponse>(); var issues = FormPublicationPolicy.MissingTranslations(saved);
        issues.AddRange(_guard.ValidateFiles(saved));
        issues.AddRange(await _guard.ValidateCatalogsAsync(saved.Draft.Fields, cancellationToken));
        if (_channels.Length == 0)
            issues.Add(new(null, null, null, "channel", FormPublicationErrors.ChannelUnavailable.Code,
                ErrorMessages.Get(FormPublicationErrors.ChannelUnavailable.Code), null, null));
        var canonical = FormPublicationSnapshotPolicy.ChannelRequest(saved);
        var profiles = new List<FormPublicationProfile>();
        var publicationAvailable = _channels.Length > 0;
        foreach (var channel in issues.Count == 0 ? _channels : [])
        {
            var result = await channel.CheckAsync(canonical, cancellationToken); if (result.IsFailure) return result.Error;
            var check = result.Value;
            publicationAvailable &= check.PublicationAvailable;
            profiles.Add(new(channel.ChannelId, check.ProfileRevision,
                check.Cultures.Select(culture => new FormPublicationCultureProfile(culture.Culture, culture.CompilationFingerprint)).ToArray()));
            channels.Add(new(channel.ChannelId, check.ProfileRevision, check.Compatibility, check.ExternalEvidence, check.Cultures.Select(culture => culture.Culture).ToArray()));
            issues.AddRange(check.Issues.Select(issue => new FormPublicationIssueResponse(channel.ChannelId, issue.FieldId, issue.ChoiceId,
                issue.Path, issue.Error.Code, ErrorMessages.Get(issue.Error.Code), issue.Limit, issue.Culture)));
        }
        var compatibility = _channels.Length == 0 ? "Unavailable" : issues.Count == 0 ? "LocallyValidated" : "Blocked";
        var open = await _publicationReader.ExistsOpenOperationAsync(id, cancellationToken);
        var canAccept = compatibility == "LocallyValidated" && publicationAvailable && !open;
        // The check is advisory: acceptance assigns the number again under the existing definition lock.
        // Every accepted version counts, including failed publications; an active version is not the counter.
        var nextVersionNumber = checked((long)await _publicationReader.CountVersionsAsync(id, cancellationToken) + 1);
        var expiresAtUtc = canAccept ? _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(10) : (DateTime?)null;
        var token = canAccept ? _protector.ProtectTicket(new(actor, id, row.Revision,
            FormPublicationSnapshotPolicy.Fingerprint(saved, profiles), expiresAtUtc!.Value)) : null;
        return new FormPublicationCheckResponse(id, row.Revision, compatibility, canAccept,
            open ? "InProgress" : publicationAvailable ? "Available" : "Unavailable", channels.AsReadOnly(), issues.AsReadOnly(),
            await DiffAsync(id, saved, cancellationToken), nextVersionNumber, token, expiresAtUtc);
    });
    private async Task<FormPublicationDiffResponse> DiffAsync(Guid id, FormDefinitionResponse saved, CancellationToken ct)
    {
        var active = await _publicationReader.FindActiveVersionAsync(id, ct);
        if (active is null) return new(null, saved.Draft.Fields.Select(field => field.FieldId).ToArray(), [], []);
        var schema = System.Text.Json.JsonSerializer.Deserialize<FormVersionSchema>(active.SchemaJson, System.Text.Json.JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The active form schema is invalid.");
        var previous = schema.Questions.ToDictionary(field => field.FieldId);
        var current = saved.Draft.Fields.ToDictionary(field => field.FieldId);
        return new(active.VersionId, saved.Draft.Fields.Where(field => !previous.ContainsKey(field.FieldId)).Select(field => field.FieldId).ToArray(),
            saved.Draft.Fields.Where(field => previous.TryGetValue(field.FieldId, out var old) && FormDraftPolicy.Serialize(field) != FormDraftPolicy.Serialize(old)).Select(field => field.FieldId).ToArray(),
            schema.Questions.Where(field => !current.ContainsKey(field.FieldId)).Select(field => field.FieldId).ToArray());
    }
}
