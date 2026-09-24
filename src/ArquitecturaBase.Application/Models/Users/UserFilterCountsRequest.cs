using ArquitecturaBase.Application.Models.Identity;

namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Usa los filtros del listado; paginado y orden permanecen en sus valores por omisión.</summary>
public sealed record UserFilterCountsRequest : UserListRequest;
