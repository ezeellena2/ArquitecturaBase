using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>PKCE con S256, como lo hace oidc-client-ts.</summary>
internal static class Pkce
{
    public static string CreateVerifier() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string ChallengeOf(string verifier) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}
