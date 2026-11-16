namespace ArquitecturaBase.Application.Models.Identity;

/// <summary>
/// Un tramo de la poda de OpenIddict: cuántos tokens y cuántas autorizaciones podó, y desde qué fecha de creación sigue el
/// próximo tramo (null si llegó al umbral final).
/// </summary>
public sealed record OpenIddictPruneTranche(long Tokens, long Authorizations, DateTime? NextFromUtc);
