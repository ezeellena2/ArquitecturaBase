namespace ArquitecturaBase.Application.Interfaces.Integrations.Emails;

/// <summary>
/// N7: the warnings of the daily limit of the email (<c>EmailQuotaGuard</c>) go out once a day in each copy, not once per
/// request: the day can stay above 80 % of the limit, or at the limit, for hours. It lives in the memory of each copy, like
/// the limit per address of the WhatsApp codes: a restart can repeat a warning, and each copy warns on its own.
/// </summary>
public interface IEmailQuotaWarnings
{
    /// <summary>True for the first request of the day that finds the emails at 80 % of the limit: that one warns.</summary>
    bool TryWarnNear(DateTime nowUtc);

    /// <summary>True for the first request of the day that finds the emails at the limit: that one warns.</summary>
    bool TryWarnReached(DateTime nowUtc);
}
