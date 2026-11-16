using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>
/// N22: a file received by WhatsApp that could not be downloaded from Meta says so with this module's code; the core's
/// <see cref="FileErrors"/> stay neutral.
/// </summary>
public static class WhatsAppMediaErrors
{
    public static readonly Error Unavailable = Error.Validation("WhatsApp.Media.Unavailable", "The file could not be downloaded from WhatsApp.");

    /// <summary>
    /// A download from Meta or the private storage may pass: the message retries it with the usual scale (H11). What the file is,
    /// or a copy that is gone, does not.
    /// </summary>
    public static bool MayPass(Error error) => error == Unavailable || error == FileErrors.StorageUnavailable;
}
