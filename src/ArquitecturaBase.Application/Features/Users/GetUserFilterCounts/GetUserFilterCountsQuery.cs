using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.GetUserFilterCounts;

/// <summary>
/// Los mismos filtros que el listado. <b>Ignora Page, PageSize y Sort:</b> cuenta, no lista, así que el
/// endpoint no los enlaza y quedan siempre en sus valores por omisión.
/// </summary>
public sealed record GetUserFilterCountsQuery : UserListRequest, IQuery<UserFilterCounts>;
