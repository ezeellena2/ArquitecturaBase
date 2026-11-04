namespace ArquitecturaBase.Domain.Modules.Arca.Connection;

/// <summary>Lo que encuentra un host al querer renovar un ticket de acceso.</summary>
public enum ArcaTicketLease
{
    Acquired,
    AlreadyValid,
    Busy,
    Blocked,
}
