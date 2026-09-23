using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// La firma de los webhooks y la palabra de verificación (sección 7 del spec). La firma es el HMAC-SHA256 del cuerpo
/// crudo, byte por byte como llegó, con el secreto de la app: leerlo como JSON y volver a escribirlo cambia los bytes
/// (Meta manda una "á" escapada, como el texto <c>\u00e1</c>, y otro serializador la escribiría distinto). Se compara
/// en tiempo constante, así la respuesta no dice cuántos bytes coinciden. Solo se registra con el webhook prendido,
/// que es cuando están los dos secretos.
/// </summary>
internal sealed class WhatsAppSignatureValidator : IWhatsAppSignatureValidator
{
    /// <summary>El formato de Meta: <c>X-Hub-Signature-256: sha256=&lt;hex&gt;</c>.</summary>
    private const string SignaturePrefix = "sha256=";

    private readonly byte[] _appSecret;
    private readonly byte[] _verifyTokenHash;

    public WhatsAppSignatureValidator(IOptions<WhatsAppOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var settings = options.Value;

        if (!settings.HasWebhookSecrets)
        {
            throw new InvalidOperationException(
                "The WhatsApp webhook needs WhatsApp:AppSecret and WhatsApp:VerifyToken: it is registered only when both are set.");
        }

        _appSecret = Encoding.UTF8.GetBytes(settings.AppSecret!);
        _verifyTokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(settings.VerifyToken!));
    }

    public bool IsValidSignature(ReadOnlySpan<byte> body, string? signatureHeader)
    {
        if (signatureHeader is null || !signatureHeader.StartsWith(SignaturePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        Span<byte> received = stackalloc byte[HMACSHA256.HashSizeInBytes];

        // Exactamente 64 caracteres hexadecimales: uno de más no entra y uno de menos no llena los 32 bytes.
        var status = Convert.FromHexString(
            signatureHeader.AsSpan(SignaturePrefix.Length), received, out var charsConsumed, out var bytesWritten);

        if (status != OperationStatus.Done
            || bytesWritten != HMACSHA256.HashSizeInBytes
            || charsConsumed != signatureHeader.Length - SignaturePrefix.Length)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(_appSecret, body, expected);

        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    /// <summary>
    /// Se comparan los SHA-256 de las dos palabras y no las palabras: así los dos lados miden siempre lo mismo, y el
    /// tiempo de la comparación tampoco dice el largo de la palabra guardada.
    /// </summary>
    public bool IsValidVerifyToken(string? verifyToken)
    {
        if (string.IsNullOrEmpty(verifyToken))
        {
            return false;
        }

        Span<byte> received = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(verifyToken), received);

        return CryptographicOperations.FixedTimeEquals(_verifyTokenHash, received);
    }
}
