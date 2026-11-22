using System.Diagnostics;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

/// <summary>
/// Registration mode, presentation and branding in Redis for at most one minute; the sign-in retention periods straight from
/// the database, because the retention reads them once per run. Factories read committed data in their own scope.
/// If Redis does not answer, the same projection is read from the database with the request's context, without caching
/// it (N9): the sign-in keeps working. Without the settings row both reads give the defaults, as the factory does. While
/// Redis is down every read goes to the database, so the log gets one Warning per process and per minute, with the
/// exception; the other reads go to Debug, without it.
/// </summary>
internal sealed partial class SystemSettingsReader(RedisCache cache, ApplicationDbContext dbContext, ILogger<SystemSettingsReader> logger)
    : ISystemSettingsReader
{
    private static readonly TimeSpan Expiration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan WarningWindow = TimeSpan.FromMinutes(1);

    // Stopwatch timestamp of the last Warning, shared by every read of the process; 0 before the first one.
    private static long lastWarning;

    public async Task<SystemPresentationResponse> FindPresentationAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetOrCreateInOwnScopeAsync<ApplicationDbContext, int, SystemPresentationResponse>(
                SystemSettingsCache.PresentationKey, 0, static (db, _, token) => ReadPresentationAsync(db, token),
                Expiration, cancellationToken);
        }
        catch (Exception exception) when (RedisCache.IsUnavailable(exception))
        {
            LogReadFromDatabase(exception);
            return await ReadPresentationAsync(dbContext, cancellationToken);
        }
    }

    public async Task<RegistrationMode> FindRegistrationModeAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetOrCreateInOwnScopeAsync<ApplicationDbContext, int, RegistrationMode>(
                SystemSettingsCache.CacheKey, 0, static (db, _, token) => ReadRegistrationModeAsync(db, token),
                Expiration, cancellationToken);
        }
        catch (Exception exception) when (RedisCache.IsUnavailable(exception))
        {
            LogReadFromDatabase(exception);
            return await ReadRegistrationModeAsync(dbContext, cancellationToken);
        }
    }

    // RC6-B: every email, invitation and PDF reads it, like the presentation.
    public async Task<SystemBrandingResponse> FindBrandingAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetOrCreateInOwnScopeAsync<ApplicationDbContext, int, SystemBrandingResponse>(
                SystemSettingsCache.BrandingKey, 0, static (db, _, token) => ReadBrandingAsync(db, token),
                Expiration, cancellationToken);
        }
        catch (Exception exception) when (RedisCache.IsUnavailable(exception))
        {
            LogReadFromDatabase(exception);
            return await ReadBrandingAsync(dbContext, cancellationToken);
        }
    }

    // RC1-A: once per run, so without the cache; what the administrator saved counts from the next run.
    public async Task<IdentityRetentionResponse> FindIdentityRetentionAsync(CancellationToken cancellationToken) =>
        await dbContext.SystemSettings.AsNoTracking()
            .Select(settings => new IdentityRetentionResponse(settings.LoginAuditRetentionMonths, settings.LoginCodeRetentionDays,
                settings.InvitationRetentionDays))
            .FirstOrDefaultAsync(cancellationToken) ?? IdentityRetentionResponse.Defaults;

    private static async Task<SystemBrandingResponse> ReadBrandingAsync(ApplicationDbContext db, CancellationToken cancellationToken) =>
        await db.SystemSettings.AsNoTracking()
            .Select(settings => new SystemBrandingResponse(settings.AppName, settings.EmailSenderName, settings.EmailLogoUrl))
            .FirstOrDefaultAsync(cancellationToken) ?? SystemBrandingResponse.Defaults;

    private static async Task<SystemPresentationResponse> ReadPresentationAsync(ApplicationDbContext db, CancellationToken cancellationToken) =>
        await db.SystemSettings.AsNoTracking()
            .Select(settings => new SystemPresentationResponse(settings.DefaultCulture, settings.DefaultTimeZoneId, settings.DefaultPageSize,
                new SystemAppearanceResponse(settings.PrimaryColor, settings.MenuColor, settings.HeaderColor, settings.AlternateRowColor)))
            .FirstOrDefaultAsync(cancellationToken) ?? SystemPresentationResponse.Defaults;

    // Without the row, default(RegistrationMode) is InviteOnly: the closed mode.
    private static Task<RegistrationMode> ReadRegistrationModeAsync(ApplicationDbContext db, CancellationToken cancellationToken) =>
        db.SystemSettings.AsNoTracking()
            .Select(settings => settings.RegistrationMode).FirstOrDefaultAsync(cancellationToken);

    private void LogReadFromDatabase(Exception exception)
    {
        if (TryOpenWarningWindow(ref lastWarning, Stopwatch.GetTimestamp(), WarningWindow))
        {
            ReadFromDatabase(logger, exception);
        }
        else
        {
            ReadFromDatabaseAgain(logger);
        }
    }

    /// <summary>
    /// True for the first read of a window: none before, or the last Warning at least <paramref name="window"/> ago (in
    /// Stopwatch timestamps). Of the reads that race for a new window only the one that swaps the timestamp gets it.
    /// </summary>
    internal static bool TryOpenWarningWindow(ref long last, long now, TimeSpan window)
    {
        var previous = Interlocked.Read(ref last);
        return (previous == 0 || Stopwatch.GetElapsedTime(previous, now) >= window)
            && Interlocked.CompareExchange(ref last, now, previous) == previous;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis did not respond: the system settings were read from the database.")]
    private static partial void ReadFromDatabase(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Redis did not respond again: the system settings were read from the database; the Warning comes once a minute.")]
    private static partial void ReadFromDatabaseAgain(ILogger logger);
}
