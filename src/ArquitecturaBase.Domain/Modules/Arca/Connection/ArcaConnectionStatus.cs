namespace ArquitecturaBase.Domain.Modules.Arca.Connection;

public enum ArcaConnectionStatus
{
    AwaitingCertificate,
    Unverified,
    Active,
    NeedsAttention,
}
