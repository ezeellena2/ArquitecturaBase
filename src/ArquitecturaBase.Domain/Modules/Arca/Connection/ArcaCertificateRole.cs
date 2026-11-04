namespace ArquitecturaBase.Domain.Modules.Arca.Connection;

/// <summary>A lo sumo hay un certificado activo y uno pendiente: así se renueva sin corte.</summary>
public enum ArcaCertificateRole
{
    Active,
    Pending,
}
