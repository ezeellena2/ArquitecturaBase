namespace ArquitecturaBase.Infrastructure.Emails;

internal enum EmailDelivery
{
    /// <summary>Envío real por SMTP (Gmail con contraseña de aplicación).</summary>
    Smtp,

    /// <summary>Archivos .eml en una carpeta local, para desarrollo.</summary>
    PickupDirectory,
}
