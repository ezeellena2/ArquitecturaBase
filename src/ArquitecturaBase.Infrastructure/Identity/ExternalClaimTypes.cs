namespace ArquitecturaBase.Infrastructure.Identity;

internal static class ExternalClaimTypes
{
    /// <summary>Google lo manda como booleano; al mapearlo queda "True"/"False".</summary>
    public const string EmailVerified = "email_verified";
}
