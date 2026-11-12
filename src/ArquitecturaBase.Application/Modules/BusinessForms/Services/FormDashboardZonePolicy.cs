using ArquitecturaBase.Application.Interfaces.Persistence;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
/// <summary>
/// The zone of the dashboard's buckets (DOC7-A): the one the front builds the sheet's filter in, the profile's of whoever reads, so a
/// day is the same in the sheet and in the dashboard; then the business zone of the presentation settings, and then UTC. Read on the
/// server, never from the request, by its IANA id, the one PostgreSQL reads: the profile also admits a zone by its Windows name.
/// </summary>
internal sealed class FormDashboardZonePolicy(IUserReader users, ISystemSettingsReader settings)
{
    public async Task<(TimeZoneInfo Zone, string Id)> FindAsync(Guid actor, CancellationToken cancellationToken)
    {
        var account = await users.FindByIdAsync(actor, cancellationToken);
        if (account is not null && Iana(account.TimeZoneId) is { } own) return own;
        return Iana((await settings.FindPresentationAsync(cancellationToken)).DefaultTimeZoneId) ?? (TimeZoneInfo.Utc, "UTC");
    }

    private static (TimeZoneInfo Zone, string Id)? Iana(string id) =>
        !TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? null
        : zone.HasIanaId ? (zone, zone.Id)
        : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? (zone, iana) : null;
}
