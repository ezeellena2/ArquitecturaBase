namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>External payloads already sent by Meta templates and historical reply buttons.</summary>
public static class WhatsAppAccessActions
{
    public const string WantToEnter = "WANT_TO_ENTER";
    public const string HaveAccount = "HAVE_ACCOUNT";
    public const string CreateAccount = "CREATE_ACCOUNT";
    public const string DidNotRequestCode = "DID_NOT_REQUEST_CODE";

    public static bool IsKnown(string? id) => id is WantToEnter or HaveAccount or CreateAccount or DidNotRequestCode;
}
