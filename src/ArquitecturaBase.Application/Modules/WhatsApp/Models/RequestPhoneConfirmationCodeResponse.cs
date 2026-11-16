namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>
/// El código que confirma agregar un correo salió al número verificado de la cuenta: cuándo se puede pedir otro y el
/// número enmascarado, para decir adónde llegó.
/// </summary>
public sealed record RequestPhoneConfirmationCodeResponse(int ResendAfterSeconds, string MaskedPhone);
