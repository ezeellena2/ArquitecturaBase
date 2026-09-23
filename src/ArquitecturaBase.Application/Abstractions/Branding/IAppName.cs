namespace ArquitecturaBase.Application.Abstractions.Branding;

/// <summary>
/// El nombre del sistema que ven las personas ("Arquitectura Base"): el mismo de los correos, <c>Email:AppName</c>.
/// Lo usan los mensajes que arma Application, como los del bot de WhatsApp, para no repetir el nombre en otro lado.
/// </summary>
public interface IAppName
{
    string Value { get; }
}
