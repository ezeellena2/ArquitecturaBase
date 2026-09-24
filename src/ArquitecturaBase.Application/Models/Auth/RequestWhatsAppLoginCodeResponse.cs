namespace ArquitecturaBase.Application.Models.Auth;

/// <summary>La misma respuesta exista o no la cuenta; Phone es el destino normalizado para verificar el código.</summary>
public sealed record RequestWhatsAppLoginCodeResponse(int ResendAfterSeconds, string Phone, string MaskedPhone)
{
    public override string ToString() => nameof(RequestWhatsAppLoginCodeResponse);
}
