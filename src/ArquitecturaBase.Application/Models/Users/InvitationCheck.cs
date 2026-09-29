namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Lo que mira un canal de invitación, con el nombre de cada campo en el cuerpo del alta o del reenvío.</summary>
public sealed record InvitationCheck(
    bool HasEmail,
    bool HasPhone,
    bool Consent,
    string? DisplayName,
    string ChannelField,
    string ConsentField,
    string DisplayNameField);
