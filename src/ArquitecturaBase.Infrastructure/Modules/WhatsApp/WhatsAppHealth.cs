namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>
/// H9: qué permiso del token usa cada cliente de Meta. Mensajes, descarga de medios y subida de documentos van con
/// <c>whatsapp_business_messaging</c>; Flows y <c>phone_numbers</c>, con <c>whatsapp_business_management</c>.
/// </summary>
internal enum WhatsAppTokenUse
{
    Messaging,
    Management,
}

/// <summary>
/// Lo que la app sabe del token por lo que contestó Meta a cualquiera de sus clientes (H9): lo rechazó, o le falta el permiso
/// de una de sus dos familias. Lo lee <see cref="WhatsAppHealthCheck"/> (sección 9 del spec: "error en el log y en la salud de
/// la app"). Vive en memoria: un reinicio arranca sin problema anotado.
/// </summary>
internal sealed class WhatsAppHealth
{
    private readonly Lock _lock = new();
    private bool _invalidToken;
    private bool _messagingPermissionMissing;
    private bool _managementPermissionMissing;

    /// <summary>
    /// <see cref="WhatsAppSendFailure.InvalidToken"/> si Meta rechazó el token y ninguna llamada anduvo después;
    /// <see cref="WhatsAppSendFailure.MissingPermission"/> si a alguna familia le falta el permiso y ninguna llamada de esa familia
    /// anduvo después; <c>null</c> si no hay problema.
    /// </summary>
    public WhatsAppSendFailure? TokenProblem
    {
        get
        {
            lock (_lock)
            {
                return _invalidToken ? WhatsAppSendFailure.InvalidToken
                    : _messagingPermissionMissing || _managementPermissionMissing ? WhatsAppSendFailure.MissingPermission : null;
            }
        }
    }

    /// <summary>A qué familias les falta el permiso, para que el chequeo nombre el que hay que dar.</summary>
    public (bool Messaging, bool Management) MissingPermissions
    {
        get
        {
            lock (_lock)
            {
                return (_messagingPermissionMissing, _managementPermissionMissing);
            }
        }
    }

    public void ReportTokenProblem(WhatsAppSendFailure failure, WhatsAppTokenUse use = WhatsAppTokenUse.Messaging)
    {
        if (!failure.IsTokenProblem())
        {
            throw new ArgumentOutOfRangeException(nameof(failure), failure, "Only token problems affect the WhatsApp health.");
        }

        lock (_lock)
        {
            if (failure == WhatsAppSendFailure.InvalidToken) _invalidToken = true;
            else if (use == WhatsAppTokenUse.Messaging) _messagingPermissionMissing = true;
            else _managementPermissionMissing = true;
        }
    }

    /// <summary>
    /// H9: what any call to Meta answered says about the token, from every client: a 401 is the token and a 403 a permission of
    /// that client's family, and an answer that worked clears them. Another status says nothing about the token.
    /// </summary>
    public void Observe(System.Net.HttpStatusCode status, WhatsAppTokenUse use)
    {
        if (status == System.Net.HttpStatusCode.Unauthorized) ReportTokenProblem(WhatsAppSendFailure.InvalidToken, use);
        else if (status == System.Net.HttpStatusCode.Forbidden) ReportTokenProblem(WhatsAppSendFailure.MissingPermission, use);
        else if ((int)status is >= 200 and < 300) ReportSent(use);
    }

    /// <summary>
    /// Una llamada que anduvo prueba que el token vale y que su familia tiene el permiso: por ejemplo, si en Meta le asignaron la
    /// cuenta de WhatsApp al usuario del sistema, que no cambia el token. No dice nada del permiso de la otra familia.
    /// </summary>
    public void ReportSent(WhatsAppTokenUse use = WhatsAppTokenUse.Messaging)
    {
        lock (_lock)
        {
            _invalidToken = false;
            if (use == WhatsAppTokenUse.Messaging) _messagingPermissionMissing = false;
            else _managementPermissionMissing = false;
        }
    }
}
