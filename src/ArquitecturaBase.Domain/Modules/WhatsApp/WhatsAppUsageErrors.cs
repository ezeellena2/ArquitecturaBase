using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>Consumo de WhatsApp (T7, 2026-11-27): the month asked for and Meta, which reports what the account spent.</summary>
public static class WhatsAppUsageErrors
{
    public static readonly Error MonthInvalid = Error.Validation("WhatsApp.Usage.MonthInvalid", "The month goes as yyyy-MM.");
    public static readonly Error MonthOutOfRange = Error.Validation("WhatsApp.Usage.MonthOutOfRange",
        "The month goes from the current one back to eleven before: Meta keeps a year of usage.");
    public static readonly Error Unavailable = Error.Failure("WhatsApp.Usage.Unavailable", "Meta did not report the WhatsApp usage.");
}
