namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>Verifies Meta's subscription token and accepts signed raw webhook bytes.</summary>
public interface IWhatsAppWebhookService
{
    bool IsValidVerifyToken(string? token);

    Task<bool> ReceiveAsync(ReadOnlyMemory<byte> body, string? signature, CancellationToken cancellationToken);
}
